using Ryujinx.Ava.Systems.AppLibrary;
using Ryujinx.Common.Logging;
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Ryujinx.Ava.Common
{
    /// <summary>
    /// [Nextendo] Live "N players online" per game server listed by Nextendo, shown next to each
    /// title in the game list. Server listing is tracked separately from version-verified support.
    ///
    /// The account server aggregates the real counts from the game servers themselves, so this
    /// only has to poll one endpoint and hand the numbers to the matching ApplicationData. It is
    /// deliberately best-effort: a failed poll leaves the previous numbers in place rather than
    /// flashing every game to zero, and never blocks or logs noisily — a player who can't reach
    /// us has bigger problems than a stale counter.
    /// </summary>
    public static class NextendoOnlineCounts
    {
        private static readonly TimeSpan _pollInterval = TimeSpan.FromSeconds(5);
        private static readonly object _lock = new();
        private static Dictionary<string, int> _counts = new();
        private static Timer _timer;
        private static ApplicationLibrary _library;

        /// <summary>Players currently online for a title id, or 0 when unknown.</summary>
        public static int For(string titleIdString)
        {
            return TryGetCount(titleIdString, out int count) ? count : 0;
        }

        private static bool TryGetCount(string titleIdString, out int count)
        {
            count = 0;
            if (string.IsNullOrEmpty(titleIdString))
            {
                return false;
            }

            lock (_lock)
            {
                return _counts.TryGetValue(titleIdString.ToLowerInvariant(), out count);
            }
        }

        /// <summary>
        /// Starts polling and pushing counts into the library's applications. Safe to call twice.
        /// </summary>
        public static void Start(ApplicationLibrary library)
        {
            if (_timer != null)
            {
                return;
            }

            _library = library;
            _timer = new Timer(_ => _ = RefreshAsync(), null, TimeSpan.FromSeconds(2), _pollInterval);
        }

        public static async Task RefreshAsync()
        {
            try
            {
                using HttpClient http = new() { Timeout = TimeSpan.FromSeconds(8) };
                NextendoApi.AddAppHeader(http);
                HttpResponseMessage resp = await http.GetAsync($"{NextendoApi.BaseUrl()}/api/online-counts");
                if (!resp.IsSuccessStatusCode)
                {
                    return;
                }

                using JsonDocument doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
                if (!doc.RootElement.TryGetProperty("counts", out JsonElement counts)
                    || counts.ValueKind != JsonValueKind.Object)
                {
                    return;
                }

                Dictionary<string, int> parsed = new();
                foreach (JsonProperty p in counts.EnumerateObject())
                {
                    if (p.Value.TryGetInt32(out int n))
                    {
                        parsed[p.Name.ToLowerInvariant()] = n;
                    }
                }

                // SM3DW currently exposes its public aggregate through the site's dashboard
                // bridge. Prefer the common endpoint once it includes this title.
                const string sm3dwTitleId = "010028600ebda000";
                if (!parsed.ContainsKey(sm3dwTitleId))
                {
                    if (TryGetCount(sm3dwTitleId, out int previousCount))
                    {
                        parsed[sm3dwTitleId] = previousCount;
                    }

                    try
                    {
                        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(2));
                        using HttpResponseMessage sm3dw = await http.GetAsync(
                            $"{NextendoApi.BaseUrl()}/game-stats/sm3dw", timeout.Token);
                        if (sm3dw.IsSuccessStatusCode)
                        {
                            using JsonDocument stats = JsonDocument.Parse(
                                await sm3dw.Content.ReadAsStringAsync(timeout.Token));
                            if (stats.RootElement.TryGetProperty("online", out JsonElement online)
                                && online.ValueKind == JsonValueKind.True)
                            {
                                if (stats.RootElement.TryGetProperty("connected", out JsonElement connected)
                                    && connected.TryGetInt32(out int count) && count >= 0)
                                {
                                    parsed[sm3dwTitleId] = count;
                                }
                            }
                            else if (online.ValueKind == JsonValueKind.False)
                            {
                                parsed.Remove(sm3dwTitleId);
                            }
                        }
                    }
                    catch
                    {
                        // Keep SM3DW's last count without interrupting updates for other games.
                    }
                }

                lock (_lock)
                {
                    _counts = parsed;
                }

                Publish();
            }
            catch
            {
                // Offline or server down: keep the last known numbers rather than zeroing the UI.
            }
        }

        // Push fresh counts and server-list availability so the library updates without a rescan.
        private static void Publish()
        {
            ApplicationLibrary lib = _library;
            if (lib == null)
            {
                return;
            }

            try
            {
                foreach (ApplicationData app in lib.Applications.Items)
                {
                    bool serverAvailable = TryGetCount(app.IdString, out int count);
                    app.IsNextendoServerAvailable = serverAvailable;
                    app.NextendoPlayersOnline = serverAvailable ? count : 0;
                }
            }
            catch (Exception ex)
            {
                Logger.Debug?.Print(LogClass.Application, $"[Nextendo] online-count publish failed: {ex.Message}");
            }
        }
    }
}
