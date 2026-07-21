namespace Ryujinx.HLE.HOS.Services.Ectx
{
    [Service("ectx:aw")] // 11.0.0+
    class IWriterForApplication : IpcService
    {
        public IWriterForApplication(ServiceCtx context) { }

        [CommandCmif(0)]
        // CreateContextRegistrar() -> object<nn::err::context::IContextRegistrar>
        public ResultCode CreateContextRegistrar(ServiceCtx context)
        {
            MakeObject(context, new IContextRegistrar(context));

            return ResultCode.Success;
        }
    }
}
