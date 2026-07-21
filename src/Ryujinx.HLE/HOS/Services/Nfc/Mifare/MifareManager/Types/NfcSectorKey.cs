using Ryujinx.Common.Memory;
using System.Runtime.InteropServices;

namespace Ryujinx.HLE.HOS.Services.Nfc.Mifare.MifareManager
{
    [StructLayout(LayoutKind.Sequential, Size = 0x10)]
    struct NfcSectorKey
    {
        public NfcMifareCommand MifareCommand;
        public byte Unknown;
        public Array6<byte> Reserved1;
        public Array6<byte> SectorKey;
        public Array2<byte> Reserved2;

    }
}