namespace Ryujinx.HLE.HOS.Services.Nfc.Mifare.MifareManager
{
    enum NfcTagType : byte
    {
        NfcTagType_None = 0b_0000_0000,
        NfcTagType_Type1 = 0b_0000_0001, ///< ISO14443A RW. Topaz
        NfcTagType_Type2 = 0b_0000_0010, ///< ISO14443A RW. Ultralight, NTAGX, ST25TN
        NfcTagType_Type3 = 0b_0000_0100, ///< ISO14443A RW/RO. Sony FeliCa
        NfcTagType_Type4A = 0b_0000_1000, ///< ISO14443A RW/RO. DESFire
        NfcTagType_Type4B = 0b_0001_0000, ///< ISO14443B RW/RO. DESFire
        NfcTagType_Type5 = 0b_0010_0000, ///< ISO15693 RW/RO. SLI, SLIX, ST25TV
        NfcTagType_Mifare = 0b_0100_0000, ///< Mifare clasic. Skylanders
        NfcTagType_All = 0xFF,
    }
}