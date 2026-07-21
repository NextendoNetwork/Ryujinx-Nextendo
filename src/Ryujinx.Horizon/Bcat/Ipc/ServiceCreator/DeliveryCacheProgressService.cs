using Ryujinx.Common.Logging;
using Ryujinx.Horizon.Bcat.Ipc.Types;
using Ryujinx.Horizon.Common;
using Ryujinx.Horizon.Sdk.Bcat;
using Ryujinx.Horizon.Sdk.OsTypes;
using Ryujinx.Horizon.Sdk.Sf;
using Ryujinx.Horizon.Sdk.Sf.Hipc;
using System;
using System.Threading;

namespace Ryujinx.Horizon.Bcat.Ipc
{
    partial class DeliveryCacheProgressService : IDeliveryCacheProgressService, IDisposable
    {
        private int _handle;
        private SystemEventType _systemEvent;
        private int _disposalState;

        [CmifCommand(0)]
        public Result GetEvent([CopyHandle] out int handle)
        {
            if (_handle == 0)
            {
                Os.CreateSystemEvent(out _systemEvent, EventClearMode.ManualClear, true).AbortOnFailure();

                _handle = Os.GetReadableHandleOfSystemEvent(ref _systemEvent);
            }

            // Signal the event so the game's BCAT delivery-cache sync wait returns
            // immediately (the sync is a no-op here; GetImpl already reports Done). Without
            // this, games like SSBU block waiting for the news/data sync and show
            // "Connect to the Internet to receive the latest news" forever.
            Os.SignalSystemEvent(ref _systemEvent);

            handle = _handle;

            Logger.Stub?.PrintStub(LogClass.ServiceBcat);

            return Result.Success;
        }

        [CmifCommand(1)]
        public Result GetImpl([Buffer(HipcBufferFlags.Out | HipcBufferFlags.Pointer, 0x200)] out DeliveryCacheProgressImpl deliveryCacheProgressImpl)
        {
            deliveryCacheProgressImpl = new DeliveryCacheProgressImpl
            {
                State = DeliveryCacheProgressImpl.Status.Done,
                Result = 0,
            };

            Logger.Stub?.PrintStub(LogClass.ServiceBcat);

            return Result.Success;
        }

        public void Dispose()
        {
            if (_handle != 0 && Interlocked.Exchange(ref _disposalState, 1) == 0)
            {
                Os.DestroySystemEvent(ref _systemEvent);
            }
        }
    }
}
