using Gommon;
using LibHac.Common;
using LibHac.Fs;
using LibHac.Fs.Fsa;
using LibHac.FsSystem;
using LibHac.Loader;
using LibHac.Ns;
using LibHac.Tools.Fs;
using LibHac.Tools.FsSystem;
using LibHac.Tools.FsSystem.NcaUtils;
using Ryujinx.Ava.Common.Locale;
using Ryujinx.Ava.Systems.PlayReport;
using Ryujinx.Ava.Utilities;
using Ryujinx.Common.Configuration;
using Ryujinx.Common.Logging;
using Ryujinx.HLE.FileSystem;
using Ryujinx.HLE.Loaders.Processes.Extensions;
using System;
using System.IO;
using System.Linq;
using System.Text.Json.Serialization;

namespace Ryujinx.Ava.Systems.AppLibrary
{
    // [Nextendo] INotifyPropertyChanged is implemented for ONE reason: the live "N players
    // online" badge. Everything else here is set once at scan time, but the player count changes
    // while the list is on screen, and a plain property would bind once and never update.
    public class ApplicationData : System.ComponentModel.INotifyPropertyChanged
    {
        public event System.ComponentModel.PropertyChangedEventHandler PropertyChanged;

        private int _nextendoPlayersOnline;

        /// <summary>
        /// [Nextendo] Players currently online for this title, pushed by NextendoOnlineCounts.
        /// </summary>
        [JsonIgnore]
        public int NextendoPlayersOnline
        {
            get => _nextendoPlayersOnline;
            set
            {
                if (_nextendoPlayersOnline == value)
                {
                    return;
                }

                _nextendoPlayersOnline = value;
                PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(NextendoPlayersOnline)));
                PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(NextendoPlayersText)));
                PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(HasNextendoPlayersOnline)));
            }
        }

        /// <summary>[Nextendo] Hide the badge entirely at zero rather than showing "0 online".</summary>
        [JsonIgnore]
        public bool HasNextendoPlayersOnline => _nextendoPlayersOnline > 0;

        /// <summary>[Nextendo] Localised "N players online" / "1 player online".</summary>
        [JsonIgnore]
        public string NextendoPlayersText =>
            LocaleManager.Instance.UpdateAndGetDynamicValue(
                _nextendoPlayersOnline == 1
                    ? LocaleKeys.Dialog_Nextendo_PlayerOnlineFormat
                    : LocaleKeys.Dialog_Nextendo_PlayersOnlineFormat,
                _nextendoPlayersOnline);

        public bool Favorite { get; set; }
        public bool HasIndependentConfiguration { get; set; }
        public byte[] Icon { get; set; }
        public string Name { get; set; } = "Unknown";

        private ulong _id;

        public ulong Id
        {
            get => _id;
            set
            {
                _id = value;

                Compatibility = CompatibilityDatabase.Find(value);
                RichPresenceSpec = PlayReports.Analyzer.TryGetSpec(IdString, out GameSpec gameSpec)
                    ? gameSpec
                    : default(Optional<GameSpec>);
            }
        }
        public Optional<GameSpec> RichPresenceSpec { get; set; }

        public string Developer { get; set; } = "Unknown";
        public string Version { get; set; } = "0";
        public int PlayerCount { get; set; }
        public int GameCount { get; set; }

        public bool HasLdnGames => PlayerCount != 0 && GameCount != 0;

        public bool HasRichPresenceAsset => DiscordIntegrationModule.HasAssetImage(IdString);
        public bool HasDynamicRichPresenceSupport => RichPresenceSpec.HasValue;

        public TimeSpan TimePlayed { get; set; }
        public DateTime? LastPlayed { get; set; }
        public string FileExtension { get; set; }
        public long FileSize { get; set; }
        public string Path { get; set; }
        public BlitStruct<ApplicationControlProperty> ControlHolder { get; set; }

        public bool HasControlHolder => ControlHolder.ByteSpan.Length > 0 && !ControlHolder.ByteSpan.IsZeros();

        public string TimePlayedString => ValueFormatUtils.FormatTimeSpan(TimePlayed);

        public bool HasPlayedPreviously => TimePlayed.TotalSeconds > 1;

        public string LastPlayedString => ValueFormatUtils.FormatDateTime(LastPlayed)?.Replace(" ", "\n");

        public string FileSizeString => ValueFormatUtils.FormatFileSize(FileSize);

        public Optional<CompatibilityEntry> Compatibility { get; private set; }

        public bool HasPlayabilityInfo => Compatibility.HasValue;

        public string LocalizedStatus => Compatibility.Convert(x => x.LocalizedStatus);

        public bool HasCompatibilityLabels => !FormattedCompatibilityLabels.Equals(string.Empty);

        public string FormattedCompatibilityLabels
            => Compatibility.Convert(x => x.FormattedIssueLabels).OrElse(string.Empty);

        public LocaleKeys? PlayabilityStatus => Compatibility.Convert(x => x.Status).OrElse(null);

        public bool HasPtcCacheFiles
        {
            get
            {
                DirectoryInfo mainDir = new(System.IO.Path.Combine(AppDataManager.GamesDirPath, IdString, "cache", "cpu", "0"));
                DirectoryInfo backupDir = new(System.IO.Path.Combine(AppDataManager.GamesDirPath, IdString, "cache", "cpu", "1"));

                return (mainDir.Exists && (mainDir.EnumerateFiles("*.cache").Any() || mainDir.EnumerateFiles("*.info").Any())) ||
                       (backupDir.Exists && (backupDir.EnumerateFiles("*.cache").Any() || backupDir.EnumerateFiles("*.info").Any()));
            }
        }

        public bool HasShaderCacheFiles
        {
            get
            {
                DirectoryInfo shaderCacheDir = new(System.IO.Path.Combine(AppDataManager.GamesDirPath, IdString, "cache", "shader"));

                if (!shaderCacheDir.Exists) return false;

                return shaderCacheDir.EnumerateDirectories("*").Any() || 
                       shaderCacheDir.GetFiles("*.toc").Length != 0 || 
                       shaderCacheDir.GetFiles("*.data").Length != 0;
            }
        }

        public string LocalizedStatusTooltip =>
            Compatibility.Convert(x =>
#pragma warning disable CS8509 // It is exhaustive for all possible values this can contain.
                LocaleManager.Instance[x.Status switch
#pragma warning restore CS8509
                {
                    LocaleKeys.CompatibilityListPlayable => LocaleKeys.CompatibilityListPlayableTooltip,
                    LocaleKeys.CompatibilityListIngame => LocaleKeys.CompatibilityListIngameTooltip,
                    LocaleKeys.CompatibilityListMenus => LocaleKeys.CompatibilityListMenusTooltip,
                    LocaleKeys.CompatibilityListBoots => LocaleKeys.CompatibilityListBootsTooltip,
                    LocaleKeys.CompatibilityListNothing => LocaleKeys.CompatibilityListNothingTooltip,
                }]
            ).OrElse(string.Empty);


        [JsonIgnore] public string IdString => Id.ToString("x16");

        [JsonIgnore] public ulong IdBase => Id & ~0x1FFFUL;

        [JsonIgnore] public string IdBaseString => IdBase.ToString("x16");

        public static string GetBuildId(VirtualFileSystem virtualFileSystem, IntegrityCheckLevel checkLevel, string titleFilePath)
        {
            if (!System.IO.Path.Exists(titleFilePath))
            {
                Logger.Error?.Print(LogClass.Application, $"File \"{titleFilePath}\" does not exist.");
                return string.Empty;
            }

            using FileStream file = new(titleFilePath, FileMode.Open, FileAccess.Read);

            Nca mainNca = null;
            Nca patchNca = null;

            string extension = System.IO.Path.GetExtension(titleFilePath).ToLower();

            if (extension is ".nsp" or ".xci")
            {
                IFileSystem pfs;

                if (extension == ".xci")
                {
                    Xci xci = new(virtualFileSystem.KeySet, file.AsStorage());

                    pfs = xci.OpenPartition(XciPartitionType.Secure);
                }
                else
                {
                    PartitionFileSystem pfsTemp = new();
                    pfsTemp.Initialize(file.AsStorage()).ThrowIfFailure();
                    pfs = pfsTemp;
                }

                foreach (DirectoryEntryEx fileEntry in pfs.EnumerateEntries("/", "*.nca"))
                {
                    using UniqueRef<IFile> ncaFile = new();

                    pfs.OpenFile(ref ncaFile.Ref, fileEntry.FullPath.ToU8Span(), OpenMode.Read).ThrowIfFailure();

                    Nca nca = new(virtualFileSystem.KeySet, ncaFile.Get.AsStorage());

                    if (nca.Header.ContentType != NcaContentType.Program)
                    {
                        continue;
                    }

                    int dataIndex = Nca.GetSectionIndexFromType(NcaSectionType.Data, NcaContentType.Program);

                    if (nca.Header.GetFsHeader(dataIndex).IsPatchSection())
                    {
                        patchNca = nca;
                    }
                    else
                    {
                        mainNca = nca;
                    }
                }
            }
            else if (extension == ".nca")
            {
                mainNca = new Nca(virtualFileSystem.KeySet, file.AsStorage());
            }

            if (mainNca == null)
            {
                Logger.Error?.Print(LogClass.Application, "Extraction failure. The main NCA was not present in the selected file");

                return string.Empty;
            }

            (Nca updatePatchNca, _) = mainNca.GetUpdateData(virtualFileSystem, checkLevel, 0, out string _);

            if (updatePatchNca != null)
            {
                patchNca = updatePatchNca;
            }

            IFileSystem codeFs = null;

            if (patchNca == null)
            {
                if (mainNca.CanOpenSection(NcaSectionType.Code))
                {
                    codeFs = mainNca.OpenFileSystem(NcaSectionType.Code, IntegrityCheckLevel.ErrorOnInvalid);
                }
            }
            else
            {
                if (patchNca.CanOpenSection(NcaSectionType.Code))
                {
                    codeFs = mainNca.OpenFileSystemWithPatch(patchNca, NcaSectionType.Code, IntegrityCheckLevel.ErrorOnInvalid);
                }
            }

            if (codeFs == null)
            {
                Logger.Error?.Print(LogClass.Loader, "No ExeFS found in NCA");

                return string.Empty;
            }

            const string MainExeFs = "main";

            if (!codeFs.FileExists($"/{MainExeFs}"))
            {
                Logger.Error?.Print(LogClass.Loader, "No main binary ExeFS found in ExeFS");

                return string.Empty;
            }

            using UniqueRef<IFile> nsoFile = new();

            codeFs.OpenFile(ref nsoFile.Ref, $"/{MainExeFs}".ToU8Span(), OpenMode.Read).ThrowIfFailure();

            NsoReader reader = new();
            reader.Initialize(nsoFile.Release().AsStorage().AsFile(OpenMode.Read)).ThrowIfFailure();

            return Convert.ToHexString(reader.Header.ModuleId).Replace("-", string.Empty).ToUpper()[..16];
        }

        // [Nextendo] Games with private-online support via Nextendo Network.
        [JsonIgnore]
        public bool IsNextendoCompatible => !string.IsNullOrEmpty(NextendoCompatibleVersion);

        // The single game version Nextendo Network currently supports for this title.
        // A different version can't reach the Nextendo servers (different NEX access key).
        [JsonIgnore]
        public string NextendoCompatibleVersion => IdBaseString switch
        {
            "0100152000022000" => "3.0.5",  // Mario Kart 8 Deluxe
            "01006a800016e000" => "13.0.4", // Super Smash Bros. Ultimate
            "0100f8f0000a2000" => "5.5.2",  // Splatoon 2 (EU)
            "01003bc0000a0000" => "5.5.2",  // Splatoon 2 (US)
            "01003c700009c800" => "5.5.2",  // Splatoon 2 (JP)
            _ => "",
        };

        // [Nextendo] CTGPDX variant — Mario Kart 8 Deluxe on base 3.0.3 running the verified
        // CTGP Deluxe mod. CTGPDX ships a Skyline plugin at romfs/skyline/plugins/CTGPDX.nro;
        // we hash it against the known-good CTGPDX v1.1.1 so a player can't join CTGPDX online
        // with tampered/fake tracks. A 3.0.3 install WITHOUT valid CTGPDX is blocked from online.
        private const string Mk8dxBaseId = "0100152000022000";
        private const string CtgpdxVersion = "3.0.3";
        private const string CtgpdxNroSha256 = "66771c3c609330649e95c30226528880e060ad649eadaf37db0356044d351345";

        private bool? _ctgpdxInstalled;

        [JsonIgnore]
        public bool IsCtgpdxInstalled
        {
            get
            {
                _ctgpdxInstalled ??= CheckCtgpdxMod();
                return _ctgpdxInstalled.Value;
            }
        }

        private bool CheckCtgpdxMod()
        {
            if (IdBaseString != Mk8dxBaseId)
            {
                return false;
            }

            try
            {
                foreach (string basePath in new[] { Ryujinx.HLE.HOS.ModLoader.GetModsBasePath(), Ryujinx.HLE.HOS.ModLoader.GetSdModsBasePath() })
                {
                    string dir = System.IO.Path.Combine(basePath, "contents", IdBaseString);
                    if (!Directory.Exists(dir))
                    {
                        continue;
                    }

                    foreach (string nro in Directory.EnumerateFiles(dir, "CTGPDX.nro", SearchOption.AllDirectories))
                    {
                        if (!nro.Replace('\\', '/').EndsWith("romfs/skyline/plugins/CTGPDX.nro", StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }

                        using FileStream fs = File.OpenRead(nro);
                        string hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(fs));
                        if (hash.Equals(CtgpdxNroSha256, StringComparison.OrdinalIgnoreCase))
                        {
                            return true;
                        }
                    }
                }
            }
            catch
            {
                // Unreadable mods dir -> treat as not installed (fail closed on the CTGPDX variant).
            }

            return false;
        }

        // True when THIS install is the CTGPDX variant: MK8DX base 3.0.3 + a verified CTGPDX mod.
        [JsonIgnore]
        public bool IsCtgpdxVariant => IdBaseString == Mk8dxBaseId && Version == CtgpdxVersion && IsCtgpdxInstalled;

        // Reaches Nextendo online when the installed version is the vanilla compatible version,
        // OR the CTGPDX variant (MK8DX 3.0.3 + verified CTGPDX).
        //
        // Each title has ONE supported version because the NEX access key and the protocol shape
        // are version-specific: a mismatched build doesn't play badly, it fails to talk to the
        // servers at all — and a player who gets in on the wrong version desyncs everyone else in
        // the lobby. Gating it here turns a confusing mid-match failure into a clear "wrong
        // version" applet before launch.
        //
        // This was temporarily bypassed to test vanilla MK8DX 3.0.3 online against the CTGPDX
        // crash; the test is over, so it is enforced again.
        [JsonIgnore]
        public bool IsNextendoVersionOk =>
            IsNextendoCompatible && (Version == NextendoCompatibleVersion || IsCtgpdxVariant);

        // Only Splatoon 2 needs the BCAT schedule byaml (VS/Coop/Fest schedule). The "download the
        // online schedule" prompt + context-menu button must appear ONLY for these titles — never
        // for the other online titles, which don't use it.
        [JsonIgnore]
        public bool RequiresNextendoByaml => IdBaseString switch
        {
            "0100f8f0000a2000" or "01003bc0000a0000" or "01003c700009c800" => true,
            _ => false,
        };

        [JsonIgnore]
        public string NextendoTooltip =>
            IsCtgpdxVariant
                ? LocaleManager.Instance[LocaleKeys.Dialog_Nextendo_TooltipCtgpdx]
                : LocaleManager.Instance.UpdateAndGetDynamicValue(
                    LocaleKeys.Dialog_Nextendo_TooltipVersionFormat, NextendoCompatibleVersion);

        // Badge label shown in the game list (version made visible, not just on hover).
        [JsonIgnore]
        public string NextendoBadge =>
            IsCtgpdxVariant
                ? "Nextendo Network · CTGPDX · 3.0.3"
                : $"Nextendo Network · v{NextendoCompatibleVersion}";
    }
}
