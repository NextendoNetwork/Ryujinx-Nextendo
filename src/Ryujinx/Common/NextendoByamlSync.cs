using Avalonia.Threading;
using FluentAvalonia.UI.Controls;
using Ryujinx.Ava.Systems.AppLibrary;
using Ryujinx.Common.Configuration;
using Ryujinx.Common.Logging;
using System;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
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

        // Same root the BcatSeed override serves from: <exe dir>/bcat-seed.
        private static string SeedRoot => Path.Combine(AppContext.BaseDirectory, "bcat-seed");

        // The marker that tells us the schedule is already installed for this title.
        // One source of truth: ApplicationData.RequiresNextendoByaml (Splatoon 2 only).
        public static bool RequiresByaml(ApplicationData app) => app != null && app.RequiresNextendoByaml;

        public static bool IsInstalled(ApplicationData app)
        {
            if (app == null)
            {
                return false;
            }

            // vsdata/VSSetting_0.byaml is the load-bearing schedule file.
            return File.Exists(Path.Combine(SeedRoot, "vsdata", "VSSetting_0.byaml"));
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
        // coopdata + fesdata) into <exe dir>/bcat-seed/. Returns true on success.
        public static async Task<bool> DownloadAndInstallAsync(ApplicationData app)
        {
            if (app == null)
            {
                return false;
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
    }
}
