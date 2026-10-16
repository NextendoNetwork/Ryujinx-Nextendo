using Ryujinx.Common.Logging;
using Ryujinx.HLE.HOS.Kernel.Threading;
using System;

namespace Ryujinx.HLE.HOS.Services.Nim.ShopServiceAccessServerInterface.ShopServiceAccessServer.ShopServiceAccessor
{
    class IShopServiceAsync : IpcService
    {
        // The shop is not emulated: every request completes at once with an empty JSON object,
        // which titles that query the shop at boot (e.g. Civilization VII) accept.
        private static readonly byte[] _emptyResponse = "{}"u8.ToArray();

        private byte[] _response = [];

        public KEvent CompletionEvent { get; }

        public IShopServiceAsync(Horizon system)
        {
            CompletionEvent = new KEvent(system.KernelContext);
        }

        [CommandCmif(0)]
        // Cancel()
        public ResultCode Cancel(ServiceCtx context)
        {
            return ResultCode.Success;
        }

        [CommandCmif(1)]
        // GetSize() -> u64
        public ResultCode GetSize(ServiceCtx context)
        {
            context.ResponseData.Write((ulong)_response.Length);

            return ResultCode.Success;
        }

        [CommandCmif(2)]
        // Read(u64 offset) -> (u64 size, buffer<bytes, 0x22>)
        public ResultCode Read(ServiceCtx context)
        {
            ulong offset = context.RequestData.ReadUInt64();

            (ulong position, ulong size) = context.Request.GetBufferType0x22();

            ulong readSize = 0;

            if (offset < (ulong)_response.Length)
            {
                readSize = Math.Min(size, (ulong)_response.Length - offset);

                context.Memory.Write(position, _response.AsSpan((int)offset, (int)readSize));
            }

            context.ResponseData.Write(readSize);

            return ResultCode.Success;
        }

        [CommandCmif(3)]
        // GetErrorCode() -> u32
        public ResultCode GetErrorCode(ServiceCtx context)
        {
            context.ResponseData.Write(0u);

            return ResultCode.Success;
        }

        [CommandCmif(4)]
        // Request()
        public ResultCode Request(ServiceCtx context)
        {
            _response = _emptyResponse;

            CompletionEvent.ReadableEvent.Signal();

            Logger.Stub?.PrintStub(LogClass.ServiceNim);

            return ResultCode.Success;
        }

        [CommandCmif(5)]
        // Prepare(buffer<bytes, 5> path, buffer<bytes, 5> post_data)
        public ResultCode Prepare(ServiceCtx context)
        {
            Logger.Stub?.PrintStub(LogClass.ServiceNim);

            return ResultCode.Success;
        }
    }
}
