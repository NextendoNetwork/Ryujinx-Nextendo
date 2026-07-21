using Ryujinx.Common.Memory;
using System.Runtime.InteropServices;

namespace Ryujinx.HLE.HOS.Services.Nfc.Mifare.MifareManager
{
    [StructLayout(LayoutKind.Sequential, Size = 0x18)]
    struct NfcMifareReadBlockParameter
    {
        public byte SectorNumber;
        public Array7<byte> Reserved;
        public NfcSectorKey SectorKey;
    }
}
