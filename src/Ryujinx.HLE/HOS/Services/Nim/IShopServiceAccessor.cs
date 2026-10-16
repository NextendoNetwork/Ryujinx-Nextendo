using Ryujinx.Common.Logging;
using Ryujinx.HLE.HOS.Ipc;
using Ryujinx.HLE.HOS.Services.Nim.ShopServiceAccessServerInterface.ShopServiceAccessServer.ShopServiceAccessor;
using Ryujinx.Horizon.Common;
using System;

namespace Ryujinx.HLE.HOS.Services.Nim.ShopServiceAccessServerInterface.ShopServiceAccessServer
{
    class IShopServiceAccessor : IpcService
    {
        private readonly Horizon _system;

        public IShopServiceAccessor(Horizon system)
        {
            _system = system;
        }

        [CommandCmif(0)]
        // CreateAsyncInterface(u64) -> (handle<copy>, object<nn::ec::IShopServiceAsync>)
        public ResultCode CreateAsyncInterface(ServiceCtx context)
        {
            IShopServiceAsync asyncInterface = new(_system);

            if (context.Process.HandleTable.GenerateHandle(asyncInterface.CompletionEvent.ReadableEvent, out int eventHandle) != Result.Success)
            {
                throw new InvalidOperationException("Out of handles!");
            }

            // The event must be set first: MakeObject keeps the copy handles already in the response,
            // whereas setting them afterwards dropped the object (null session, crash in the game).
            context.Response.HandleDesc = IpcHandleDesc.MakeCopy(eventHandle);

            MakeObject(context, asyncInterface);

            Logger.Stub?.PrintStub(LogClass.ServiceNim);

            return ResultCode.Success;
        }
    }
}
