using Ryujinx.Common.Memory;
using System.Runtime.InteropServices;

namespace Ryujinx.HLE.HOS.Services.Nfc.Mifare.MifareManager
{
    [StructLayout(LayoutKind.Sequential, Size = 0x28)]
    struct NfcMifareWriteBlockParameter
    {
        public Array16<byte> Data;
        public byte SectorNumber;
        public Array7<byte> Reserved;
        public NfcSectorKey SectorKey;
    }
}
