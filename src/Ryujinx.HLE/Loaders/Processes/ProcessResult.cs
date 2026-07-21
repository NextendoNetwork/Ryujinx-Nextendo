using LibHac.Common;
using LibHac.Loader;
using LibHac.Ns;
using Ryujinx.Common.Logging;
using Ryujinx.Cpu;
using Ryujinx.HLE.HOS.SystemState;
using Ryujinx.Horizon.Common;

namespace Ryujinx.HLE.Loaders.Processes
{
    public class ProcessResult
    {
        public static ProcessResult Failed => new(null, new BlitStruct<ApplicationControlProperty>(1), false, false, null, 0, 0, 0, 0, TitleLanguage.AmericanEnglish);

        private readonly byte _mainThreadPriority;
        private readonly uint _mainThreadStackSize;

        public readonly IDiskCacheLoadState DiskCacheLoadState;

        public readonly MetaLoader MetaLoader;
        public readonly ApplicationControlProperty ApplicationControlProperties;

        public readonly ulong ProcessId;
        public readonly string Name;
        public readonly string DisplayVersion;
        public readonly ulong ProgramId;
        public readonly string ProgramIdText;
        public readonly bool Is64Bit;
        public readonly bool DiskCacheEnabled;
        public readonly bool AllowCodeMemoryForJit;
        public readonly ProcessIdentity Identity;

        public ProcessResult(
            MetaLoader metaLoader,
            BlitStruct<ApplicationControlProperty> applicationControlProperties,
            bool diskCacheEnabled,
            bool allowCodeMemoryForJit,
            IDiskCacheLoadState diskCacheLoadState,
            ulong pid,
            byte programIndex,
            byte mainThreadPriority,
            uint mainThreadStackSize,
            TitleLanguage titleLanguage)
        {
            _mainThreadPriority = mainThreadPriority;
            _mainThreadStackSize = mainThreadStackSize;

            DiskCacheLoadState = diskCacheLoadState;
            ProcessId = pid;

            MetaLoader = metaLoader;
            ApplicationControlProperties = applicationControlProperties.Value;

            if (metaLoader is not null)
            {
                Logger.Info?.Print(LogClass.Application,$"metaLoader: {metaLoader}");
                ulong programId = metaLoader.ProgramId;

                Name = ApplicationControlProperties.Title[(int)titleLanguage].NameString.ToString();

                if (string.IsNullOrWhiteSpace(Name))
                {
                    foreach (ApplicationControlProperty.ApplicationTitle appTitle in ApplicationControlProperties.Title)
                    {
                        if (appTitle.Name[0] != 0)
                            continue;

                        Name = appTitle.NameString.ToString();
                    }
                }

                DisplayVersion = ApplicationControlProperties.DisplayVersionString.ToString();
                ProgramId = programId;
                ProgramIdText = $"{programId:x16}";
                Is64Bit = metaLoader.IsProgram64Bit;
                Identity = new ProcessIdentity(pid, programId, programIndex, DisplayVersion, GetProcessKind(programId));
            } 
            
            else
            {
                Logger.Error?.Print(LogClass.Application,$"metaLoader is null !!!");
                ProcessId = 0;
                return;
            }
            
            DiskCacheEnabled = diskCacheEnabled;
            AllowCodeMemoryForJit = allowCodeMemoryForJit;
        }

        private static ProcessKind GetProcessKind(ulong programId)
        {
            if (programId == 0)
            {
                return ProcessKind.Application;
            }

            if (programId is >= 0x0100000000001000 and <= 0x0100000000001FFF)
            {
                return ProcessKind.SystemApplet;
            }

            if (programId is >= 0x0100000000000800 and <= 0x0100000000000FFF)
            {
                return ProcessKind.LibraryApplet;
            }

            if (programId <= 0x0100000000007FFF)
            {
                return ProcessKind.SystemApplication;
            }

            return ProcessKind.Application;
        }

        public bool Start(Switch device)
        {
            device.Configuration.ContentManager.LoadEntries(device);

            Result result = device.System.KernelContext.Processes[ProcessId].Start(_mainThreadPriority, _mainThreadStackSize);
            if (result != Result.Success)
            {
                Logger.Error?.Print(LogClass.Loader, $"Process start returned error \"{result}\".");

                return false;
            }

            bool isFirmware = ProgramId is >= 0x0100000000000819 and <= 0x010000000000081C;
            bool isFirmwareApplication = ProgramId <= 0x0100000000007FFF;

            string name = !isFirmware
                ? (isFirmwareApplication ? "Firmware Application " : string.Empty) + (!string.IsNullOrWhiteSpace(Name) ? Name : "<Unknown Name>")
                : "Firmware";

            // TODO: LibHac npdm currently doesn't support version field.
            string version = !isFirmware
                ? (!string.IsNullOrWhiteSpace(DisplayVersion) ? DisplayVersion : "<Unknown Version>")
                : device.System.ContentManager.GetCurrentFirmwareVersion()?.VersionString ?? "?";

            Logger.Info?.Print(LogClass.Loader, $"Application Loaded: {name} v{version} [{ProgramIdText}] [{(Is64Bit ? "64-bit" : "32-bit")}]");
            Logger.Info?.Print(LogClass.Loader, $"Process identity: {Identity}");

            return true;
        }
    }
}
