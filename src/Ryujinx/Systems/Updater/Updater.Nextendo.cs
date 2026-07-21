using Ryujinx.Ava.Common.Locale;
using Ryujinx.Ava.UI.Helpers;
using Ryujinx.Common;
using Ryujinx.Common.Logging;
using System;
using System.Linq;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading.Tasks;
using Avalonia.Threading;

namespace Ryujinx.Ava.Systems
{
    /// <summary>
    /// [Nextendo] Where an update comes from.
    ///
    /// Everything that makes an update actually happen — the chunked download, the extraction, the
    /// in-place replacement of the running install and the restart — already exists in Updater.cs
    /// and is reused as-is. Only the source changes: Ryujinx-Nextendo releases live on the
    /// NextendoNetwork GitHub, not on the Ryubing update server, so this part answers "is there a
    /// newer build, and what is its download url".
    /// </summary>
    internal static partial class Updater
    {
        // The release LIST, deliberately not /releases/latest: that endpoint silently skips
        // prereleases, and Nextendo ships builds marked EXPERIMENTAL. Using it would mean the
        // updater reports "up to date" forever while a newer build sits on the release page.
        private const string NextendoReleasesApi =
            "https://api.github.com/repos/NextendoNetwork/Ryujinx-Nextendo/releases?per_page=10";

        private const string NextendoReleasesPage =
            "https://github.com/NextendoNetwork/Ryujinx-Nextendo/releases";

        // Mirrors AppDataManager.DefaultPortableDir, which is private and in another assembly.
        // If that ever changes, this has to follow, or updates start writing over player data.
        private const string NextendoPortableDirName = "portable";

        internal enum ArchiveKind
        {
            Unknown,
            Zip,
            SevenZip,
            TarGz,
            TarXz,
        }

        private static ArchiveKind _archiveKind;

        // Hosts allowed to serve an update. The updater replaces the running install with whatever
        // it downloads, so this is the trust boundary of the whole feature: everything downstream
        // assumes the bytes came from here.
        private static readonly string[] _allowedAssetHosts =
        [
            "github.com",
            "objects.githubusercontent.com",
            "release-assets.githubusercontent.com",
        ];

        /// <summary>
        /// True when the url is an HTTPS url served by GitHub for our own repository.
        ///
        /// Parsed as a Uri and matched on the HOST, never as a substring of the whole url: a naive
        /// Contains("NextendoNetwork/") also accepts https://evil.com/NextendoNetwork/x.zip and
        /// https://github.com.attacker.net/NextendoNetwork/x.zip.
        /// </summary>
        private static bool NextendoIsOwnReleaseUrl(string url)
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out Uri uri))
            {
                return false;
            }

            // https only: an http asset url would be a silent downgrade on the one download whose
            // authenticity actually matters.
            if (uri.Scheme != Uri.UriSchemeHttps)
            {
                return false;
            }

            if (!_allowedAssetHosts.Any(h => uri.Host.Equals(h, StringComparison.OrdinalIgnoreCase)))
            {
                return false;
            }

            // github.com must additionally be OUR repository; the asset CDN hosts serve opaque
            // paths, so the host itself is all that can be checked there.
            return !uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase)
                || uri.AbsolutePath.StartsWith("/NextendoNetwork/Ryujinx-Nextendo/", StringComparison.OrdinalIgnoreCase);
        }

        private static ArchiveKind NextendoArchiveKindFromUrl(string url)
        {
            if (url is null)
            {
                return ArchiveKind.Unknown;
            }

            // .tar.gz / .tar.xz first: a plain extension check would read both as their last suffix.
            if (url.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase))
            {
                return ArchiveKind.TarGz;
            }

            if (url.EndsWith(".tar.xz", StringComparison.OrdinalIgnoreCase))
            {
                return ArchiveKind.TarXz;
            }

            if (url.EndsWith(".7z", StringComparison.OrdinalIgnoreCase))
            {
                return ArchiveKind.SevenZip;
            }

            return url.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)
                ? ArchiveKind.Zip
                : ArchiveKind.Unknown;
        }

        /// <summary>
        /// Checks the Nextendo release page and, if a newer build exists, offers to install it over
        /// the current one. Replaces the old flow, which sent the player to a browser and exited.
        /// </summary>
        public static async Task BeginNextendoUpdateAsync(bool showVersionUpToDate = false)
        {
            if (_running)
            {
                return;
            }

            _running = true;

            try
            {
                if (!Version.TryParse(Program.Version, out Version currentVersion))
                {
                    Logger.Warning?.Print(LogClass.Application,
                        $"[Nextendo] Cannot parse the running version ('{Program.Version}'); skipping update check.");

                    return;
                }

                (Version newVersion, string assetUrl) = await QueryNextendoReleaseAsync();

                if (newVersion is null)
                {
                    if (showVersionUpToDate)
                    {
                        await ContentDialogHelper.CreateWarningDialog(
                            LocaleManager.Instance[LocaleKeys.Dialog_Nextendo_UpdateCheckFailed],
                            LocaleManager.Instance[LocaleKeys.Dialog_Nextendo_UpdateCheckFailedSub]);
                    }

                    return;
                }

                if (newVersion <= currentVersion)
                {
                    if (showVersionUpToDate)
                    {
                        await ContentDialogHelper.CreateUpdaterUpToDateInfoDialog(
                            LocaleManager.Instance[LocaleKeys.DialogUpdaterAlreadyOnLatestVersionMessage],
                            string.Empty,
                            NextendoReleasesPage);
                    }

                    Logger.Info?.Print(LogClass.Application, "[Nextendo] Up to date.");

                    return;
                }

                // No asset for this platform: say so plainly and point at the page, rather than
                // pretending there is nothing new.
                if (string.IsNullOrEmpty(assetUrl))
                {
                    Logger.Warning?.Print(LogClass.Application,
                        $"[Nextendo] Release {newVersion} has no build for this platform.");

                    await ContentDialogHelper.CreateWarningDialog(
                        LocaleManager.Instance[LocaleKeys.Dialog_Nextendo_UpdateNoAsset],
                        NextendoReleasesPage);

                    return;
                }

                // The url arrives inside a JSON response. TLS to api.github.com authenticates that
                // response, but the updater is the one component that runs what it downloads, so it
                // does not take the url on faith: it must be an https GitHub url for our own repo.
                // Without this, anything that could influence the response — a compromised token, a
                // proxy, a poisoned cache — would choose which binary replaces the player's install.
                if (!NextendoIsOwnReleaseUrl(assetUrl))
                {
                    Logger.Error?.Print(LogClass.Application,
                        $"[Nextendo] Refusing update: asset url is not served by GitHub for this repo ({assetUrl}).");

                    await ContentDialogHelper.CreateWarningDialog(
                        LocaleManager.Instance[LocaleKeys.Dialog_Nextendo_UpdateUntrustedUrl],
                        NextendoReleasesPage);

                    return;
                }

                bool accepted = false;

                await Dispatcher.UIThread.InvokeAsync(async () =>
                {
                    UserResult choice = await ContentDialogHelper.CreateUpdaterChoiceDialog(
                        LocaleManager.Instance[LocaleKeys.RyujinxUpdater],
                        LocaleManager.Instance[LocaleKeys.RyujinxUpdaterMessage],
                        $"{currentVersion} → {newVersion}",
                        NextendoReleasesPage);

                    accepted = choice == UserResult.Yes;
                });

                if (!accepted)
                {
                    return;
                }

                // Hands over to the upstream machinery: download, extract, replace in place,
                // restart. _running stays true across it — UpdateRyujinx owns it from here.
                await UpdateRyujinx(assetUrl);

                return;
            }
            catch (Exception ex)
            {
                Logger.Error?.Print(LogClass.Application, $"[Nextendo] Update check failed: {ex.Message}");
            }
            finally
            {
                // UpdateRyujinx clears _running itself once it has taken over; every path that
                // returns before that has to release it or the updater never runs again.
                if (!_updateSuccessful)
                {
                    _running = false;
                }
            }
        }

        /// <summary>
        /// Silent check for the "update available" indicator: no dialogs, no download. Returns
        /// false on any failure, because a background check is not worth bothering anyone about.
        /// </summary>
        public static async Task<bool> NextendoUpdateAvailableAsync()
        {
            try
            {
                if (!Version.TryParse(Program.Version, out Version currentVersion))
                {
                    return false;
                }

                (Version newVersion, string assetUrl) = await QueryNextendoReleaseAsync();

                // Only flag an update the player could actually install from here.
                return newVersion is not null
                    && newVersion > currentVersion
                    && !string.IsNullOrEmpty(assetUrl);
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Reads the latest release off GitHub. Returns (null, null) when unreachable — an update
        /// check that cannot reach the network is not an error worth interrupting anyone over.
        /// </summary>
        private static async Task<(Version Version, string AssetUrl)> QueryNextendoReleaseAsync()
        {
            using HttpClient http = ConstructHttpClient();
            http.Timeout = TimeSpan.FromSeconds(10);

            // GitHub rejects requests without a User-Agent. ConstructHttpClient already sets one.
            http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");

            HttpResponseMessage response = await http.GetAsync(NextendoReleasesApi);
            if (!response.IsSuccessStatusCode)
            {
                Logger.Warning?.Print(LogClass.Application,
                    $"[Nextendo] Release check returned {(int)response.StatusCode}.");

                return (null, null);
            }

            using JsonDocument doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            JsonElement root = doc.RootElement;

            if (root.ValueKind != JsonValueKind.Array)
            {
                return (null, null);
            }

            Version best = null;
            string bestAsset = null;

            foreach (JsonElement release in root.EnumerateArray())
            {
                // Drafts are not published yet — they are visible to us only because the token
                // might have write access, and pushing one to players would be a mistake.
                if (release.TryGetProperty("draft", out JsonElement draft) && draft.GetBoolean())
                {
                    continue;
                }

                if (!release.TryGetProperty("tag_name", out JsonElement tagElement))
                {
                    continue;
                }

                // Tags read like "v1.4.4" or "1.4.4"; Version.TryParse wants the bare number.
                string tag = (tagElement.GetString() ?? "").TrimStart('v', 'V');

                if (!Version.TryParse(tag, out Version version))
                {
                    Logger.Debug?.Print(LogClass.Application, $"[Nextendo] Skipping unparsable release tag '{tag}'.");

                    continue;
                }

                // GitHub returns newest-first, but that is by publish date — a hotfix to an older
                // branch published later would win. Compare versions and let the highest one lead.
                if (best is not null && version <= best)
                {
                    continue;
                }

                best = version;
                bestAsset = FindPlatformAsset(release);
            }

            return (best, bestAsset);
        }

        /// <summary>Picks the asset built for the platform AND architecture we are running on.</summary>
        private static string FindPlatformAsset(JsonElement release)
        {
            if (!release.TryGetProperty("assets", out JsonElement assets)
                || assets.ValueKind != JsonValueKind.Array)
            {
                return null;
            }

            string platform = OperatingSystem.IsWindows() ? "win" : OperatingSystem.IsLinux() ? "linux" : "macos";

            // Architecture matters as much as the OS. Matching the platform alone picks whichever
            // build happens to be listed first — on a release carrying both x64 and arm64 that is
            // a coin flip, and the loser gets an install that cannot start.
            string[] arches = RuntimeInformation.OSArchitecture switch
            {
                Architecture.Arm64 => ["arm64", "aarch64"],
                Architecture.X64 => ["x64", "amd64", "x86_64"],
                _ => [],
            };

            if (arches.Length == 0)
            {
                Logger.Warning?.Print(LogClass.Application,
                    $"[Nextendo] No known asset naming for {RuntimeInformation.OSArchitecture}.");

                return null;
            }

            // Match the platform tag, the architecture, AND an archive we can actually extract, so
            // checksums, installers and symbol bundles in the same release cannot be picked.
            return assets.EnumerateArray()
                .Select(a => new
                {
                    Name = a.TryGetProperty("name", out JsonElement n) ? n.GetString() ?? "" : "",
                    Url = a.TryGetProperty("browser_download_url", out JsonElement u) ? u.GetString() : null,
                })
                .Where(a => a.Url is not null)
                .Where(a => a.Name.Contains(platform, StringComparison.OrdinalIgnoreCase))
                .Where(a => arches.Any(arch => a.Name.Contains(arch, StringComparison.OrdinalIgnoreCase)))
                .Where(a => NextendoArchiveKindFromUrl(a.Name) != ArchiveKind.Unknown)
                .Select(a => a.Url)
                .FirstOrDefault();
        }
    }
}
