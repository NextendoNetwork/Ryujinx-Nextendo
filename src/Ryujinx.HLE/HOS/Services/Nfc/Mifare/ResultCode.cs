namespace Ryujinx.HLE.HOS.Services.Nfc.Mifare
{
    public enum ResultCode
    {
        ModuleId = 161,
        ErrorCodeShift = 9,

        Success = 0,

        DeviceNotFound = (64 << ErrorCodeShift) | ModuleId, // 0x80A1
        WrongArgument = (65 << ErrorCodeShift) | ModuleId, // 0x82A1
        WrongDeviceState = (73 << ErrorCodeShift) | ModuleId, // 0x92A1
        NfcDisabled = (80 << ErrorCodeShift) | ModuleId, // 0xA0A1
        TagNotFound = (97 << ErrorCodeShift) | ModuleId, // 0xC2A1
        MifareAccessError = (288 << ErrorCodeShift) | ModuleId, // 0x240a1
    }
}
