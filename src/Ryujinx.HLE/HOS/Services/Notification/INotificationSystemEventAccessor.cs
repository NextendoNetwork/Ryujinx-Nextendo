using Ryujinx.Common.Logging;
using Ryujinx.HLE.HOS.Ipc;
using Ryujinx.HLE.HOS.Kernel.Threading;
using Ryujinx.Horizon.Common;
using System;

namespace Ryujinx.HLE.HOS.Services.Notification
{
    class INotificationSystemEventAccessor : IpcService
    {

        private readonly KEvent _getNotificationSendingNotifierEvent;
        private int _getNotificationSendingNotifierEventHandle;

        public INotificationSystemEventAccessor(ServiceCtx context)
        {
            _getNotificationSendingNotifierEvent = new KEvent(context.Device.System.KernelContext);
        }

        [CommandCmif(0)] // 9.0.0+
        // GetNotificationSendingNotifier() -> nn::notification::server::INotificationSystemEventAccessor
        public ResultCode GetSystemEvent(ServiceCtx context)
        {
            if (_getNotificationSendingNotifierEventHandle == 0)
            {
                if (context.Process.HandleTable.GenerateHandle(_getNotificationSendingNotifierEvent.ReadableEvent, out _getNotificationSendingNotifierEventHandle) != Result.Success)
                {
                    throw new InvalidOperationException("Out of handles!");
                }
            }

            context.Response.HandleDesc = IpcHandleDesc.MakeCopy(_getNotificationSendingNotifierEventHandle);
            return ResultCode.Success;
        }
    }
}
