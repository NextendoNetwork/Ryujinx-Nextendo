using Avalonia.Threading;
using FluentAvalonia.UI.Controls;
using Ryujinx.Ava.Systems.AppLibrary;
using Ryujinx.Common.Configuration;
using Ryujinx.Common.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Threading.Tasks;

namespace Ryujinx.Ava.Common
{
    /// <summary>
    /// [Nextendo] BCAT schedule (byaml) delivery. Some Nextendo titles (Splatoon 2) need a
    /// schedule/festival byaml in the local BCAT seed for online to work — without it the game
    /// bounces "offline". We do NOT bundle Nintendo data in the emulator pack: instead the file
    /// is offered for download from the Nextendo server on first launch (and re-downloadable from
    /// the game's right-click menu) into &lt;exe dir&gt;/bcat-seed/, which the BcatSeed override reads.
    /// </summary>
    public static class NextendoByamlSync
    {
        private static string BaseUrl()
        {
            // [Nextendo] Une seule decision, dans NextendoEndpoint : c'est elle qui choisit qui
            // recoit le jeton du compte. Cette logique etait dupliquee ici et acceptait
            // n'importe quelle valeur de NEXTENDO_API.
            return NextendoEndpoint.BaseUrl();
        }

        // Same root the BcatSeed override serves from. MUST be a WRITABLE location: on macOS the
        // .app bundle (AppContext.BaseDirectory) is read-only, so extracting the schedule there
        // failed with "download failed" — likewise under Program Files on Windows. AppDataManager's
        // base dir is always writable (and portable-mode aware).
        private const string VioletTitleId = "01008f6008c5e000";
        private const string ScarletTitleId = "0100a3d008c5c000";
        private const string LegendsZaTitleId = "0100f43008c44000";
        private const string SwitchSportsTitleId = "0100d2f00d5c0000";
        private const string SwordTitleId = "0100abf008968000";
        private const string ShieldTitleId = "01008db008c2c000";
        private const string SmashUltimateTitleId = "01006a800016e000";
        private static string SeedRoot => Path.Combine(AppDataManager.BaseDirPath, "bcat-seed");
        // Titles whose BCAT is a plain package installed into its own seed folder.
        private static bool IsPokemon(ApplicationData app) => app?.IdBaseString is
            VioletTitleId or ScarletTitleId or LegendsZaTitleId or SwitchSportsTitleId or
            SwordTitleId or ShieldTitleId or SmashUltimateTitleId;
        private static string PokemonSeedRoot(ApplicationData app) => Path.Combine(SeedRoot, app.IdBaseString);

        // [Nextendo] Legacy next-to-exe seed location REMOVED: it let a stale copy shadow the live
        // writable one (wrong Splatoon 2 rotation) and was copied to other emulators. Only SeedRoot
        // (writable, server-synced) is honoured now.

        // The marker that tells us BCAT content is already installed for this title.
        // One source of truth: ApplicationData.RequiresNextendoByaml.
        public static bool RequiresByaml(ApplicationData app) => app != null && app.RequiresNextendoByaml;

        public static bool IsInstalled(ApplicationData app)
        {
            if (app == null)
            {
                return false;
            }

            if (app.IdBaseString == Ryujinx.Common.NextendoSplatoon3Bcat.TitleId)
            {
                return Ryujinx.Common.NextendoSplatoon3Bcat.IsInstalled(
                    Path.Combine(SeedRoot, Ryujinx.Common.NextendoSplatoon3Bcat.TitleId));
            }

            if (IsPokemon(app))
            {
                string titleRoot = PokemonSeedRoot(app);
                return Directory.Exists(titleRoot) && Directory.EnumerateFiles(titleRoot, "*", SearchOption.AllDirectories).Any();
            }

            // vsdata/VSSetting_0.byaml is the load-bearing schedule file (writable, server-synced).
            return File.Exists(Path.Combine(SeedRoot, "vsdata", "VSSetting_0.byaml"));
        }

        // Stores the SHA-256 of the server BCAT zip currently extracted locally, so we can tell on
        // the next launch whether the server's schedule has changed.
        private static string VersionFilePath => Path.Combine(AppDataManager.BaseDirPath, "nextendo_bcat_version.txt");

        // Les dossiers de premier niveau que la derniere synchronisation a poses. Sans cette
        // memoire, on ne saurait pas quoi retirer quand le serveur cesse de servir un dossier.
        private static string DirsFilePath => Path.Combine(AppDataManager.BaseDirPath, "nextendo_bcat_dirs.txt");

        // Les noms de premier niveau contenus dans une archive : « vsdata/VSSetting_0.byaml » rend
        // « vsdata ». C'est le perimetre exact de ce que cette archive possede.
        private static HashSet<string> RacinesDe(ZipArchive archive)
        {
            HashSet<string> out_ = new(StringComparer.OrdinalIgnoreCase);
            foreach (ZipArchiveEntry e in archive.Entries)
            {
                // Un zip ecrit sous Windows peut separer avec le caractere 92 au lieu de la barre :
                // on coupe sur les deux.
                string nom = e.FullName.Split(new[] { '/', (char)92 },
                    StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
                if (!string.IsNullOrEmpty(nom))
                {
                    out_.Add(nom);
                }
            }

            return out_;
        }

        private static HashSet<string> RacinesInstallees()
        {
            HashSet<string> out_ = new(StringComparer.OrdinalIgnoreCase);
            try
            {
                if (File.Exists(DirsFilePath))
                {
                    foreach (string l in File.ReadAllLines(DirsFilePath))
                    {
                        string nom = l.Trim();
                        if (nom.Length > 0)
                        {
                            out_.Add(nom);
                        }
                    }
                }
            }
            catch { /* liste inconnue : on ne retire rien de plus que ce que l'archive apporte */ }

            return out_;
        }

        /// <summary>
        /// [Nextendo] Force-sync the local BCAT schedule with the server on launch. IsInstalled()
        /// only checks a file EXISTS — it never noticed when the server's schedule changed, so a
        /// player kept an outdated rotation forever and got communication errors against players on
        /// the current one. Here we fetch the server zip, hash it, and if it differs from what's
        /// installed we WIPE the seed and re-extract the server copy — no prompt, forced. Best-effort:
        /// on any failure (offline, server down) we log and launch with whatever is local.
        /// Returns true if the local schedule was refreshed.
        /// </summary>
        public static async Task<bool> EnsureUpToDateAsync(ApplicationData app)
        {
            if (app == null || !RequiresByaml(app))
            {
                return false;
            }

            if (app.IdBaseString == Ryujinx.Common.NextendoSplatoon3Bcat.TitleId)
            {
                return await EnsureSplatoon3BcatAsync();
            }

            if (IsPokemon(app))
            {
                return await EnsurePokemonBcatAsync(app);
            }

            try
            {
                using HttpClient http = new() { Timeout = TimeSpan.FromSeconds(15) };
                NextendoApi.AddAppHeader(http);
                if (!string.IsNullOrEmpty(NextendoAccount.NexToken))
                {
                    http.DefaultRequestHeaders.Add("Authorization", "Bearer " + NextendoAccount.NexToken);
                }

                byte[] zip = await http.GetByteArrayAsync($"{BaseUrl()}/api/bcat/{app.IdString}");
                if (zip == null || zip.Length == 0)
                {
                    return false;
                }

                string serverHash = Convert.ToHexString(SHA256.HashData(zip));
                string localHash = null;
                try
                {
                    if (File.Exists(VersionFilePath))
                    {
                        localHash = File.ReadAllText(VersionFilePath).Trim();
                    }
                }
                catch { /* treat as unknown -> refresh */ }

                // Up to date only if the hash matches AND the schedule is actually present.
                if (string.Equals(serverHash, localHash, StringComparison.OrdinalIgnoreCase) && IsInstalled(app))
                {
                    return false;
                }

                // ⚠️ ON N'EFFACE QUE CE QUI EST A NOUS.
                //
                // Cette place effacait TOUTE la racine du seed, alors qu'elle n'est pas reservee
                // a Splatoon 2 : Splatoon 3 y depose le paquet de festival, sous eu-default. Un
                // joueur qui installait le paquet de fete puis lancait Splatoon 2 une seule fois
                // le perdait sans un message — et sur une installation neuve le fichier
                // d'empreinte est absent, donc l'effacement etait garanti au premier lancement.
                //
                // L'intention d'origine reste : un fichier que le serveur ne sert plus ne doit
                // pas trainer. On la tient en ne retirant que les dossiers de premier niveau que
                // cette archive apporte, plus ceux que la synchronisation precedente avait poses
                // et que le serveur a depuis retires. Tout le reste — les autres jeux — n'est
                // pas touche.
                Directory.CreateDirectory(SeedRoot);

                using (MemoryStream ms = new(zip))
                using (ZipArchive archive = new(ms, ZipArchiveMode.Read))
                {
                    HashSet<string> aNous = RacinesDe(archive);

                    foreach (string racine in aNous.Union(RacinesInstallees()))
                    {
                        string chemin = Path.Combine(SeedRoot, racine);
                        try
                        {
                            if (Directory.Exists(chemin))
                            {
                                Directory.Delete(chemin, recursive: true);
                            }
                            else if (File.Exists(chemin))
                            {
                                File.Delete(chemin);
                            }
                        }
                        catch (Exception ex)
                        {
                            Logger.Warning?.Print(LogClass.Application,
                                $"[Nextendo] BCAT auto-update: could not clear {racine}: {ex.Message}");
                        }
                    }

                    archive.ExtractToDirectory(SeedRoot, overwriteFiles: true);

                    try { File.WriteAllLines(DirsFilePath, aNous.OrderBy(x => x, StringComparer.Ordinal)); }
                    catch { /* non-fatal : au pire on ne retirera pas un dossier abandonne */ }
                }

                try { File.WriteAllText(VersionFilePath, serverHash); } catch { /* non-fatal */ }

                Logger.Info?.Print(LogClass.Application,
                    $"[Nextendo] BCAT auto-update: schedule refreshed ({zip.Length} B, hash {serverHash[..Math.Min(8, serverHash.Length)]})");
                return true;
            }
            catch (Exception ex)
            {
                Logger.Warning?.Print(LogClass.Application, $"[Nextendo] BCAT auto-update failed (keeping local): {ex.Message}");
                return false;
            }
        }

        // Per-title "don't ask again" list.
        private static string SkipFilePath => Path.Combine(AppDataManager.BaseDirPath, "nextendo_byaml_skip.txt");

        public static bool IsSkipped(string idString)
        {
            try
            {
                if (File.Exists(SkipFilePath))
                {
                    foreach (string line in File.ReadAllLines(SkipFilePath))
                    {
                        if (string.Equals(line.Trim(), idString, StringComparison.OrdinalIgnoreCase))
                        {
                            return true;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Warning?.Print(LogClass.Application, $"[Nextendo] byaml skip read failed: {ex.Message}");
            }

            return false;
        }

        public static void MarkSkipped(string idString)
        {
            try
            {
                File.AppendAllText(SkipFilePath, idString + Environment.NewLine);
            }
            catch (Exception ex)
            {
                Logger.Warning?.Print(LogClass.Application, $"[Nextendo] byaml skip persist failed: {ex.Message}");
            }
        }

        // Download (with a progress dialog) + extract the title's BCAT seed bundle (vsdata +
        // coopdata + fesdata) into the writable bcat-seed dir (SeedRoot). Returns true on success.
        public static async Task<bool> DownloadAndInstallAsync(ApplicationData app)
        {
            if (app == null)
            {
                return false;
            }

            if (app.IdBaseString == Ryujinx.Common.NextendoSplatoon3Bcat.TitleId)
            {
                return await EnsureSplatoon3BcatAsync(force: true);
            }

            if (IsPokemon(app))
            {
                return await EnsurePokemonBcatAsync(app, force: true);
            }

            string tmpZip = Path.Combine(Path.GetTempPath(), $"nextendo_byaml_{app.IdString}.zip");
            bool ok = false;

            TaskDialog dialog = new()
            {
                Header = "Nextendo Network — planning (BCAT)",
                SubHeader = $"Téléchargement du planning en ligne pour {app.Name}…",
                IconSource = new SymbolIconSource { Symbol = Symbol.Download },
                ShowProgressBar = true,
                XamlRoot = RyujinxApp.MainWindow,
            };

            dialog.Opened += (_, _) =>
            {
                Task.Run(async () =>
                {
                    try
                    {
                        using HttpClient http = new() { Timeout = TimeSpan.FromMinutes(5) };
                        NextendoApi.AddAppHeader(http);
                        if (!string.IsNullOrEmpty(NextendoAccount.NexToken))
                        {
                            http.DefaultRequestHeaders.Add("Authorization", "Bearer " + NextendoAccount.NexToken);
                        }

                        using HttpResponseMessage resp = await http.GetAsync(
                            $"{BaseUrl()}/api/bcat/{app.IdString}", HttpCompletionOption.ResponseHeadersRead);

                        if (!resp.IsSuccessStatusCode)
                        {
                            Logger.Warning?.Print(LogClass.Application,
                                $"[Nextendo] byaml download HTTP {(int)resp.StatusCode} for {app.IdString}");
                            return;
                        }

                        long total = resp.Content.Headers.ContentLength ?? 0;

                        using (Stream remote = await resp.Content.ReadAsStreamAsync())
                        using (FileStream fs = File.Open(tmpZip, FileMode.Create))
                        {
                            byte[] buffer = new byte[64 * 1024];
                            long written = 0;
                            int read;
                            while ((read = await remote.ReadAsync(buffer)) > 0)
                            {
                                await fs.WriteAsync(buffer.AsMemory(0, read));
                                written += read;
                                if (total > 0)
                                {
                                    double pct = written * 100.0 / total;
                                    Dispatcher.UIThread.Post(() =>
                                        dialog.SetProgressBarState(pct, TaskDialogProgressState.Normal));
                                }
                            }
                        }

                        Dispatcher.UIThread.Post(() => dialog.SubHeader = "Installation du planning…");

                        Directory.CreateDirectory(SeedRoot);
                        ZipFile.ExtractToDirectory(tmpZip, SeedRoot, overwriteFiles: true);
                        ok = true;

                        Logger.Info?.Print(LogClass.Application, $"[Nextendo] byaml schedule installed -> {SeedRoot}");
                    }
                    catch (Exception ex)
                    {
                        Logger.Warning?.Print(LogClass.Application, $"[Nextendo] byaml download failed: {ex.Message}");
                    }
                    finally
                    {
                        try { File.Delete(tmpZip); } catch { /* best effort */ }
                        Dispatcher.UIThread.Post(() => dialog.Hide());
                    }
                });
            };

            await dialog.ShowAsync(true);
            return ok;
        }

        private static async Task<bool> EnsureSplatoon3BcatAsync(bool force = false)
        {
            try
            {
                using HttpClient http = new() { Timeout = TimeSpan.FromSeconds(60) };
                NextendoApi.AddAppHeader(http);
                if (!string.IsNullOrEmpty(NextendoAccount.NexToken))
                {
                    http.DefaultRequestHeaders.Add("Authorization", "Bearer " + NextendoAccount.NexToken);
                }
                bool changed = await Ryujinx.Common.NextendoSplatoon3Bcat.SyncAsync(http, BaseUrl(), SeedRoot, force);
                Logger.Info?.Print(LogClass.Application, changed
                    ? "[Nextendo] Splatoon 3 BCAT updated from server."
                    : "[Nextendo] Splatoon 3 BCAT is current.");
                return changed || (force && IsInstalledForSplatoon3());
            }
            catch (Exception ex)
            {
                Logger.Warning?.Print(LogClass.Application,
                    $"[Nextendo] Splatoon 3 BCAT update failed; retaining local cache: {ex.Message}");
                return false;
            }
        }

        private static bool IsInstalledForSplatoon3() => Ryujinx.Common.NextendoSplatoon3Bcat.IsInstalled(
            Path.Combine(SeedRoot, Ryujinx.Common.NextendoSplatoon3Bcat.TitleId));

        private static async Task<HttpResponseMessage> GetPokemonBcatResponseAsync(HttpClient http, string titleId)
        {
            HttpResponseMessage response = await http.GetAsync($"{BaseUrl()}/api/bcat/{titleId}", HttpCompletionOption.ResponseHeadersRead);
            if (titleId == ScarletTitleId &&
                (response.StatusCode is System.Net.HttpStatusCode.NoContent or System.Net.HttpStatusCode.NotFound))
            {
                // The current Meowscarada event is shared by both games, but the account API
                // serves its ZIP under Violet's title ID. Keep Scarlet's local cache separate.
                response.Dispose();
                Logger.Info?.Print(LogClass.Application, "[Nextendo] Scarlet BCAT has no separate package; using the shared Violet event.");
                return await http.GetAsync($"{BaseUrl()}/api/bcat/{VioletTitleId}", HttpCompletionOption.ResponseHeadersRead);
            }
            return response;
        }

        private static async Task<bool> EnsurePokemonBcatAsync(ApplicationData app, bool force = false)
        {
            string titleId = app.IdBaseString;
            string titleRoot = PokemonSeedRoot(app);
            string versionPath = Path.Combine(AppDataManager.BaseDirPath, $"nextendo_bcat_version_{titleId}.txt");
            try
            {
                using HttpClient http = new() { Timeout = TimeSpan.FromSeconds(15) };
                NextendoApi.AddAppHeader(http);
                if (!string.IsNullOrEmpty(NextendoAccount.NexToken))
                {
                    http.DefaultRequestHeaders.Add("Authorization", "Bearer " + NextendoAccount.NexToken);
                }
                using HttpResponseMessage response = await GetPokemonBcatResponseAsync(http, titleId);
                // An event is optional: an empty event store must not prevent launching either game.
                if (response.StatusCode is System.Net.HttpStatusCode.NotFound or System.Net.HttpStatusCode.NoContent) return false;
                response.EnsureSuccessStatusCode();
                using Stream remote = await response.Content.ReadAsStreamAsync();
                using MemoryStream bytes = new();
                byte[] buffer = new byte[65536];
                int read;
                while ((read = await remote.ReadAsync(buffer)) != 0)
                {
                    if (bytes.Length + read > 16 * 1024 * 1024) throw new InvalidDataException("Pokémon BCAT package exceeds 16 MiB.");
                    bytes.Write(buffer, 0, read);
                }
                byte[] zip = bytes.ToArray();
                string hash = Convert.ToHexString(SHA256.HashData(zip));
                if (!force && File.Exists(versionPath) && File.ReadAllText(versionPath).Trim() == hash &&
                    Directory.Exists(titleRoot) && Directory.EnumerateFiles(titleRoot, "*", SearchOption.AllDirectories).Any()) return false;
                Ryujinx.Common.NextendoPokemonBcat.Install(zip, titleRoot);
                File.WriteAllText(versionPath, hash);
                Logger.Info?.Print(LogClass.Application, $"[Nextendo] Pokémon BCAT {titleId} updated ({zip.Length} B, hash {hash[..8]}).");
                return true;
            }
            catch (Exception ex)
            {
                Logger.Warning?.Print(LogClass.Application, $"[Nextendo] Pokémon BCAT {titleId} update failed; using local event: {ex.Message}");
                return false;
            }
        }
    }
}
