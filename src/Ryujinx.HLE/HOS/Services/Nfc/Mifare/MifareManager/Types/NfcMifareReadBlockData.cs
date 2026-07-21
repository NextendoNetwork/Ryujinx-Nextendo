using Ryujinx.Common.Memory;
using System.Runtime.InteropServices;

namespace Ryujinx.HLE.HOS.Services.Nfc.Mifare.MifareManager
{
    [StructLayout(LayoutKind.Sequential, Size = 0x18)]
    struct NfcMifareReadBlockData
    {
        public Array16<byte> Data;
        public byte SectorNumber;
        public Array7<byte> Reserved;
    }
}
