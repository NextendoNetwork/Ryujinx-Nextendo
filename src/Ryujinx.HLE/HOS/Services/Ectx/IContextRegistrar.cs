using System;
using Ryujinx.Common.Logging;
using Ryujinx.Horizon.Common;

namespace Ryujinx.HLE.HOS.Services.Ectx
{
    class IContextRegistrar : DisposableIpcService
    {
        public IContextRegistrar(ServiceCtx context) { }

        [CommandCmif(0)] // 11.0.0+
        // Complete(nn::Result result, buffer<bytes, 5> raw_context) -> (i32 context_descriptor)
        public ResultCode Complete(ServiceCtx context)
        {
            Result result = new(context.RequestData.ReadInt32());
            ulong rawContextPosition = context.Request.SendBuff[0].Position;
            ulong rawContextSize = context.Request.SendBuff[0].Size;

            byte[] rawContext = new byte[rawContextSize];

            context.Memory.Read(rawContextPosition, rawContext);

            context.ResponseData.Write(0); // TODO: return context_descriptor

            Logger.Stub?.PrintStub(LogClass.ServiceEctx, $"Result: {result}, rawContext: {Convert.ToHexString(rawContext)}" );

            return ResultCode.Success;
        }

        protected override void Dispose(bool isDisposing) { }
    }
}
