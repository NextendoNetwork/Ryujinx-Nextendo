namespace Ryujinx.HLE.HOS.Services.Nfc.Mifare.MifareManager
{
    enum NfcProtocol : byte
    {
        NfcProtocol_None = 0b_0000_0000,
        NfcProtocol_TypeA = 0b_0000_0001, ///< ISO14443A
        NfcProtocol_TypeB = 0b_0000_0010, ///< ISO14443B
        NfcProtocol_TypeF = 0b_0000_0100, ///< Sony FeliCa
        NfcProtocol_All = 0xFF,
    }
}