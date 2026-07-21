using ARMeilleure.State;
using Ryujinx.Common.Logging;
using Ryujinx.Cpu.AppleHv.Arm;
using Ryujinx.Memory.Tracking;
using System;
using System.Runtime.Versioning;
using System.Threading;

namespace Ryujinx.Cpu.AppleHv
{
    [SupportedOSPlatform("macos")]
    class HvExecutionContext : IExecutionContext
    {
        /// <inheritdoc/>
        public ulong Pc
        {
            get
            {
                uint currentEl = Pstate & ~((uint)ExceptionLevel.PstateMask);
                if (currentEl == (uint)ExceptionLevel.EL1h)
                    return _impl.ElrEl1;
                return _impl.Pc;
            }
        }

        /// <inheritdoc/>
        public long TpidrEl0
        {
            get => _impl.TpidrEl0;
            set => _impl.TpidrEl0 = value;
        }

        /// <inheritdoc/>
        public long TpidrroEl0
        {
            get => _impl.TpidrroEl0;
            set => _impl.TpidrroEl0 = value;
        }

        /// <inheritdoc/>
        public uint Pstate
        {
            get => _impl.Pstate;
            set => _impl.Pstate = value;
        }

        /// <inheritdoc/>
        public uint Fpcr
        {
            get => _impl.Fpcr;
            set => _impl.Fpcr = value;
        }

        /// <inheritdoc/>
        public uint Fpsr
        {
            get => _impl.Fpsr;
            set => _impl.Fpsr = value;
        }

        /// <inheritdoc/>
        public ulong ThreadUid { get; set; }

        /// <inheritdoc/>
        public bool IsAarch32
        {
            get => false;
            set
            {
                if (value)
                    throw new NotSupportedException();
            }
        }

        /// <inheritdoc/>
        public bool Running { get; private set; }

        private readonly ICounter _counter;
        private readonly IHvExecutionContext _shadowContext;
        private IHvExecutionContext _impl;
        private int _shouldStep;
        private readonly ExceptionCallbacks _exceptionCallbacks;
        private int _interruptRequested;

        // GPU Sync control
        private int _syncCounter;
        private int _strongSyncCounter;

        public HvExecutionContext(ICounter counter, ExceptionCallbacks exceptionCallbacks)
        {
            _counter = counter;
            _shadowContext = new HvExecutionContextShadow();
            _impl = _shadowContext;
            _exceptionCallbacks = exceptionCallbacks;
            Running = true;
        }

        /// <inheritdoc/>
        public ulong GetX(int index) => _impl.GetX(index);

        /// <inheritdoc/>
        public void SetX(int index, ulong value) => _impl.SetX(index, value);

        /// <inheritdoc/>
        public V128 GetV(int index) => _impl.GetV(index);

        /// <inheritdoc/>
        public void SetV(int index, V128 value) => _impl.SetV(index, value);

        private void InterruptHandler() => _exceptionCallbacks.InterruptCallback?.Invoke(this);
        private void BreakHandler(ulong address, int imm) => _exceptionCallbacks.BreakCallback?.Invoke(this, address, imm);
        private void StepHandler() => _exceptionCallbacks.StepCallback?.Invoke(this);
        private void SupervisorCallHandler(ulong address, int imm) => _exceptionCallbacks.SupervisorCallback?.Invoke(this, address, imm);
        private void UndefinedHandler(ulong address, int opCode) => _exceptionCallbacks.UndefinedCallback?.Invoke(this, address, opCode);

        /// <inheritdoc/>
        public void RequestInterrupt()
        {
            if (Interlocked.Exchange(ref _interruptRequested, 1) == 0 && _impl is HvExecutionContextVcpu impl)
                impl.RequestInterrupt();
        }

        private bool GetAndClearInterruptRequested()
        {
            return Interlocked.Exchange(ref _interruptRequested, 0) != 0;
        }

        /// <inheritdoc/>
        public void RequestDebugStep()
        {
            Interlocked.Exchange(ref _shouldStep, 1);
        }

        /// <inheritdoc/>
        public ulong DebugPc
        {
            get => Pc;
            set
            {
                uint currentEl = Pstate & ~((uint)ExceptionLevel.PstateMask);
                if (currentEl == (uint)ExceptionLevel.EL1h)
                    _impl.ElrEl1 = value;
                else
                    _impl.Pc = value;
            }
        }

        /// <inheritdoc/>
        public void StopRunning()
        {
            Running = false;
            RequestInterrupt();
        }

        public unsafe void Execute(HvMemoryManager memoryManager, ulong address)
        {
            HvVcpu vcpu = HvVcpuPool.Instance.Create(memoryManager.AddressSpace, _shadowContext, SwapContext);
            HvApi.hv_vcpu_set_reg(vcpu.Handle, HvReg.PC, address).ThrowOnError();

            _syncCounter = 0;
            _strongSyncCounter = 0;

            while (Running)
            {
                if (Interlocked.CompareExchange(ref _shouldStep, 0, 1) == 1)
                {
                    uint currentEl = Pstate & ~((uint)ExceptionLevel.PstateMask);
                    if (currentEl == (uint)ExceptionLevel.EL1h)
                    {
                        HvApi.hv_vcpu_get_sys_reg(vcpu.Handle, HvSysReg.SPSR_EL1, out ulong spsr).ThrowOnError();
                        spsr |= (1U << 21);
                        HvApi.hv_vcpu_set_sys_reg(vcpu.Handle, HvSysReg.SPSR_EL1, spsr);
                    }
                    else
                    {
                        Pstate |= (1U << 21);
                    }
                    HvApi.hv_vcpu_set_sys_reg(vcpu.Handle, HvSysReg.MDSCR_EL1, 1);
                }

                // Adaptive GPU synchronization to prevent 0 FPS
                if (++_syncCounter % 12 == 0)
                {
                    TryGpuSync();
                    _syncCounter = 0;
                }

                HvApi.hv_vcpu_run(vcpu.Handle).ThrowOnError();

                HvExitReason reason = vcpu.ExitInfo->Reason;

                if (reason == HvExitReason.Exception)
                {
                    uint hvEsr = (uint)vcpu.ExitInfo->Exception.Syndrome;
                    ExceptionClass hvEc = (ExceptionClass)(hvEsr >> 26);

                    if (hvEc != ExceptionClass.HvcAarch64)
                        throw new Exception($"Unhandled exception from guest kernel with ESR 0x{hvEsr:X} ({hvEc}).");

                    address = SynchronousException(memoryManager, ref vcpu);
                    HvApi.hv_vcpu_set_reg(vcpu.Handle, HvReg.PC, address).ThrowOnError();
                }
                else if (reason is HvExitReason.Canceled or HvExitReason.VTimerActivated)
                {
                    if (GetAndClearInterruptRequested())
                    {
                        ReturnToPool(vcpu);
                        InterruptHandler();
                        vcpu = RentFromPool(memoryManager.AddressSpace, vcpu);
                    }

                    if (reason == HvExitReason.VTimerActivated)
                    {
                        vcpu.EnableAndUpdateVTimer();

                        // Unmask VTimer interrupts.
                        HvApi.hv_vcpu_set_vtimer_mask(vcpu.Handle, false).ThrowOnError();
                    }
                }
                else
                {
                    throw new Exception($"Unhandled exit reason {reason}.");
                }
            }

            HvVcpuPool.Instance.Destroy(vcpu, SwapContext);
        }

        // TryGpuSync() is called periodically in the main Execute() loop. The "syncing" value can be tuned based on gameplay results.
        // This feature it to be followed-up and further completed in a future PR.
        private void TryGpuSync()
        {
            try
            {
                Thread.Yield();

                if (++_strongSyncCounter % 6 == 0)
                {
                    Thread.Yield();
                }
            }
            catch (Exception ex)
            {
                if (_strongSyncCounter % 100 == 0)
                {
                    Logger.Warning?.Print(LogClass.Gpu, $"[AppleHv] GPU sync issue: {ex.Message}");
                }
            }
        }

        private ulong SynchronousException(HvMemoryManager memoryManager, ref HvVcpu vcpu)
        {
            ulong vcpuHandle = vcpu.Handle;
            HvApi.hv_vcpu_get_sys_reg(vcpuHandle, HvSysReg.ELR_EL1, out ulong elr).ThrowOnError();
            HvApi.hv_vcpu_get_sys_reg(vcpuHandle, HvSysReg.ESR_EL1, out ulong esr).ThrowOnError();

            ExceptionClass ec = (ExceptionClass)((uint)esr >> 26);

            switch (ec)
            {
                case ExceptionClass.DataAbortLowerEl:
                    DataAbort(memoryManager.Tracking, vcpuHandle, (uint)esr);
                    break;

                case ExceptionClass.TrappedMsrMrsSystem:
                    InstructionTrap((uint)esr);
                    HvApi.hv_vcpu_set_sys_reg(vcpuHandle, HvSysReg.ELR_EL1, elr + 4UL).ThrowOnError();
                    break;

                case ExceptionClass.SvcAarch64:
                    ReturnToPool(vcpu);
                    ushort id = (ushort)esr;
                    SupervisorCallHandler(elr - 4UL, id);
                    Thread.Yield(); // MoltenVK causes extremely frequent SVC exits, and HVF handles them in a busy loop. Hypervisor.Framework accelerates the guest CPU, and without periodic yielding/flushing, MoltenVK's presentation queue can starve, causing permanent 0 FPS deadlock.
                    vcpu = RentFromPool(memoryManager.AddressSpace, vcpu);
                    break;

                case ExceptionClass.SoftwareStepLowerEl:
                    HvApi.hv_vcpu_get_sys_reg(vcpuHandle, HvSysReg.SPSR_EL1, out ulong spsr).ThrowOnError();
                    spsr &= ~((ulong)(1 << 21));
                    HvApi.hv_vcpu_set_sys_reg(vcpuHandle, HvSysReg.SPSR_EL1, spsr).ThrowOnError();
                    HvApi.hv_vcpu_set_sys_reg(vcpuHandle, HvSysReg.MDSCR_EL1, 0);
                    ReturnToPool(vcpu);
                    StepHandler();
                    vcpu = RentFromPool(memoryManager.AddressSpace, vcpu);
                    break;

                case ExceptionClass.BrkAarch64:
                    ReturnToPool(vcpu);
                    BreakHandler(elr, (ushort)esr);
                    vcpu = RentFromPool(memoryManager.AddressSpace, vcpu);
                    break;

                default:
                    throw new Exception($"Unhandled guest exception {ec}.");
            }

            // Make sure we will continue running at EL0.
            if (memoryManager.AddressSpace.GetAndClearUserTlbInvalidationPending())

                // TODO: Invalidate only the range that was modified?
                return HvAddressSpace.KernelRegionTlbiEretAddress;

            return HvAddressSpace.KernelRegionEretAddress;
        }

        private static void DataAbort(MemoryTracking tracking, ulong vcpu, uint esr)
        {
            bool write = (esr & (1u << 6)) != 0;
            bool farValid = (esr & (1u << 10)) == 0;
            int accessSizeLog2 = (int)((esr >> 22) & 3);

            if (farValid)
            {
                HvApi.hv_vcpu_get_sys_reg(vcpu, HvSysReg.FAR_EL1, out ulong far).ThrowOnError();
                ulong size = 1UL << accessSizeLog2;

                if (!tracking.VirtualMemoryEvent(far, size, write))
                {
                    string rw = write ? "write" : "read";
                    throw new Exception($"Unhandled invalid memory access at VA 0x{far:X} with size 0x{size:X} ({rw}).");
                }
            }
            else
            {
                throw new Exception($"Unhandled invalid memory access at unknown VA with ESR 0x{esr:X}.");
            }
        }

        private void InstructionTrap(uint esr)
        {
            bool read = (esr & 1) != 0;
            uint rt = (esr >> 5) & 0x1f;

            if (read)
            {
                // Op0 Op2 Op1 CRn 00000 CRm
                switch ((esr >> 1) & 0x1ffe0f)
                {
                    case 0b11_000_011_1110_00000_0000: // CNTFRQ_EL0
                        WriteRt(rt, _counter.Frequency);
                        break;
                    case 0b11_001_011_1110_00000_0000: // CNTPCT_EL0
                        WriteRt(rt, _counter.Counter);
                        break;
                    default:
                        throw new Exception($"Unhandled system register read with ESR 0x{esr:X}");
                }
            }
            else
            {
                throw new Exception($"Unhandled system register write with ESR 0x{esr:X}");
            }
        }

        private void WriteRt(uint rt, ulong value)
        {
            if (rt < 31)
                SetX((int)rt, value);
        }

        private void ReturnToPool(HvVcpu vcpu)
        {
            HvVcpuPool.Instance.Return(vcpu, SwapContext);
        }

        private HvVcpu RentFromPool(HvAddressSpace addressSpace, HvVcpu vcpu)
        {
            return HvVcpuPool.Instance.Rent(addressSpace, _shadowContext, vcpu, SwapContext);
        }

        private void SwapContext(IHvExecutionContext newContext)
        {
            _impl = newContext;
        }

        public void Dispose() { }
    }
}
