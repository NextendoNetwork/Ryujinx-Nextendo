using Ryujinx.HLE.HOS.Kernel.Process;
using Ryujinx.HLE.HOS.Kernel.Threading;

namespace Ryujinx.HLE.HOS.Kernel.Ipc
{
    class KSessionRequest
    {
        public KBufferDescriptorTable BufferDescriptorTable { get; private set; }

        public KThread ClientThread { get; private set; }

        public KProcess ServerProcess { get; set; }

        public KWritableEvent AsyncEvent { get; private set; }

        public ulong CustomCmdBuffAddr { get; private set; }
        public ulong CustomCmdBuffSize { get; private set; }

        public KSessionRequest Set(
            KThread clientThread,
            ulong customCmdBuffAddr,
            ulong customCmdBuffSize,
            KWritableEvent asyncEvent = null)
        {
            ClientThread = clientThread;
            CustomCmdBuffAddr = customCmdBuffAddr;
            CustomCmdBuffSize = customCmdBuffSize;
            AsyncEvent = asyncEvent;

            BufferDescriptorTable = BufferDescriptorTable?.Clear() ?? new KBufferDescriptorTable();

            return this;
        }
    }
}
