using LibHac.Ncm;

namespace Ryujinx.HLE.HOS.Services.Arp
{
    class ApplicationLaunchProperty
    {
        public ulong TitleId;
        public int Version;
        public byte BaseGameStorageId;
        public byte UpdateGameStorageId;
#pragma warning disable CS0649 // Field is never assigned to
        public short Padding;
#pragma warning restore CS0649

        public static ApplicationLaunchProperty Default
        {
            get
            {
                return new ApplicationLaunchProperty
                {
                    TitleId = 0x00,
                    Version = 0x00,
                    BaseGameStorageId = (byte)StorageId.BuiltInSystem,
                    UpdateGameStorageId = (byte)StorageId.None,
                };
            }
        }

        public static ApplicationLaunchProperty GetByPid(ServiceCtx context)
        {
            return GetByPid(context, context.ClientProcessId);
        }

        public static ApplicationLaunchProperty GetByPid(ServiceCtx context, ulong pid)
        {
            // TODO: Handle ApplicationLaunchProperty as array when pid will be supported and return the right item.
            //       For now we can hardcode values, and fix it after GetApplicationLaunchProperty is implemented.

            return new ApplicationLaunchProperty
            {
                TitleId = context.Device.Processes.GetProcess(pid).ProgramId,
                Version = 0x00,
                BaseGameStorageId = (byte)StorageId.BuiltInSystem,
                UpdateGameStorageId = (byte)StorageId.None,
            };
        }
    }
}
