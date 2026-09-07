using LibHac.Common;
using LibHac.Fs;
using LibHac.Fs.Fsa;
using LibHac.FsSystem;
using LibHac.Loader;
using LibHac.Tools.Fs;
using LibHac.Tools.FsSystem;
using LibHac.Tools.FsSystem.RomFs;
using LibHac.Util;
using Ryujinx.Common.Configuration;
using Ryujinx.Common.Logging;
using Ryujinx.Common.Utilities;
using Ryujinx.HLE.HOS.Kernel.Process;
using Ryujinx.HLE.Loaders.Executables;
using Ryujinx.HLE.Loaders.Mods;
using Ryujinx.HLE.Loaders.Processes;
using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using LazyFile = Ryujinx.HLE.HOS.Services.Fs.FileSystemProxy.LazyFile;
using Path = System.IO.Path;

namespace Ryujinx.HLE.HOS
{
    public class ModLoader
    {
        private const string RomfsDir = "romfs";
        private const string ExefsDir = "exefs";
        private const string CheatDir = "cheats";
        private const string RomfsContainer = "romfs.bin";
        private const string ExefsContainer = "exefs.nsp";
        private const string StubExtension = ".stub";
        private const string CheatExtension = ".txt";
        private const string DefaultCheatName = "<default>";

        // ⚠️ SPLATOON 3 — AUCUN MOD, QUELLE QU'EN SOIT LA FORME.
        //
        // Le test ouvert de Splatoon 3 se joue sur les serveurs Nextendo, contre de vrais adversaires.
        // Un seul mod de jeu — portée d'une arme, cadence de tir, vitesse de déplacement, un simple
        // fichier de paramètres remplacé par LayeredFS — suffit à gâcher la partie de sept autres
        // joueurs. Et le serveur ne peut rien y voir : c'est la console qui calcule les tirs et les
        // touches, puis rapporte le résultat. Nous n'avons aucun moyen de contredire un client modifié.
        //
        // On coupe donc à la RACINE, plutôt que de tenter de distinguer un mod « de triche » d'un mod
        // cosmétique — une distinction que rien ne permet de faire de façon fiable, un mod purement
        // visuel pouvant toujours cacher une valeur de jeu. Pour ce titre, et pour lui seul, le
        // chargeur de mods ne collecte rien et n'applique rien.
        //
        // Cinq portes à fermer, pas une : romfs (dossier), romfs.bin (conteneur), exefs, les
        // correctifs IPS/pchtxt — y compris les correctifs GLOBAUX, qui ne passent pas par le cache
        // par application — et les codes de triche, dont l'activation lit son fichier directement.
        public const ulong Splatoon3ApplicationId = 0x0100C2500FC20000;

        /// <summary>Vrai si ce titre refuse tout mod (Splatoon 3 : test en ligne sans triche).</summary>
        /// <summary>
        /// L'interdiction ne vaut que sur NOS serveurs : elle existe parce que Splatoon 3 s'y
        /// joue contre de vraies personnes et qu'un seul mod de jeu gâcherait leur partie. En
        /// mode « serveur personnalisé », l'émulateur ne parle plus à Nextendo du tout, et la
        /// politique d'un serveur privé n'est pas la nôtre à faire respecter.
        /// </summary>
        /// ⚠️ La condition est <see cref="NextendoServerOverride.IsActive"/>, PAS
        /// <c>HorsNextendo</c>. HorsNextendo est vrai dès que la case est cochée, même sans
        /// adresse valide — et dans ce cas la redirection retombe sur nos serveurs. Se fier à
        /// HorsNextendo laissait donc jouer moddé SUR NOS SERVEURS PUBLICS, en éditant à la main
        /// un fichier JSON pour cocher la case sans rien remplir. IsActive exige une adresse
        /// utilisable, donc un trafic qui part réellement ailleurs.
        public static bool ModsInterdits(ulong applicationId) =>
            applicationId == Splatoon3ApplicationId && !NextendoServerOverride.IsActive;

        /// <summary>
        /// Noms des mods trouves sur le disque pour un titre qui les refuse, et donc IGNORES.
        ///
        /// On les releve au lieu de les ignorer en silence : un joueur qui a installe un mod et ne
        /// le voit pas agir croira a un bug de l'emulateur, pas a une regle. L'interface lit cette
        /// liste au lancement pour le lui dire clairement.
        /// </summary>
        public static IReadOnlyList<string> ModsRefuses => _modsRefuses;
        private static readonly List<string> _modsRefuses = [];

        /// <summary>
        /// Recense les mods installes pour un titre, SANS rien charger.
        ///
        /// Sert a prevenir le joueur AVANT le lancement : la collecte, elle, n'a lieu qu'au
        /// chargement du programme, trop tard pour afficher un message utile.
        /// </summary>
        /// <remarks>
        /// Rend le CHEMIN COMPLET du dossier, pas son nom.
        ///
        /// Un mod peut etre pose de deux facons : dans son propre dossier
        /// (contents/&lt;titre&gt;/MonMod/exefs) ou a plat, directement a la racine du titre
        /// (contents/&lt;titre&gt;/exefs). Dans le second cas le nom vaut « exefs » ou « romfs » —
        /// c'est un type de contenu, pas un nom de mod, et le message affichait donc « exefs »
        /// tout court. Un joueur qui ne se souvient pas d'avoir installe quoi que ce soit y lit
        /// une accusation incomprehensible, et n'a aucun moyen de retrouver le dossier fautif.
        /// Le chemin, lui, se lit et s'ouvre.
        /// </remarks>
        public static List<string> ModsInstallesPour(ulong applicationId)
        {
            List<string> trouves = [];

            foreach (string racine in new[] { GetModsBasePath(), GetSdModsBasePath() })
            {
                DirectoryInfo contenus = new(Path.Combine(racine, AmsContentsDir));
                if (!contenus.Exists)
                {
                    continue;
                }

                DirectoryInfo dossierDuJeu = FindApplicationDir(contenus, $"{applicationId:x16}");
                if (dossierDuJeu == null || !dossierDuJeu.Exists)
                {
                    continue;
                }

                foreach (DirectoryInfo mod in dossierDuJeu.EnumerateDirectories())
                {
                    if (mod.EnumerateFileSystemInfos().Any() && !trouves.Contains(mod.FullName))
                    {
                        trouves.Add(mod.FullName);
                    }
                }
            }

            return trouves;
        }

        /// <summary>Recense ce qui est installe pour un titre qui refuse les mods.</summary>
        private static void RecenserModsRefuses(ulong applicationId, ReadOnlySpan<string> searchDirPaths)
        {
            foreach (string racine in searchDirPaths)
            {
                DirectoryInfo contenus = new(Path.Combine(racine, AmsContentsDir));
                if (!contenus.Exists)
                {
                    continue;
                }

                DirectoryInfo dossierDuJeu = FindApplicationDir(contenus, $"{applicationId:x16}");
                if (dossierDuJeu == null || !dossierDuJeu.Exists)
                {
                    continue;
                }

                foreach (DirectoryInfo mod in dossierDuJeu.EnumerateDirectories())
                {
                    // Un dossier de mod vide n'est pas un mod : ne pas alarmer pour rien.
                    if (mod.EnumerateFileSystemInfos().Any() && !_modsRefuses.Contains(mod.Name))
                    {
                        _modsRefuses.Add(mod.Name);
                    }
                }
            }

            if (_modsRefuses.Count > 0)
            {
                Logger.Warning?.Print(LogClass.ModLoader,
                    $"[Nextendo] Application {applicationId:X16} : {_modsRefuses.Count} mod(s) IGNORE(S) — {string.Join(", ", _modsRefuses)}");
            }
        }

        private const string AmsContentsDir = "contents";
        private const string AmsNsoPatchDir = "exefs_patches";
        private const string AmsNroPatchDir = "nro_patches";
        private const string AmsKipPatchDir = "kip_patches";

        private static readonly ModMetadataJsonSerializerContext _serializerContext = new(JsonHelper.GetDefaultSerializerOptions());

        public readonly struct Mod<T> where T : FileSystemInfo
        {
            public readonly string Name;
            public readonly T Path;
            public readonly bool Enabled;

            public Mod(string name, T path, bool enabled)
            {
                Name = name;
                Path = path;
                Enabled = enabled;
            }
        }

        public struct Cheat
        {
            // Atmosphere identifies the executables with the first 8 bytes
            // of the build id, which is equivalent to 16 hex digits.
            public const int CheatIdSize = 16;

            public readonly string Name;
            public readonly FileInfo Path;
            public readonly IEnumerable<String> Instructions;

            public Cheat(string name, FileInfo path, IEnumerable<String> instructions)
            {
                Name = name;
                Path = path;
                Instructions = instructions;
            }
        }

        // Application dependent mods
        public class ModCache
        {
            public List<Mod<FileInfo>> RomfsContainers { get; }
            public List<Mod<FileInfo>> ExefsContainers { get; }

            public List<Mod<DirectoryInfo>> RomfsDirs { get; }
            public List<Mod<DirectoryInfo>> ExefsDirs { get; }

            public List<Cheat> Cheats { get; }

            public ModCache()
            {
                RomfsContainers = [];
                ExefsContainers = [];
                RomfsDirs = [];
                ExefsDirs = [];
                Cheats = [];
            }
        }

        // Application independent mods
        private class PatchCache
        {
            public List<Mod<DirectoryInfo>> NsoPatches { get; }
            public List<Mod<DirectoryInfo>> NroPatches { get; }
            public List<Mod<DirectoryInfo>> KipPatches { get; }

            internal bool Initialized { get; set; }

            public PatchCache()
            {
                NsoPatches = [];
                NroPatches = [];
                KipPatches = [];

                Initialized = false;
            }
        }

        private readonly Dictionary<ulong, ModCache> _appMods; // key is ApplicationId
        private PatchCache _patches;

        private static readonly EnumerationOptions _dirEnumOptions = new()
        {
            MatchCasing = MatchCasing.CaseInsensitive,
            MatchType = MatchType.Simple,
            RecurseSubdirectories = false,
            ReturnSpecialDirectories = false,
        };

        public ModLoader()
        {
            _appMods = new Dictionary<ulong, ModCache>();
            _patches = new PatchCache();
        }

        private void Clear()
        {
            _appMods.Clear();
            _patches = new PatchCache();
            _modsRefuses.Clear();
        }

        private static bool StrEquals(string s1, string s2) => string.Equals(s1, s2, StringComparison.OrdinalIgnoreCase);

        public static string GetModsBasePath() => EnsureBaseDirStructure(AppDataManager.GetModsPath());
        public static string GetSdModsBasePath() => EnsureBaseDirStructure(AppDataManager.GetSdModsPath());

        private static string EnsureBaseDirStructure(string modsBasePath)
        {
            DirectoryInfo modsDir = new(modsBasePath);

            modsDir.CreateSubdirectory(AmsContentsDir);
            modsDir.CreateSubdirectory(AmsNsoPatchDir);
            modsDir.CreateSubdirectory(AmsNroPatchDir);
            // TODO: uncomment when KIPs are supported
            // modsDir.CreateSubdirectory(AmsKipPatchDir);

            return modsDir.FullName;
        }

        private static DirectoryInfo FindApplicationDir(DirectoryInfo contentsDir, string applicationId)
            => contentsDir.EnumerateDirectories(applicationId, _dirEnumOptions).FirstOrDefault();

        private static void AddModsFromDirectory(ModCache mods, DirectoryInfo dir, ModMetadata modMetadata)
        {
            System.Text.StringBuilder types = new();

            foreach (DirectoryInfo modDir in dir.EnumerateDirectories())
            {
                types.Clear();
                Mod<DirectoryInfo> mod = new(string.Empty, null, true);

                if (StrEquals(RomfsDir, modDir.Name))
                {
                    Mod modData = modMetadata.Mods.FirstOrDefault(x => modDir.Parent.FullName.Equals(x.Path));
                    bool enabled = modData?.Enabled ?? true;

                    mods.RomfsDirs.Add(mod = new Mod<DirectoryInfo>(dir.Name, modDir, enabled));
                    types.Append('R');
                }
                else if (StrEquals(ExefsDir, modDir.Name))
                {
                    Mod modData = modMetadata.Mods.FirstOrDefault(x => modDir.Parent.FullName.Equals(x.Path));
                    bool enabled = modData?.Enabled ?? true;

                    mods.ExefsDirs.Add(mod = new Mod<DirectoryInfo>(dir.Name, modDir, enabled));
                    types.Append('E');
                }
                else if (StrEquals(CheatDir, modDir.Name))
                {
                    types.Append('C', QueryCheatsDir(mods, modDir));
                }
                else
                {
                    AddModsFromDirectory(mods, modDir, modMetadata);
                }

                if (types.Length > 0)
                {
                    Logger.Info?.Print(LogClass.ModLoader, $"Found {(mod.Enabled ? "enabled" : "disabled")} mod '{mod.Name}' [{types}]");
                }
            }
        }

        public static string GetApplicationDir(string modsBasePath, string applicationId)
        {
            DirectoryInfo contentsDir = new(Path.Combine(modsBasePath, AmsContentsDir));
            DirectoryInfo applicationModsPath = FindApplicationDir(contentsDir, applicationId);

            if (applicationModsPath == null)
            {
                Logger.Info?.Print(LogClass.ModLoader, $"Creating mods directory for Application {applicationId.ToUpper()}");
                applicationModsPath = contentsDir.CreateSubdirectory(applicationId);
            }

            return applicationModsPath.FullName;
        }

        // Static Query Methods
        private static void QueryPatchDirs(PatchCache cache, DirectoryInfo patchDir)
        {
            if (cache.Initialized || !patchDir.Exists)
            {
                return;
            }

            List<Mod<DirectoryInfo>> patches;
            string type;

            if (StrEquals(AmsNsoPatchDir, patchDir.Name))
            {
                patches = cache.NsoPatches;
                type = "NSO";
            }
            else if (StrEquals(AmsNroPatchDir, patchDir.Name))
            {
                patches = cache.NroPatches;
                type = "NRO";
            }
            else if (StrEquals(AmsKipPatchDir, patchDir.Name))
            {
                patches = cache.KipPatches;
                type = "KIP";
            }
            else
            {
                return;
            }

            foreach (DirectoryInfo modDir in patchDir.EnumerateDirectories())
            {
                patches.Add(new Mod<DirectoryInfo>(modDir.Name, modDir, true));
                Logger.Info?.Print(LogClass.ModLoader, $"Found {type} patch '{modDir.Name}'");
            }
        }

        private static void QueryApplicationDir(ModCache mods, DirectoryInfo applicationDir, ulong applicationId)
        {
            if (!applicationDir.Exists)
            {
                return;
            }

            string modJsonPath = Path.Combine(AppDataManager.GamesDirPath, applicationId.ToString("x16"), "mods.json");
            ModMetadata modMetadata = new();

            if (File.Exists(modJsonPath))
            {
                try
                {
                    modMetadata = JsonHelper.DeserializeFromFile(modJsonPath, _serializerContext.ModMetadata);
                }
                catch
                {
                    Logger.Warning?.Print(LogClass.ModLoader, $"Failed to deserialize mod data for {applicationId:X16} at {modJsonPath}");
                }
            }

            FileInfo fsFile = new(Path.Combine(applicationDir.FullName, RomfsContainer));
            if (fsFile.Exists)
            {
                Mod modData = modMetadata.Mods.FirstOrDefault(x => fsFile.FullName.Contains(x.Path));
                bool enabled = modData == null || modData.Enabled;

                mods.RomfsContainers.Add(new Mod<FileInfo>($"<{applicationDir.Name} RomFs>", fsFile, enabled));
            }

            fsFile = new FileInfo(Path.Combine(applicationDir.FullName, ExefsContainer));
            if (fsFile.Exists)
            {
                Mod modData = modMetadata.Mods.FirstOrDefault(x => fsFile.FullName.Contains(x.Path));
                bool enabled = modData == null || modData.Enabled;

                mods.ExefsContainers.Add(new Mod<FileInfo>($"<{applicationDir.Name} ExeFs>", fsFile, enabled));
            }

            AddModsFromDirectory(mods, applicationDir, modMetadata);
        }

        public static void QueryContentsDir(ModCache mods, DirectoryInfo contentsDir, ulong applicationId, ulong[] installedDlcs)
        {
            if (!contentsDir.Exists)
            {
                return;
            }

            Logger.Info?.Print(LogClass.ModLoader, $"Searching mods for {((applicationId & 0x1000) != 0 ? "DLC" : "Application")} {applicationId:X16} in \"{contentsDir.FullName}\"");

            DirectoryInfo applicationDir = FindApplicationDir(contentsDir, $"{applicationId:x16}");

            if (applicationDir != null)
            {
                QueryApplicationDir(mods, applicationDir, applicationId);
            }

            foreach (ulong installedDlcId in installedDlcs)
            {
                DirectoryInfo dlcModDir = FindApplicationDir(contentsDir, $"{installedDlcId:x16}");

                if (dlcModDir != null)
                {
                    QueryApplicationDir(mods, dlcModDir, applicationId);
                }
            }
        }

        private static int QueryCheatsDir(ModCache mods, DirectoryInfo cheatsDir)
        {
            if (!cheatsDir.Exists)
            {
                return 0;
            }

            int numMods = 0;

            foreach (FileInfo file in cheatsDir.EnumerateFiles())
            {
                if (!StrEquals(CheatExtension, file.Extension))
                {
                    continue;
                }

                string cheatId = Path.GetFileNameWithoutExtension(file.Name);

                if (cheatId.Length != Cheat.CheatIdSize)
                {
                    continue;
                }

                if (!ulong.TryParse(cheatId, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out _))
                {
                    continue;
                }

                int oldCheatsCount = mods.Cheats.Count;

                // A cheat file can contain several cheats for the same executable, so the file must be parsed in
                // order to properly enumerate them.
                mods.Cheats.AddRange(GetCheatsInFile(file));

                if (mods.Cheats.Count - oldCheatsCount > 0)
                {
                    numMods++;
                }
            }

            return numMods;
        }

        private static IEnumerable<Cheat> GetCheatsInFile(FileInfo cheatFile)
        {
            string cheatName = DefaultCheatName;
            List<string> instructions = [];

            using StreamReader cheatData = cheatFile.OpenText();
            while (cheatData.ReadLine() is { } line)
            {
                line = line.Trim();

                if (line.StartsWith('['))
                {
                    // This line starts a new cheat section.
                    if (!line.EndsWith(']') || line.Length < 3)
                    {
                        // Skip the entire file if there's any error while parsing the cheat file.

                        Logger.Warning?.Print(LogClass.ModLoader, $"Ignoring cheat '{cheatFile.FullName}' because it is malformed");

                        yield break;
                    }

                    // Add the previous section to the list.
                    if (instructions.Count > 0)
                    {
                        yield return new Cheat($"<{cheatName} Cheat>", cheatFile, instructions);
                    }

                    // Start a new cheat section.
                    cheatName = line[1..^1];
                    instructions = [];
                }
                else if (line.Length > 0)
                {
                    // The line contains an instruction.
                    instructions.Add(line);
                }
            }

            // Add the last section being processed.
            if (instructions.Count > 0)
            {
                yield return new Cheat($"<{cheatName} Cheat>", cheatFile, instructions);
            }
        }

        // Assumes searchDirPaths don't overlap
        private static void CollectMods(Dictionary<ulong, ModCache> modCaches, PatchCache patches, params ReadOnlySpan<string> searchDirPaths)
        {
            static bool IsPatchesDir(string name) => StrEquals(AmsNsoPatchDir, name) ||
                                                     StrEquals(AmsNroPatchDir, name) ||
                                                     StrEquals(AmsKipPatchDir, name);

            static bool IsContentsDir(string name) => StrEquals(AmsContentsDir, name);

            static bool TryQuery(DirectoryInfo searchDir, PatchCache patches, Dictionary<ulong, ModCache> modCaches)
            {
                if (IsContentsDir(searchDir.Name))
                {
                    foreach ((ulong applicationId, ModCache cache) in modCaches)
                    {
                        QueryContentsDir(cache, searchDir, applicationId, Array.Empty<ulong>());
                    }

                    return true;
                }
                else if (IsPatchesDir(searchDir.Name))
                {
                    QueryPatchDirs(patches, searchDir);

                    return true;
                }

                return false;
            }

            foreach (string path in searchDirPaths)
            {
                DirectoryInfo searchDir = new(path);
                if (!searchDir.Exists)
                {
                    Logger.Warning?.Print(LogClass.ModLoader, $"Mod Search Dir '{searchDir.FullName}' doesn't exist");
                    return;
                }

                if (!TryQuery(searchDir, patches, modCaches))
                {
                    foreach (DirectoryInfo subdir in searchDir.EnumerateDirectories())
                    {
                        TryQuery(subdir, patches, modCaches);
                    }
                }
            }

            patches.Initialized = true;
        }

        public void CollectMods(IEnumerable<ulong> applications, params ReadOnlySpan<string> searchDirPaths)
        {
            Clear();

            foreach (ulong applicationId in applications)
            {
                if (ModsInterdits(applicationId))
                {
                    Logger.Info?.Print(LogClass.ModLoader, $"Application {applicationId:X16} : mods desactives (test en ligne Nextendo) — rien ne sera collecte");
                    RecenserModsRefuses(applicationId, searchDirPaths);

                    continue;
                }

                _appMods[applicationId] = new ModCache();
            }

            CollectMods(_appMods, _patches, searchDirPaths);

            // Ceinture ET bretelles : si un chemin de collecte venait a creer l'entree malgre tout,
            // on la retire. Le cout est nul, et l'oubli couterait une partie truquee.
            foreach (ulong applicationId in _appMods.Keys.Where(ModsInterdits).ToList())
            {
                _appMods.Remove(applicationId);
            }
        }

        internal IStorage ApplyRomFsMods(ulong applicationId, IStorage baseStorage)
        {
            if (ModsInterdits(applicationId))
            {
                return baseStorage;
            }

            if (!_appMods.TryGetValue(applicationId, out ModCache mods) || mods.RomfsDirs.Count + mods.RomfsContainers.Count == 0)
            {
                return baseStorage;
            }

            HashSet<string> fileSet = [];
            RomFsBuilder builder = new();
            int count = 0;

            Logger.Info?.Print(LogClass.ModLoader, $"Applying RomFS mods for Application {applicationId:X16}");

            // Prioritize loose files first
            foreach (Mod<DirectoryInfo> mod in mods.RomfsDirs)
            {
                if (!mod.Enabled)
                {
                    continue;
                }

                using (IFileSystem fs = new LocalFileSystem(mod.Path.FullName))
                {
                    AddFiles(fs, mod.Name, mod.Path.FullName, fileSet, builder);
                }

                count++;
            }

            // Then files inside images
            foreach (Mod<FileInfo> mod in mods.RomfsContainers)
            {
                if (!mod.Enabled)
                {
                    continue;
                }

                Logger.Info?.Print(LogClass.ModLoader, $"Found 'romfs.bin' for Application {applicationId:X16}");
                using (IFileSystem fs = new RomFsFileSystem(mod.Path.OpenRead().AsStorage()))
                {
                    AddFiles(fs, mod.Name, mod.Path.FullName, fileSet, builder);
                }

                count++;
            }

            if (fileSet.Count == 0)
            {
                Logger.Info?.Print(LogClass.ModLoader, "No files found. Using base RomFS");

                return baseStorage;
            }

            Logger.Info?.Print(LogClass.ModLoader, $"Replaced {fileSet.Count} file(s) over {count} mod(s). Processing base storage...");

            // And finally, the base romfs
            RomFsFileSystem baseRom = new(baseStorage);
            foreach (DirectoryEntryEx entry in baseRom.EnumerateEntries()
                                         .Where(f => f.Type == DirectoryEntryType.File && !fileSet.Contains(f.FullPath))
                                         .OrderBy(f => f.FullPath, StringComparer.Ordinal))
            {
                using UniqueRef<IFile> file = new();

                baseRom.OpenFile(ref file.Ref, entry.FullPath.ToU8Span(), OpenMode.Read).ThrowIfFailure();
                builder.AddFile(entry.FullPath, file.Release());
            }

            Logger.Info?.Print(LogClass.ModLoader, "Building new RomFS...");
            IStorage newStorage = builder.Build();
            Logger.Info?.Print(LogClass.ModLoader, "Using modded RomFS");

            return newStorage;
        }

        private static void AddFiles(IFileSystem fs, string modName, string rootPath, HashSet<string> fileSet, RomFsBuilder builder)
        {
            foreach (DirectoryEntryEx entry in fs.EnumerateEntries()
                                    .AsParallel()
                                    .Where(f => f.Type == DirectoryEntryType.File)
                                    .OrderBy(f => f.FullPath, StringComparer.Ordinal))
            {
                LazyFile file = new(entry.FullPath, rootPath, fs);

                if (fileSet.Add(entry.FullPath))
                {
                    builder.AddFile(entry.FullPath, file);
                }
                else
                {
                    Logger.Warning?.Print(LogClass.ModLoader, $"    Skipped duplicate file '{entry.FullPath}' from '{modName}'", "ApplyRomFsMods");
                }
            }
        }

        internal bool ReplaceExefsPartition(ulong applicationId, ref IFileSystem exefs)
        {
            if (!_appMods.TryGetValue(applicationId, out ModCache mods) || mods.ExefsContainers.Count == 0)
            {
                return false;
            }

            if (mods.ExefsContainers.Count > 1)
            {
                Logger.Warning?.Print(LogClass.ModLoader, "Multiple ExeFS partition replacements detected");
            }

            Logger.Info?.Print(LogClass.ModLoader, "Using replacement ExeFS partition");

            PartitionFileSystem pfs = new();
            pfs.Initialize(mods.ExefsContainers[0].Path.OpenRead().AsStorage()).ThrowIfFailure();
            exefs = pfs;

            return true;
        }

        public struct ModLoadResult
        {
            public BitVector32 Stubs;
            public BitVector32 Replaces;
            public MetaLoader Npdm;
            public string Hash;

            public bool Modified => (Stubs.Data | Replaces.Data) != 0;
        }

        internal ModLoadResult ApplyExefsMods(ulong applicationId, NsoExecutable[] nsos)
        {
            ModLoadResult modLoadResult = new()
            {
                Stubs = new BitVector32(),
                Replaces = new BitVector32(),
                Hash = null,
            };

            string tempHash = string.Empty;

            if (ModsInterdits(applicationId))
            {
                return modLoadResult;
            }

            if (!_appMods.TryGetValue(applicationId, out ModCache mods) || mods.ExefsDirs.Count == 0)
            {
                return modLoadResult;
            }

            if (nsos.Length != ProcessConst.ExeFsPrefixes.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(nsos), nsos.Length, "NSO Count is incorrect");
            }

            List<Mod<DirectoryInfo>> exeMods = mods.ExefsDirs;

            foreach (Mod<DirectoryInfo> mod in exeMods)
            {
                if (!mod.Enabled)
                {
                    continue;
                }

                for (int i = 0; i < ProcessConst.ExeFsPrefixes.Length; ++i)
                {
                    string nsoName = ProcessConst.ExeFsPrefixes[i];

                    FileInfo nsoFile = new(Path.Combine(mod.Path.FullName, nsoName));
                    if (nsoFile.Exists)
                    {
                        if (modLoadResult.Replaces[1 << i])
                        {
                            Logger.Warning?.Print(LogClass.ModLoader, $"Multiple replacements to '{nsoName}'");

                            continue;
                        }

                        modLoadResult.Replaces[1 << i] = true;

                        using FileStream stream = nsoFile.OpenRead();
                        nsos[i] = new NsoExecutable(stream.AsStorage(), nsoName);
                        Logger.Info?.Print(LogClass.ModLoader, $"NSO '{nsoName}' replaced");
                        stream.Seek(0, SeekOrigin.Begin);
                        tempHash += Convert.ToHexStringLower(MD5.HashData(stream));
                    }

                    modLoadResult.Stubs[1 << i] |= File.Exists(Path.Combine(mod.Path.FullName, nsoName + StubExtension));
                }

                FileInfo npdmFile = new(Path.Combine(mod.Path.FullName, "main.npdm"));
                if (npdmFile.Exists)
                {
                    if (modLoadResult.Npdm != null)
                    {
                        Logger.Warning?.Print(LogClass.ModLoader, "Multiple replacements to 'main.npdm'");

                        continue;
                    }

                    modLoadResult.Npdm = new MetaLoader();
                    modLoadResult.Npdm.Load(File.ReadAllBytes(npdmFile.FullName));

                    Logger.Info?.Print(LogClass.ModLoader, "main.npdm replaced");
                }
            }

            for (int i = ProcessConst.ExeFsPrefixes.Length - 1; i >= 0; --i)
            {
                if (modLoadResult.Stubs[1 << i] && !modLoadResult.Replaces[1 << i]) // Prioritizes replacements over stubs
                {
                    Logger.Info?.Print(LogClass.ModLoader, $"    NSO '{nsos[i].Name}' stubbed");
                    nsos[i] = null;
                }
            }

            if (!string.IsNullOrEmpty(tempHash))
            {
                modLoadResult.Hash += Convert.ToHexStringLower(MD5.HashData(tempHash.ToBytes()));
            }

            return modLoadResult;
        }

        internal void ApplyNroPatches(NroExecutable nro)
        {
            List<Mod<DirectoryInfo>> nroPatches = _patches.NroPatches;

            if (nroPatches.Count == 0)
            {
                return;
            }

            // NRO patches aren't offset relative to header unlike NSO
            // according to Atmosphere's ro patcher module
            ApplyProgramPatches(nroPatches, 0, nro);
        }

        internal bool ApplyNsoPatches(ulong applicationId, params ReadOnlySpan<IExecutable> programs)
        {
            // ⚠️ Sortir AVANT _patches.NsoPatches : ces correctifs-la sont GLOBAUX (mods/exefs_patches
            // et sdcard/atmosphere/exefs_patches), ils ne passent pas par le cache par application.
            // Sans cette garde, un simple .pchtxt depose la s'appliquerait a Splatoon 3 malgre tout.
            //
            // Mais Splatoon 3 a BESOIN de deux correctifs pour parler a nos serveurs (contournement
            // du certificat epingle, nom de pair). Ils ne viennent plus du disque : ils sont integres
            // au binaire (NextendoS3Patches). Le disque est donc refuse en bloc, sans exception a
            // percer — et le joueur n'a aucun fichier a installer.
            if (ModsInterdits(applicationId))
            {
                return AppliquerCorrectifsIntegres(programs);
            }

            AppliquerCorrectifsIntegres(programs);

            IEnumerable<Mod<DirectoryInfo>> nsoMods = _patches.NsoPatches;

            if (_appMods.TryGetValue(applicationId, out ModCache mods))
            {
                nsoMods = nsoMods.Concat(mods.ExefsDirs);
            }

            // NSO patches are created with offset 0 according to Atmosphere's patcher module
            // But `Program` doesn't contain the header which is 0x100 bytes. So, we adjust for that here
            return ApplyProgramPatches(nsoMods, 0x100, programs);
        }

        internal void LoadCheats(ulong applicationId, ProcessTamperInfo tamperInfo, TamperMachine tamperMachine)
        {
            if (ModsInterdits(applicationId))
            {
                Logger.Info?.Print(LogClass.ModLoader, $"Application {applicationId:X16} : codes de triche desactives (test en ligne Nextendo)");

                return;
            }

            if (tamperInfo?.BuildIds == null || tamperInfo.CodeAddresses == null)
            {
                Logger.Error?.Print(LogClass.ModLoader, "Unable to install cheat because the associated process is invalid");

                return;
            }

            Logger.Info?.Print(LogClass.ModLoader, $"Build ids found for application {applicationId:X16}:\n    {String.Join("\n    ", tamperInfo.BuildIds)}");

            if (!_appMods.TryGetValue(applicationId, out ModCache mods) || mods.Cheats.Count == 0)
            {
                return;
            }

            List<Cheat> cheats = mods.Cheats;
            Dictionary<string, ulong> processExes = tamperInfo.BuildIds.Zip(tamperInfo.CodeAddresses, (k, v) => new { k, v })
                .ToDictionary(x => x.k[..Math.Min(Cheat.CheatIdSize, x.k.Length)], x => x.v);

            foreach (Cheat cheat in cheats)
            {
                string cheatId = Path.GetFileNameWithoutExtension(cheat.Path.Name).ToUpper();

                if (!processExes.TryGetValue(cheatId, out ulong exeAddress))
                {
                    Logger.Warning?.Print(LogClass.ModLoader, $"Skipping cheat '{cheat.Name}' because no executable matches its BuildId {cheatId} (check if the game title and version are correct)");

                    continue;
                }

                Logger.Info?.Print(LogClass.ModLoader, $"Installing cheat '{cheat.Name}'");

                tamperMachine.InstallAtmosphereCheat(cheat.Name, cheatId, cheat.Instructions, tamperInfo, exeAddress);
            }

            EnableCheats(applicationId, tamperMachine);
        }

        internal static void EnableCheats(ulong applicationId, TamperMachine tamperMachine)
        {
            // Cette methode lit enabled.txt SUR LE DISQUE, sans passer par le cache des mods : elle
            // doit donc porter sa propre garde, sinon elle rouvre seule la porte qu'on vient de fermer.
            if (ModsInterdits(applicationId))
            {
                return;
            }

            DirectoryInfo contentDirectory = FindApplicationDir(new DirectoryInfo(Path.Combine(GetModsBasePath(), AmsContentsDir)), $"{applicationId:x16}");
            string enabledCheatsPath = Path.Combine(contentDirectory.FullName, CheatDir, "enabled.txt");

            if (File.Exists(enabledCheatsPath))
            {
                tamperMachine.EnableCheats(File.ReadAllLines(enabledCheatsPath));
            }
        }

        /// <summary>
        /// Applique aux programmes les correctifs INTEGRES au binaire (Splatoon 3), sans jamais
        /// toucher au disque. Meme mecanique que ApplyProgramPatches : on apparie par identifiant de
        /// build, puis on ecrit avec le decalage 0x100 propre aux NSO (l'en-tete n'est pas dans
        /// Program).
        /// </summary>
        private static bool AppliquerCorrectifsIntegres(params ReadOnlySpan<IExecutable> programs)
        {
            int count = 0;

            for (int i = 0; i < programs.Length; ++i)
            {
                if (programs[i] is not NsoExecutable nso)
                {
                    continue;
                }

                MemPatch patch = new();

                string buildId = Convert.ToHexString(nso.BuildId).TrimEnd('0');

                if (NextendoS3Patches.Verser(buildId, patch) + NextendoStardewPatches.Verser(buildId, patch) == 0)
                {
                    continue;
                }

                count += patch.Patch(programs[i].Program, 0x100);
            }

            return count > 0;
        }

        private static bool ApplyProgramPatches(IEnumerable<Mod<DirectoryInfo>> mods, int protectedOffset, params ReadOnlySpan<IExecutable> programs)
        {
            int count = 0;

            MemPatch[] patches = new MemPatch[programs.Length];

            for (int i = 0; i < patches.Length; ++i)
            {
                patches[i] = new MemPatch();
            }

            List<string> buildIds = new(programs.Length);

            foreach (IExecutable p in programs)
            {
                string buildId = p switch
                {
                    NsoExecutable nso => Convert.ToHexString(nso.BuildId).TrimEnd('0'),
                    NroExecutable nro => Convert.ToHexString(nro.Header.BuildId).TrimEnd('0'),
                    _ => string.Empty,
                };
                buildIds.Add(buildId);
            }

            int GetIndex(string buildId) => buildIds.FindIndex(id => id == buildId); // O(n) but list is small

            // Collect patches
            foreach (Mod<DirectoryInfo> mod in mods)
            {
                if (!mod.Enabled)
                {
                    continue;
                }

                DirectoryInfo patchDir = mod.Path;
                foreach (FileInfo patchFile in patchDir.EnumerateFiles())
                {
                    if (StrEquals(".ips", patchFile.Extension)) // IPS|IPS32
                    {
                        string filename = Path.GetFileNameWithoutExtension(patchFile.FullName).Split('.')[0];
                        string buildId = filename.TrimEnd('0');

                        int index = GetIndex(buildId);
                        if (index == -1)
                        {
                            continue;
                        }

                        Logger.Info?.Print(LogClass.ModLoader, $"Matching IPS patch '{patchFile.Name}' in '{mod.Name}' bid={buildId}");

                        using FileStream fs = patchFile.OpenRead();
                        using BinaryReader reader = new(fs);

                        IpsPatcher patcher = new(reader);
                        patcher.AddPatches(patches[index]);
                    }
                    else if (StrEquals(".pchtxt", patchFile.Extension)) // IPSwitch
                    {
                        using FileStream fs = patchFile.OpenRead();
                        using StreamReader reader = new(fs);

                        IPSwitchPatcher patcher = new(reader);

                        int index = GetIndex(patcher.BuildId);
                        if (index == -1)
                        {
                            continue;
                        }

                        Logger.Info?.Print(LogClass.ModLoader, $"Matching IPSwitch patch '{patchFile.Name}' in '{mod.Name}' bid={patcher.BuildId}");

                        patcher.AddPatches(patches[index]);
                    }
                }
            }

            // Apply patches
            for (int i = 0; i < programs.Length; ++i)
            {
                count += patches[i].Patch(programs[i].Program, protectedOffset);
            }

            return count > 0;
        }
    }
}
