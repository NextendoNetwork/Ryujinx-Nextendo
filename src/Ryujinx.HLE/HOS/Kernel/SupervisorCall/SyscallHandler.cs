using Ryujinx.Common.Logging;
using Ryujinx.Cpu;
using Ryujinx.HLE.HOS.Kernel.Threading;

namespace Ryujinx.HLE.HOS.Kernel.SupervisorCall
{
    partial class SyscallHandler
    {
        private readonly KernelContext _context;

        public SyscallHandler(KernelContext context)
        {
            _context = context;
        }

        public void SvcCall(IExecutionContext context, ulong address, int id)
        {
            KThread currentThread = KernelStatic.GetCurrentThread();

            if (currentThread.Owner != null &&
                currentThread.GetUserDisableCount() != 0 &&
                currentThread.Owner.PinnedThreads[currentThread.CurrentCore] == null)
            {
                _context.CriticalSection.Enter();

                currentThread.Owner.PinThread(currentThread);

                currentThread.SetUserInterruptFlag();

                _context.CriticalSection.Leave();
            }

            // [Nextendo] Some titles issue svcCallSecureMonitor (svc 0x7F) — a secure-monitor
            // call the emulator does not implement, which would otherwise fault with a
            // NotImplementedException. Return a benign result instead of crashing so the
            // title can continue.
            if (id == 0x7F)
            {
                Logger.Warning?.Print(LogClass.KernelSvc,
                    $"[Nextendo] svcCallSecureMonitor stub: x0={context.GetX(0):x16} x1={context.GetX(1):x16} " +
                    $"x2={context.GetX(2):x16} x3={context.GetX(3):x16} x4={context.GetX(4):x16}");
                // Benign SMC result: x0=0 (SMCCC_SUCCESS), everything else unchanged.
                context.SetX(0, 0);
                currentThread.HandlePostSyscall();
                return;
            }

            if (context.IsAarch32)
            {
                SyscallDispatch.Dispatch32(_context.Syscall, context, id);
            }
            else
            {
                SyscallDispatch.Dispatch64(_context.Syscall, context, id);
            }

            currentThread.HandlePostSyscall();
        }
    }
}
