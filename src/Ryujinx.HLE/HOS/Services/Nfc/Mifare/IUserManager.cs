using Ryujinx.HLE.HOS.Services.Nfc.Mifare.MifareManager;

namespace Ryujinx.HLE.HOS.Services.Nfc.Mifare
{
    [Service("nfc:mf:u")]
    class IUserManager : IpcService
    {
        public IUserManager(ServiceCtx context) { }

        [CommandCmif(0)]
        // CreateUserInterface() -> object<nn::nfc::mf::IUser>
        public ResultCode CreateUserInterface(ServiceCtx context)
        {
            MakeObject(context, new IMifare());

            return ResultCode.Success;
        }
    }
}
