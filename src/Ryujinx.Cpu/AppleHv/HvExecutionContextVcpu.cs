using ARMeilleure.State;
using Ryujinx.Common.Logging;
using Ryujinx.Memory;
using System;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Threading;

namespace Ryujinx.Cpu.AppleHv
{
    [SupportedOSPlatform("macos")]
    class HvExecutionContextVcpu : IHvExecutionContext
    {
        private static readonly MemoryBlock _setSimdFpRegFuncMem;
        private delegate HvResult SetSimdFpReg(ulong vcpu, HvSimdFPReg reg, in V128 value, nint funcPtr);
        private static readonly SetSimdFpReg _setSimdFpReg;
        private static readonly nint _setSimdFpRegNativePtr;

        public static bool AggressiveMode { get; set; } = false;
        private bool _earlyBootPhase = true;

        public ulong ThreadUid { get; set; }

        private readonly ulong[] _x = new ulong[32];
        private readonly V128[] _v = new V128[32];

        private ulong _pc;
        private ulong _elrEl1;
        private ulong _esrEl1;
        private ulong _tpidrEl0;
        private ulong _tpidrroEl0;
        private ulong _fpcr;
        private ulong _fpsr;
        private ulong _pstateRaw;

        private long _fallbackCount;
        private long _lastWarningTicks;
        private const long WarningCooldownTicks = 500_000_000; // 0.5 seconds

        private readonly ulong _vcpu;
        private int _interruptRequested;
        private readonly object _registerLock = new object();

        static HvExecutionContextVcpu()
        {
            // .NET does not support passing vectors by value, so we need to pass a pointer and use a native
            // function to load the value into a vector register.
            _setSimdFpRegFuncMem = new MemoryBlock(MemoryBlock.GetPageSize());
            _setSimdFpRegFuncMem.Write(0, 0x3DC00040u); // LDR Q0, [X2]
            _setSimdFpRegFuncMem.Write(4, 0xD61F0060u); // BR X3
            _setSimdFpRegFuncMem.Reprotect(0, _setSimdFpRegFuncMem.Size, MemoryPermission.ReadAndExecute);

            _setSimdFpReg = Marshal.GetDelegateForFunctionPointer<SetSimdFpReg>(_setSimdFpRegFuncMem.Pointer);

            if (NativeLibrary.TryLoad(HvApi.LibraryName, out nint hvLibHandle))
            {
                _setSimdFpRegNativePtr = NativeLibrary.GetExport(hvLibHandle, nameof(HvApi.hv_vcpu_set_simd_fp_reg));
            }
        }

        public HvExecutionContextVcpu(ulong vcpu)
        {
            _vcpu = vcpu;
            Reset();
        }

        public void Reset()
        {
            lock (_registerLock)
            {
                _pstateRaw = 0x80000000UL;
                _pc = 0;
                _elrEl1 = 0;
                _esrEl1 = 0;
                _tpidrEl0 = 0;
                _tpidrroEl0 = 0;
                _fpcr = 0;
                _fpsr = 0;

                Array.Clear(_x, 0, _x.Length);
                Array.Clear(_v, 0, _v.Length);

                _fallbackCount = 0;
                _lastWarningTicks = 0;
                _interruptRequested = 0;
                _earlyBootPhase = true;
            }
        }

        private void LogHvWarning(string operation, string regName, string extra = "")
        {
            if (AggressiveMode) return;

            long now = DateTime.UtcNow.Ticks;
            if (now - _lastWarningTicks <= WarningCooldownTicks) return;

            string msg = $"[AppleHv] BadArgument on {operation} {regName} | PC=0x{_pc:X16}";
            if (!string.IsNullOrEmpty(extra)) msg += $" | {extra}";
            msg += $" | Total: {Interlocked.Read(ref _fallbackCount)}";

            Logger.Warning?.Print(LogClass.Cpu, msg);
            _lastWarningTicks = now;
        }

        public ulong Pc
        {
            get { lock (_registerLock) return GetRegCached(HvReg.PC, ref _pc, "PC"); }
            set { lock (_registerLock) SetRegCached(HvReg.PC, value, ref _pc, "PC"); }
        }

        public ulong ElrEl1
        {
            get { lock (_registerLock) return GetSysRegCached(HvSysReg.ELR_EL1, ref _elrEl1, "ELR_EL1"); }
            set { lock (_registerLock) SetSysRegCached(HvSysReg.ELR_EL1, value, ref _elrEl1, "ELR_EL1"); }
        }

        public ulong EsrEl1
        {
            get { lock (_registerLock) return GetSysRegCached(HvSysReg.ESR_EL1, ref _esrEl1, "ESR_EL1"); }
            set { lock (_registerLock) SetSysRegCached(HvSysReg.ESR_EL1, value, ref _esrEl1, "ESR_EL1"); }
        }

        public long TpidrEl0
        {
            get { lock (_registerLock) return (long)GetSysRegCached(HvSysReg.TPIDR_EL0, ref _tpidrEl0, "TPIDR_EL0"); }
            set { lock (_registerLock) SetSysRegCached(HvSysReg.TPIDR_EL0, (ulong)value, ref _tpidrEl0, "TPIDR_EL0"); }
        }

        public long TpidrroEl0
        {
            get { lock (_registerLock) return (long)GetSysRegCached(HvSysReg.TPIDRRO_EL0, ref _tpidrroEl0, "TPIDRRO_EL0"); }
            set { lock (_registerLock) SetSysRegCached(HvSysReg.TPIDRRO_EL0, (ulong)value, ref _tpidrroEl0, "TPIDRRO_EL0"); }
        }

        public uint Pstate
        {
            get
            {
                lock (_registerLock)
                {
                    HvResult res = HvApi.hv_vcpu_get_reg(_vcpu, HvReg.CPSR, out ulong val);
                    if (res == HvResult.BadArgument)
                    {
                        Interlocked.Increment(ref _fallbackCount);
                        LogHvWarning("Get", "CPSR (Pstate)");
                        return (uint)_pstateRaw;
                    }
                    res.ThrowOnError();
                    _pstateRaw = val;
                    return (uint)val;
                }
            }
            set
            {
                lock (_registerLock)
                {
                    HvResult res = HvApi.hv_vcpu_set_reg(_vcpu, HvReg.CPSR, value);
                    if (res == HvResult.BadArgument)
                    {
                        Interlocked.Increment(ref _fallbackCount);
                        LogHvWarning("Set", "CPSR (Pstate)", $"value=0x{value:X}");
                    }
                    else res.ThrowOnError();
                    _pstateRaw = value;
                }
            }
        }

        public uint Fpcr
        {
            get { lock (_registerLock) return (uint)GetRegCached(HvReg.FPCR, ref _fpcr, "FPCR"); }
            set { lock (_registerLock) SetRegCached(HvReg.FPCR, value, ref _fpcr, "FPCR"); }
        }

        public uint Fpsr
        {
            get { lock (_registerLock) return (uint)GetRegCached(HvReg.FPSR, ref _fpsr, "FPSR"); }
            set { lock (_registerLock) SetRegCached(HvReg.FPSR, value, ref _fpsr, "FPSR"); }
        }

        public ulong GetX(int index)
        {
            lock (_registerLock)
            {
                ulong value;
                string regName = index == 31 ? "SP_EL0" : $"X{index}";

                if (index == 31)
                {
                    HvResult res = HvApi.hv_vcpu_get_sys_reg(_vcpu, HvSysReg.SP_EL0, out value);
                    if (res == HvResult.BadArgument)
                    {
                        Interlocked.Increment(ref _fallbackCount);
                        LogHvWarning("GetX", regName);
                        return _x[31];
                    }
                    res.ThrowOnError();
                    return _x[31] = value;
                }

                if ((uint)index > 30) return 0;

                if (index == 0 && _earlyBootPhase && _pc == 0)
                {
                    return _x[0];
                }

                HvResult resX = HvApi.hv_vcpu_get_reg(_vcpu, HvReg.X0 + (uint)index, out value);
                if (resX == HvResult.BadArgument)
                {
                    Interlocked.Increment(ref _fallbackCount);
                    LogHvWarning("GetX", regName);
                    return _x[index];
                }
                resX.ThrowOnError();
                return _x[index] = value;
            }
        }

        public void SetX(int index, ulong value)
        {
            lock (_registerLock)
            {
                string regName = index == 31 ? "SP_EL0" : $"X{index}";

                if (index == 31)
                {
                    HvResult res = HvApi.hv_vcpu_set_sys_reg(_vcpu, HvSysReg.SP_EL0, value);
                    if (res == HvResult.BadArgument)
                    {
                        Interlocked.Increment(ref _fallbackCount);
                        LogHvWarning("SetX", regName, $"value=0x{value:X16}");
                        _x[31] = value;
                        return;
                    }
                    res.ThrowOnError();
                    _x[31] = value;
                }
                else if ((uint)index <= 30)
                {
                    HvResult res = HvApi.hv_vcpu_set_reg(_vcpu, HvReg.X0 + (uint)index, value);
                    if (res == HvResult.BadArgument)
                    {
                        Interlocked.Increment(ref _fallbackCount);
                        LogHvWarning("SetX", regName, $"value=0x{value:X16}");
                        _x[index] = value;
                        return;
                    }
                    res.ThrowOnError();
                    _x[index] = value;
                }
            }
        }

        public V128 GetV(int index)
        {
            lock (_registerLock)
            {
                if ((uint)index > 31) return default;

                HvResult res = HvApi.hv_vcpu_get_simd_fp_reg(_vcpu, HvSimdFPReg.Q0 + (uint)index, out HvSimdFPUchar16 val);
                if (res == HvResult.BadArgument)
                {
                    Interlocked.Increment(ref _fallbackCount);
                    LogHvWarning("GetV", $"Q{index}");
                    return _v[index];
                }
                res.ThrowOnError();
                return _v[index] = new V128(val.Low, val.High);
            }
        }

        public void SetV(int index, V128 value)
        {
            lock (_registerLock)
            {
                if ((uint)index > 31) return;

                HvResult res = _setSimdFpReg(_vcpu, HvSimdFPReg.Q0 + (uint)index, value, _setSimdFpRegNativePtr);
                if (res == HvResult.BadArgument)
                {
                    Interlocked.Increment(ref _fallbackCount);
                    LogHvWarning("SetV", $"Q{index}");
                    _v[index] = value;
                    return;
                }
                res.ThrowOnError();
                _v[index] = value;
            }
        }

        private ulong GetRegCached(HvReg reg, ref ulong cached, string name)
        {
            HvResult res = HvApi.hv_vcpu_get_reg(_vcpu, reg, out ulong val);
            if (res == HvResult.BadArgument)
            {
                Interlocked.Increment(ref _fallbackCount);
                LogHvWarning("GetReg", name);
                return cached;
            }
            res.ThrowOnError();
            return cached = val;
        }

        private void SetRegCached(HvReg reg, ulong value, ref ulong cached, string name)
        {
            HvResult res = HvApi.hv_vcpu_set_reg(_vcpu, reg, value);
            if (res == HvResult.BadArgument)
            {
                Interlocked.Increment(ref _fallbackCount);
                LogHvWarning("SetReg", name, $"value=0x{value:X16}");
                cached = value;
                return;
            }
            res.ThrowOnError();
            cached = value;
        }

        private ulong GetSysRegCached(HvSysReg reg, ref ulong cached, string name)
        {
            HvResult res = HvApi.hv_vcpu_get_sys_reg(_vcpu, reg, out ulong val);
            if (res == HvResult.BadArgument)
            {
                Interlocked.Increment(ref _fallbackCount);
                LogHvWarning("GetSysReg", name);
                return cached;
            }
            res.ThrowOnError();
            return cached = val;
        }

        private void SetSysRegCached(HvSysReg reg, ulong value, ref ulong cached, string name)
        {
            HvResult res = HvApi.hv_vcpu_set_sys_reg(_vcpu, reg, value);
            if (res == HvResult.BadArgument)
            {
                Interlocked.Increment(ref _fallbackCount);
                LogHvWarning("SetSysReg", name, $"value=0x{value:X16}");
                cached = value;
                return;
            }
            res.ThrowOnError();
            cached = value;
        }

        public long GetFallbackCount() => Interlocked.Read(ref _fallbackCount);

        public void RequestInterrupt()
        {
            if (Interlocked.Exchange(ref _interruptRequested, 1) == 0)
            {
                ulong vcpu = _vcpu;
                HvApi.hv_vcpus_exit(ref vcpu, 1);
            }
        }

        public bool GetAndClearInterruptRequested()
        {
            return Interlocked.Exchange(ref _interruptRequested, 0) != 0;
        }
    }
}
