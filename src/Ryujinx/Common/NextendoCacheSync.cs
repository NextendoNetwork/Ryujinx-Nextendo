using Avalonia.Threading;
using FluentAvalonia.UI.Controls;
using Ryujinx.Ava.Systems.AppLibrary;
using Ryujinx.Common.Configuration;
using Ryujinx.Common.Logging;
using System;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace Ryujinx.Ava.Common
{
    /// <summary>
    /// [Nextendo] Shared shader-cache cloud. On launch of a Nextendo-compatible title — only
    /// when the user runs the right version AND is linked to a Nextendo account — we offer to
    /// download the community shader cache so the game stutters far less the first time.
    /// Only the portable "guest" shader files are shared; the GPU-specific host shaders are
    /// rebuilt locally. PPTC is intentionally NOT synced (too large, only a launch-time gain).
    /// </summary>
    public static class NextendoCacheSync
    {
        private static string BaseUrl()
        {
            // [Nextendo] Une seule decision, dans NextendoEndpoint : c'est elle qui choisit qui
            // recoit le jeton du compte. Cette logique etait dupliquee ici et acceptait
            // n'importe quelle valeur de NEXTENDO_API.
            return NextendoEndpoint.BaseUrl();
        }

        // Per-title "don't ask again" list, persisted next to the other Nextendo state.
        private static string SkipFilePath => Path.Combine(AppDataManager.BaseDirPath, "nextendo_cache_skip.txt");

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
                Logger.Warning?.Print(LogClass.Application, $"[Nextendo] cache skip read failed: {ex.Message}");
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
                Logger.Warning?.Print(LogClass.Application, $"[Nextendo] cache skip persist failed: {ex.Message}");
            }
        }

        // The local folder the shader cache installs into (shown to the user before download).
        public static string ShaderInstallPath(ApplicationData app)
            => Path.Combine(AppDataManager.GamesDirPath, app.IdString, "cache", "shader");

        // Query the server for the shared shader archive size (bytes). 0 = none available.
        public static async Task<long> ShaderAvailableAsync(ApplicationData app)
        {
            if (app == null || string.IsNullOrEmpty(NextendoAccount.NexToken))
            {
                return 0;
            }

            try
            {
                using HttpClient http = new() { Timeout = TimeSpan.FromSeconds(15) };
                http.DefaultRequestHeaders.Add("Authorization", "Bearer " + NextendoAccount.NexToken);

                HttpResponseMessage resp = await http.GetAsync($"{BaseUrl()}/api/cache/{app.IdString}/manifest");
                if (!resp.IsSuccessStatusCode)
                {
                    return 0; // 204 = nothing seeded for this title
                }

                string json = await resp.Content.ReadAsStringAsync();
                using JsonDocument doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("shader", out JsonElement shaderEl) &&
                    shaderEl.TryGetProperty("size", out JsonElement sizeEl) &&
                    sizeEl.TryGetInt64(out long size))
                {
                    return size;
                }
            }
            catch (Exception ex)
            {
                Logger.Warning?.Print(LogClass.Application, $"[Nextendo] shader manifest check failed: {ex.Message}");
            }

            return 0;
        }

        // Download (with a progress dialog) + extract the shared shader cache into the title's
        // local cache folder. Returns the install path on success, null on failure.
        public static async Task<string> DownloadAndInstallShaderAsync(ApplicationData app)
        {
            string cacheRoot = Path.Combine(AppDataManager.GamesDirPath, app.IdString, "cache");
            string installPath = Path.Combine(cacheRoot, "shader");
            string tmpZip = Path.Combine(Path.GetTempPath(), $"nextendo_shader_{app.IdString}.zip");

            TaskDialog dialog = new()
            {
                Header = "Nextendo Network — cache de shaders",
                SubHeader = $"Téléchargement du cache de shaders pour {app.Name}…",
                IconSource = new SymbolIconSource { Symbol = Symbol.Download },
                ShowProgressBar = true,
                XamlRoot = RyujinxApp.MainWindow,
            };

            bool ok = false;

            dialog.Opened += (_, _) =>
            {
                // Heavy work off the UI thread; marshal progress + close back onto it.
                Task.Run(async () =>
                {
                    try
                    {
                        using HttpClient http = new() { Timeout = TimeSpan.FromMinutes(10) };
                        http.DefaultRequestHeaders.Add("Authorization", "Bearer " + NextendoAccount.NexToken);

                        using HttpResponseMessage resp = await http.GetAsync(
                            $"{BaseUrl()}/api/cache/{app.IdString}/shader", HttpCompletionOption.ResponseHeadersRead);

                        if (!resp.IsSuccessStatusCode)
                        {
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

                        Dispatcher.UIThread.Post(() => dialog.SubHeader = "Installation du cache…");

                        Directory.CreateDirectory(cacheRoot);
                        ZipFile.ExtractToDirectory(tmpZip, cacheRoot, overwriteFiles: true);
                        ok = true;

                        Logger.Info?.Print(LogClass.Application, $"[Nextendo] shader cache installed -> {installPath}");
                    }
                    catch (Exception ex)
                    {
                        Logger.Warning?.Print(LogClass.Application, $"[Nextendo] shader cache download failed: {ex.Message}");
                    }
                    finally
                    {
                        try { File.Delete(tmpZip); } catch { /* best effort */ }
                        Dispatcher.UIThread.Post(() => dialog.Hide());
                    }
                });
            };

            await dialog.ShowAsync(true);

            return ok ? installPath : null;
        }
    }
}
