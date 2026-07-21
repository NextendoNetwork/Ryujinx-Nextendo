using Ryujinx.HLE.HOS.Services.Ldn.UserServiceCreator;

namespace Ryujinx.HLE.HOS.Services.Ldn
{
    [Service("ldn:u")]
    class IUserServiceCreator : IpcService
    {
        public IUserServiceCreator(ServiceCtx context) : base(context.Device.System.LdnServer) { }

        [CommandCmif(0)]
        // CreateUserLocalCommunicationService() -> object<nn::ldn::detail::IUserLocalCommunicationService>
        public ResultCode CreateUserLocalCommunicationService(ServiceCtx context)
        {
            MakeObject(context, new IUserLocalCommunicationService(context));

            return ResultCode.Success;
        }

        [CommandCmif(1)] // 18.0.0+
        // CreateClientProcessMonitor() -> object<nn::ldn::detail::IClientProcessMonitor>
        public ResultCode CreateClientProcessMonitor(ServiceCtx context)
        {
            MakeObject(context, new IClientProcessMonitor(context));

            return ResultCode.Success;
        }
    }
}
