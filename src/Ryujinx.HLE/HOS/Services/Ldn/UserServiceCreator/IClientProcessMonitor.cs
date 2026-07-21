using Ryujinx.Common.Logging;

namespace Ryujinx.HLE.HOS.Services.Ldn.UserServiceCreator
{
    class IClientProcessMonitor : DisposableIpcService
    {
        public IClientProcessMonitor(ServiceCtx context) { }

        [CommandCmif(0)] // 18.0.0+
        // RegisterClient(u64 pid_placeholder, pid)
        public ResultCode RegisterClient(ServiceCtx context)
        {
            Logger.Stub?.PrintStub(LogClass.ServiceLdn);

            return ResultCode.Success;
        }

        protected override void Dispose(bool isDisposing) { }
    }
}
