namespace Ryujinx.HLE.HOS.Services.Nfc.Mifare.MifareManager
{
    enum NfcMifareCommand : byte
    {
        NfcMifareCommand_Read = 0x30,
        NfcMifareCommand_AuthA = 0x60,
        NfcMifareCommand_AuthB = 0x61,
        NfcMifareCommand_Write = 0xA0,
        NfcMifareCommand_Transfer = 0xB0,
        NfcMifareCommand_Decrement = 0xC0,
        NfcMifareCommand_Increment = 0xC1,
        NfcMifareCommand_Store = 0xC2,
    }
}