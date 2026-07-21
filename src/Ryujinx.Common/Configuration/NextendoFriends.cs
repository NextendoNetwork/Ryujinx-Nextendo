using Gommon;
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;

namespace Ryujinx.Common.Configuration
{
    /// <summary>
    /// [Nextendo] Cached friend list for the in-game Switch <c>friend:</c> service. The HLE
    /// friends sysmodule (Ryujinx.Horizon) reads this so Mario Kart 8's "Amis/Adversaires"
    /// shows the player's Nextendo friends. Fetched (synchronously, cached ~10 s) from the
    /// account service using the persisted NEX token, so it works across the assembly
    /// boundary without referencing the UI project.
    /// </summary>
    public static class NextendoFriends
    {
        public readonly struct Entry
        {
            public readonly ulong Pid;
            public readonly string Name;
            // The friend's live S2 presence "AppField" (nn::friends AppKeyValueStorage, up to
            // 0xC0 bytes) relayed by the account server — encodes SessionId/Full/etc. so S2 sees
            // the friend as in a JOINABLE private battle. Empty when the friend isn't in a room.
            public readonly byte[] AppField;

            public Entry(ulong pid, string name, byte[] appField)
            {
                Pid = pid;
                Name = name;
                AppField = appField;
            }
        }

        private static readonly object _lock = new();
        private static List<Entry> _cache = new();
        private static long _lastFetchMs;
        private static int _refreshing;

        public static IReadOnlyList<Entry> Get()
        {
            // NEVER block the caller. The in-game friend service runs on the game's online
            // thread; a synchronous HTTP fetch here stalls the game for up to several seconds,
            // so it stops acking NEX/PRUDP -> the server's retransmit times out -> "erreur de
            // communication" (connection killed). Return the cached list IMMEDIATELY and refresh
            // in the BACKGROUND when stale. The list populates within ~1s and the game polls
            // repeatedly, so it fills in without ever blocking.
            if (NextendoAccount.IsLinked
                && (_lastFetchMs == 0 || Environment.TickCount64 - _lastFetchMs > 10_000)
                && System.Threading.Interlocked.Exchange(ref _refreshing, 1) == 0)
            {
                System.Threading.Tasks.Task.Run(() =>
                {
                    try { Refresh(); }
                    finally { System.Threading.Interlocked.Exchange(ref _refreshing, 0); }
                });
            }

            lock (_lock)
            {
                return _cache;
            }
        }

        public static void Invalidate()
        {
            lock (_lock)
            {
                _cache = new List<Entry>();
                _lastFetchMs = 0;
            }
        }

        private static void Refresh()
        {
            try
            {
                if (string.IsNullOrEmpty(NextendoAccount.NexToken))
                {
                    return;
                }

                // [Nextendo] Une seule decision, dans NextendoEndpoint : elle choisit qui recoit le
                // jeton du compte pose juste en dessous.
                string baseUrl = NextendoEndpoint.BaseUrl();

                using HttpRequestMessage req = new(HttpMethod.Get, baseUrl.TrimEnd('/') + "/api/friends");
                req.Headers.Add("Authorization", "Bearer " + NextendoAccount.NexToken);
                using HttpResponseMessage resp = _http.SendAsync(req).GetAwaiter().GetResult();
                string json = resp.Content.ReadAsStringAsync().GetAwaiter().GetResult();

                List<Entry> list = new();
                using JsonDocument doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("friends", out JsonElement arr) && arr.ValueKind == JsonValueKind.Array)
                {
                    foreach (JsonElement f in arr.EnumerateArray())
                    {
                        ulong pid = f.TryGetProperty("pid", out JsonElement p) ? p.GetUInt64() : 0;
                        string name = f.TryGetProperty("name", out JsonElement n) ? (n.GetString() ?? "") : "";
                        byte[] appField = null;
                        if (f.TryGetProperty("presence", out JsonElement pres) && pres.ValueKind == JsonValueKind.Object
                            && pres.TryGetProperty("app_field", out JsonElement af) && af.ValueKind == JsonValueKind.String)
                        {
                            string b64 = af.GetString();
                            if (!string.IsNullOrEmpty(b64))
                            {
                                try { appField = Convert.FromBase64String(b64); } catch { appField = null; }
                            }
                        }
                        if (pid != 0)
                        {
                            list.Add(new Entry(pid, name, appField));
                        }
                    }
                }

                lock (_lock)
                {
                    _cache = list;
                    _lastFetchMs = Environment.TickCount64;
                }
            }
            catch
            {
                // keep the stale cache on any failure
            }
        }

        private static string _lastKey;
        private static long _lastPublishMs;
        private static int _publishing;

        // ONE shared HttpClient for all background Nextendo calls. Creating a new HttpClient per
        // call leaks sockets (TIME_WAIT) and, combined with unbounded Task.Run, starves the thread
        // pool — which dropped the game's FPS. Shared client + single-flight + hard rate-limit fix it.
        private static readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(8) };

        // [Nextendo] État de présence UNIFIÉ : le status nn::friends + le blob de join S2 (appField)
        // + LE TITRE DU JEU COURANT (CurrentTitleId, posé par NextendoPresenceModule au lancement de
        // n'importe quel jeu). Ainsi les amis voient « joue à <jeu> » pour TOUT jeu (pas que S2), et
        // le blob de join S2 reste transporté quand on est en salon privé.
        public static volatile string CurrentTitleId = "";

        // [Nextendo] Détail du jeu courant venant de son PLAY REPORT (« Single Player », « Racing »…),
        // posé par NextendoRichPresence. N'existe QUE pour les jeux ayant une spec PlayReports (~13 :
        // MK8DX, SSBU, Odyssey, Zelda…) — chaque jeu émet un format propre, reversé titre par titre.
        // Vide pour tous les autres : les amis voient alors le nom du jeu seul, jamais un mode inventé.
        public static volatile string CurrentAppDetail = "";
        private static int _curStatus = 1;
        private static byte[] _curAppField = System.Array.Empty<byte>();

        /// <summary>[Nextendo] Chemin S2 (nn::friends UpdateUserPresence) : status + blob de join.</summary>
        public static void PublishPresence(int status, byte[] appField)
        {
            _curStatus = status;
            _curAppField = appField ?? System.Array.Empty<byte>();
            DoPublish();
        }

        /// <summary>[Nextendo] Chemin « jeu courant » (lancement/sortie/périodique) : le status découle
        /// du titre courant (en jeu = OnlinePlay, sinon Online au repos).</summary>
        public static void PublishGame()
        {
            _curStatus = string.IsNullOrEmpty(CurrentTitleId) ? 1 : 2;
            DoPublish();
        }

        private static System.Threading.Timer _presenceTimer;

        /// <summary>[Nextendo] Démarre la remontée de présence du JEU COURANT (n'importe quel jeu, pas
        /// que S2) : s'abonne au changement d'application (TitleIDs.CurrentApplication) pour poser le
        /// titre courant + publier, et rafraîchit périodiquement le TTL serveur (90s). Appelé une fois
        /// au démarrage de l'émulateur.</summary>
        public static void InitializePresence()
        {
            TitleIDs.CurrentApplication.Event += (_, e) =>
            {
                CurrentTitleId = e.NewValue.TryGet(out string tid) ? (tid ?? "") : "";

                // Le détail appartient au jeu qu'on quitte : sans ça, « Single Player » de Mario Kart
                // resterait collé au titre suivant jusqu'à ce qu'il émette son propre play report.
                CurrentAppDetail = "";

                PublishGame();
            };
            _presenceTimer = new System.Threading.Timer(_ => PublishGame(), null, 45000, 45000);
        }

        // Poste l'état de présence courant. HARD rate-limit (>=3s), SINGLE-FLIGHT, dédup 30s sur
        // (status|jeu|blob) — pour rafraîchir le TTL serveur (90s) tout en évitant le spam.
        private static void DoPublish()
        {
            if (!NextendoAccount.IsLinked || string.IsNullOrEmpty(NextendoAccount.NexToken))
            {
                return;
            }

            byte[] appField = _curAppField ?? System.Array.Empty<byte>();
            string appId = CurrentTitleId ?? "";
            string appDetail = CurrentAppDetail ?? "";
            int status = _curStatus;
            string appFieldB64 = Convert.ToBase64String(appField);

            // appDetail belongs in the dedup key: switching from "Single Player" to "Racing" keeps
            // the same status and title, and without it the change would be suppressed for 30s —
            // exactly the moment the detail is worth showing.
            string key = status + "|" + appId + "|" + appFieldB64 + "|" + appDetail;

            lock (_lock)
            {
                if (_lastPublishMs != 0 && Environment.TickCount64 - _lastPublishMs < 3000)
                {
                    return;
                }
                if (key == _lastKey && _lastPublishMs != 0 && Environment.TickCount64 - _lastPublishMs < 30000)
                {
                    return;
                }
                _lastKey = key;
                _lastPublishMs = Environment.TickCount64;
            }

            if (System.Threading.Interlocked.Exchange(ref _publishing, 1) == 1)
            {
                return;
            }

            string token = NextendoAccount.NexToken;

            // Serialized, not concatenated. app_field (base64) and app_id (hex) could never break
            // the JSON, but app_detail is free text straight out of a game's play report — a single
            // quote or backslash in it would corrupt the body. JsonSerializer escapes each value.
            string body = "{\"status\":" + status
                + ",\"app_field\":" + System.Text.Json.JsonSerializer.Serialize(appFieldB64)
                + ",\"app_id\":" + System.Text.Json.JsonSerializer.Serialize(appId)
                + ",\"app_detail\":" + System.Text.Json.JsonSerializer.Serialize(appDetail)
                + "}";

            System.Threading.Tasks.Task.Run(() =>
            {
                try
                {
                    // [Nextendo] Une seule decision, dans NextendoEndpoint : elle choisit qui recoit le
                // jeton du compte pose juste en dessous.
                string baseUrl = NextendoEndpoint.BaseUrl();

                    using HttpRequestMessage req = new(HttpMethod.Post, baseUrl.TrimEnd('/') + "/api/presence")
                    {
                        Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json"),
                    };
                    req.Headers.Add("Authorization", "Bearer " + token);
                    using HttpResponseMessage resp = _http.SendAsync(req).GetAwaiter().GetResult();
                }
                catch
                {
                    // best-effort presence; ignore failures
                }
                finally
                {
                    System.Threading.Interlocked.Exchange(ref _publishing, 0);
                }
            });
        }
    }
}
