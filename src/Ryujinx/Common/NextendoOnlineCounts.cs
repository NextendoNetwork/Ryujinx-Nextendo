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
    /// [Nextendo] Live "N players online" per supported game, shown next to each title in the
    /// game list.
    ///
    /// The account server aggregates the real counts from the game servers themselves, so this
    /// only has to poll one endpoint and hand the numbers to the matching ApplicationData. It is
    /// deliberately best-effort: a failed poll leaves the previous numbers in place rather than
    /// flashing every game to zero, and never blocks or logs noisily — a player who can't reach
    /// us has bigger problems than a stale counter.
    /// </summary>
    public static class NextendoOnlineCounts
    {
        private static readonly TimeSpan _pollInterval = TimeSpan.FromSeconds(30);
        private static readonly object _lock = new();
        private static Dictionary<string, int> _counts = new();
        private static Timer _timer;
        private static ApplicationLibrary _library;

        /// <summary>Players currently online for a title id, or 0 when unknown.</summary>
        public static int For(string titleIdString)
        {
            if (string.IsNullOrEmpty(titleIdString))
            {
                return 0;
            }

            lock (_lock)
            {
                return _counts.TryGetValue(titleIdString.ToLowerInvariant(), out int n) ? n : 0;
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

        // Push the fresh numbers into the loaded games so the list updates itself.
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
                    if (app.IsNextendoCompatible)
                    {
                        app.NextendoPlayersOnline = For(app.IdString);
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Debug?.Print(LogClass.Application, $"[Nextendo] online-count publish failed: {ex.Message}");
            }
        }
    }
}
