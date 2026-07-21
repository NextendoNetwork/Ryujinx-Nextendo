using Ryujinx.HLE.HOS.Kernel.Threading;
using Ryujinx.HLE.HOS.Services.Hid;

namespace Ryujinx.HLE.HOS.Services.Nfc.Mifare.MifareManager
{
    class NfcDevice
    {
        public KEvent ActivateEvent;
        public KEvent DeactivateEvent;

        public void SignalActivate() => ActivateEvent.ReadableEvent.Signal();
        public void SignalDeactivate() => DeactivateEvent.ReadableEvent.Signal();

        public NfcDeviceState State = NfcDeviceState.Unavailable;

        public PlayerIndex Handle;
        public NpadIdType NpadIdType;

        public byte[] Data;
    }
}
