using Ryujinx.Common.Configuration;
using Ryujinx.Common;
using System.Linq;
using System.IO;
using Ryujinx.Common.Logging;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Ryujinx.Ava.Common.Locale;

namespace Ryujinx.Ava.Common
{
    /// <summary>
    /// [Nextendo] Thin HTTP client for the Nextendo account service, used by the
    /// Settings → Nextendo page (profile + friends). Authenticates with the locally
    /// persisted NEX token. All calls are best-effort; errors are returned as text.
    /// </summary>
    public static class NextendoApi
    {
        // Release channel this build belongs to. The kill-switch / forced-update config is keyed by
        // channel, so this V1 line is isolated: retiring the old "beta" channel on the server blocks
        // every pre-V1 build's online without touching V1.
        public const string ReleaseChannel = "v1";

        public sealed class Friend
        {
            public ulong Pid;
            public string Username = "";
            public string Name = "";
            public string FriendCode = "";
            public string ImageBase64 = "";

            /// <summary>True when the local account has starred this friend as a favorite (synced with the website).</summary>
            public bool Favorite;

            /// <summary>Live presence from the account server: 0 = offline, non-zero = online.</summary>
            public int OnlineStatus;

            /// <summary>Title id the friend is playing right now, empty when offline/unknown.</summary>
            public string AppId = "";

            /// <summary>
            /// What the friend is doing inside the game ("Single Player"), from that game's play
            /// report. Empty for most titles — only ~13 games have a spec that can decode one.
            /// </summary>
            public string AppDetail = "";

            public bool IsOnline => OnlineStatus != 0;
        }

        public sealed class HistoryItem
        {
            public string TitleId = "";
            public string Name = "";
            public string IconBase64 = "";
            public long Seconds;
            public string LastPlayed = "";
        }

        /// <summary>Remote beta control payload (kill-switch + forced-update).</summary>
        public sealed class BetaConfig
        {
            public bool OnlineEnabled;
            public string MinAppVersion = "0.0.0";
            public string MessageEn = "";
            public string MessageFr = "";
            public string ForceUpdateUrl = "";
        }

        // Pushes the local play history and returns the merged, account-stored history
        // (so it persists across reinstalls / machines and never disappears).
        public static async Task<List<HistoryItem>> SyncHistoryAsync(List<HistoryItem> local)
        {
            List<HistoryItem> result = [];
            try
            {
                List<object> items = [];
                foreach (HistoryItem h in local)
                {
                    items.Add(new
                    {
                        title_id = h.TitleId,
                        name = h.Name,
                        icon = h.IconBase64,
                        seconds = h.Seconds,
                        last_played = h.LastPlayed,
                    });
                }

                using HttpClient http = Client();
                StringContent body = new(JsonSerializer.Serialize(new { history = items }), Encoding.UTF8, "application/json");
                HttpResponseMessage resp = await http.PutAsync($"{BaseUrl()}/api/history", body);
                using JsonDocument doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
                if (doc.RootElement.TryGetProperty("history", out JsonElement arr) && arr.ValueKind == JsonValueKind.Array)
                {
                    foreach (JsonElement e in arr.EnumerateArray())
                    {
                        result.Add(new HistoryItem
                        {
                            TitleId = e.TryGetProperty("title_id", out JsonElement t) ? t.GetString() ?? "" : "",
                            Name = e.TryGetProperty("name", out JsonElement n) ? n.GetString() ?? "" : "",
                            IconBase64 = e.TryGetProperty("icon", out JsonElement ic) ? ic.GetString() ?? "" : "",
                            Seconds = e.TryGetProperty("seconds", out JsonElement s) && s.TryGetInt64(out long sv) ? sv : 0,
                            LastPlayed = e.TryGetProperty("last_played", out JsonElement lp) ? lp.GetString() ?? "" : "",
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Warning?.Print(LogClass.Application, $"[Nextendo] history sync failed: {ex.Message}");
            }

            return result;
        }

        public static string BaseUrl()
        {
            // [Nextendo] Une seule decision, dans NextendoEndpoint : c'est elle qui choisit qui
            // recoit le jeton du compte. Cette logique etait dupliquee ici et acceptait
            // n'importe quelle valeur de NEXTENDO_API.
            return NextendoEndpoint.BaseUrl();
        }

        // The public WEBSITE (account creation / login), distinct from the API base above.
        // The "create an account / log in" buttons open THIS, not the API host.
        public static string SiteUrl()
        {
            // [Nextendo] Same override, same reasoning, as NextendoEndpoint.BaseUrl(): a custom
            // account domain typed by hand into Settings redirects the website too, not just the
            // API -- someone pointed at a friend's server should land on THEIR login page.
            string domaineCompte = NextendoServerOverride.AccountDomainUrl;
            if (domaineCompte is not null)
            {
                return domaineCompte;
            }

            string url = Environment.GetEnvironmentVariable("NEXTENDO_SITE");
            if (string.IsNullOrEmpty(url))
            {
                url = "https://nextendo.network";
            }

            return url.TrimEnd('/');
        }

        private static HttpClient Client()
        {
            // [Nextendo] Seconde barrière, volontairement redondante avec NextendoAccount :
            // en mode « serveur personnalisé » aucune requête ne doit partir vers nos
            // services, même celles qui ne regardent pas si un compte est lié (les compteurs
            // publics, par exemple). Le jeton, lui, est déjà tu à la source ; ceci ferme le
            // reste — l'existence même du trafic.
            if (NextendoServerOverride.HorsNextendo)
            {
                throw new NextendoDesactiveException();
            }

            HttpClient http = new() { Timeout = TimeSpan.FromSeconds(15) };
            if (!string.IsNullOrEmpty(NextendoAccount.NexToken))
            {
                http.DefaultRequestHeaders.Add("Authorization", "Bearer " + NextendoAccount.NexToken);
            }

            return http;
        }

        /// <summary>
        /// Levée quand on tente de joindre Nextendo alors que le mode « serveur personnalisé »
        /// est actif. Tous les appels de cette classe sont déjà enveloppés dans un try/catch
        /// qui journalise et rend une valeur vide : l'appelant obtient donc « rien », ce qui
        /// est exactement l'état attendu hors Nextendo.
        /// </summary>
        public sealed class NextendoDesactiveException : Exception
        {
            public NextendoDesactiveException()
                : base("Nextendo Network est desactive : mode serveur personnalise actif.")
            {
            }
        }

        // [Nextendo] Granular online-refusal state, mirroring the account server's /api/online-status.
        // Restored here because the scrubbed public source dropped this member: AvaHostUIHandler needs
        // it to explain WHY online is refused (unverified e-mail, unlinked Discord, playing elsewhere,
        // operator-disabled) instead of always blaming a server outage.
        public enum OnlineRefusalState
        {
            NotBlocked,
            Blocked,
            Unreachable,
        }

        // Asks the account server whether one of the online gates is refusing THIS account. Returns
        // (state, reason) with reason in { "", "unverified", "discord_unlinked", "elsewhere",
        // "disabled", "unknown" }. Any transport error -> Unreachable (caller shows the generic
        // "servers unreachable" message rather than inventing a refusal).
        public static async Task<(OnlineRefusalState state, string reason)> GetOnlineRefusalAsync()
        {
            try
            {
                using HttpClient http = Client();
                HttpResponseMessage resp = await http.GetAsync($"{BaseUrl()}/api/online-status");
                if (!resp.IsSuccessStatusCode)
                {
                    return (OnlineRefusalState.Unreachable, "");
                }

                using JsonDocument doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
                JsonElement r = doc.RootElement;
                bool allow = r.TryGetProperty("allow", out JsonElement a) && a.ValueKind == JsonValueKind.True;
                string reason = r.TryGetProperty("reason", out JsonElement rs) ? (rs.GetString() ?? "") : "";
                return allow ? (OnlineRefusalState.NotBlocked, reason) : (OnlineRefusalState.Blocked, reason);
            }
            catch
            {
                return (OnlineRefusalState.Unreachable, "");
            }
        }

        /// <summary>
        /// [Nextendo] Raised when the server definitively rejects our stored token (HTTP 401) and the
        /// local session is auto-wiped as a result. The UI subscribes to refresh the account panel and
        /// invite the player to sign in again.
        /// </summary>
        public static event Action SessionRevoked;

        /// <summary>
        /// [Nextendo] Auto-heal. A 401 on an authenticated call means the locally stored NEX token is
        /// no good — expired, or REVOKED. (2026-07-22 incident: the 1.6.5 Windows package accidentally
        /// shipped the maintainer's live session file — portable/nextendo_account.txt — so every
        /// download booted logged in as him and carried HIS token; that token is now denylisted on the
        /// server.) Left in place, such a client keeps presenting someone else's PID as its identity —
        /// in the account panel AND, worse, as the bare NEX login id in-game. So on a definitive 401 we
        /// wipe the local session: the identity falls back to the anonymous 0xcafe stub (no longer
        /// impersonating anyone) and the player is prompted to sign in as themselves. Only a real 401
        /// triggers this — network failures and 5xx surface as exceptions and must NEVER clear a valid
        /// session. Returns true when a session was cleared.
        /// </summary>
        private static bool HealIfRejected(HttpResponseMessage resp)
        {
            if (resp is null || resp.StatusCode != HttpStatusCode.Unauthorized || string.IsNullOrEmpty(NextendoAccount.NexToken))
            {
                return false;
            }

            Logger.Warning?.Print(LogClass.Application,
                "[Nextendo] jeton rejeté par le serveur (401) — session locale purgée (compte révoqué ou expiré). Reconnexion requise.");
            NextendoAccount.Clear();
            try { SessionRevoked?.Invoke(); } catch { /* UI not ready yet */ }
            return true;
        }

        /// <summary>
        /// [Nextendo] Sends a bug report — the error code the player hit, what they were doing, and
        /// the tail of the emulator log — to the account server, where it lands in the admin inbox.
        ///
        /// The log is the point of the whole thing: it turns "somebody got 2618-0006 on Discord"
        /// into the actual session, with the server able to line it up by PID and time. The player
        /// has it under their eyes at the moment it breaks; this is the one moment it is worth
        /// capturing. Only the TAIL is sent — that is where the error is, and the server caps it
        /// again anyway.
        /// </summary>
        public static async Task<(bool ok, string message)> SendReportAsync(string errorCode, string comment, bool attachLog = true)
        {
            if (string.IsNullOrEmpty(NextendoAccount.NexToken))
            {
                return (false, "Connecte-toi à Nextendo pour signaler un problème.");
            }

            try
            {
                string game = TitleIDs.CurrentApplication.Value.TryGet(out string tid) ? tid : "";

                var payload = new
                {
                    error_code = errorCode ?? "",
                    game,
                    version = Ryujinx.Common.ReleaseInformation.Version,
                    comment = comment ?? "",
                    log = attachLog ? ReadLogTail() : "",
                };

                using HttpClient http = Client();
                using StringContent body = new(
                    JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

                HttpResponseMessage resp = await http.PostAsync($"{BaseUrl()}/api/report", body);

                if (resp.IsSuccessStatusCode)
                {
                    return (true, "");
                }

                // Surface the server's own message (e.g. the rate-limit refusal), not a generic one.
                string reason = await ReadErrorMessage(resp);

                return (false, reason);
            }
            catch (Exception ex)
            {
                Logger.Warning?.Print(LogClass.Application, $"[Nextendo] report failed: {ex.Message}");

                return (false, "Envoi impossible. Vérifie ta connexion.");
            }
        }

        // The last chunk of the current log file, read even while Ryujinx is still writing it
        // (the file is opened FileShare.Read). Best-effort: a report without the log is still
        // worth sending, so any failure just yields an empty tail rather than aborting.
        private const int ReportLogTailBytes = 96 * 1024;

        private static string ReadLogTail()
        {
            try
            {
                string dir = Ryujinx.Common.Configuration.AppDataManager.LogsDirPath;
                if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir))
                {
                    return "";
                }

                FileInfo latest = new DirectoryInfo(dir)
                    .GetFiles("*.log")
                    .OrderByDescending(f => f.LastWriteTimeUtc)
                    .FirstOrDefault();

                if (latest is null)
                {
                    return "";
                }

                // Share write access: the logger holds the file open, so anything less throws.
                using FileStream fs = new(latest.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);

                if (fs.Length > ReportLogTailBytes)
                {
                    fs.Seek(-ReportLogTailBytes, SeekOrigin.End);
                }

                using StreamReader reader = new(fs);

                return reader.ReadToEnd();
            }
            catch (Exception ex)
            {
                Logger.Debug?.Print(LogClass.Application, $"[Nextendo] could not read log tail: {ex.Message}");

                return "";
            }
        }

        private static async Task<string> ReadErrorMessage(HttpResponseMessage resp)
        {
            try
            {
                using JsonDocument doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
                if (doc.RootElement.TryGetProperty("error", out JsonElement err) && err.ValueKind == JsonValueKind.String)
                {
                    return err.GetString();
                }
            }
            catch
            {
                // Not JSON, or no message: fall through to the generic line.
            }

            return "Le signalement n'a pas pu être envoyé.";
        }

        // [Nextendo] Tire le pseudo AUTORITAIRE du compte (champ "username") + la photo de profil,
        // pour resynchroniser le profil local SANS deco/reco (appelé à l'ouverture des réglages).
        // [Nextendo] Why is this account's online refused? The NEX auth protocol has no field
        // to carry a reason back to the client, so a rejected login is indistinguishable from
        // an outage — which is why every gate used to surface as "servers unreachable" and told
        // players a maintenance lie. This asks the account server directly, after the fact.
        //
        // Read-only on the server (creates no play session): merely asking must not trip the
        // "already playing elsewhere" gate against the caller.
        //
        // Returns the raw reason string (unknown / disabled / unverified / discord_unlinked /
        // elsewhere), or null when online is actually allowed or the server can't be reached —
        // in which case "servers unreachable" IS the honest answer.
        public static async Task<string> GetOnlineRefusalReasonAsync()
        {
            try
            {
                using HttpClient http = Client();
                HttpResponseMessage resp = await http.GetAsync($"{BaseUrl()}/api/online-status");
                if (!resp.IsSuccessStatusCode)
                {
                    return null;
                }

                using JsonDocument doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
                JsonElement root = doc.RootElement;
                if (root.TryGetProperty("allow", out JsonElement allow) && allow.ValueKind == JsonValueKind.True)
                {
                    return null;
                }

                return root.TryGetProperty("reason", out JsonElement r) ? r.GetString() : null;
            }
            catch
            {
                return null;
            }
        }

        public static async Task<(string name, byte[] image)> GetProfileSyncAsync()
        {
            try
            {
                using HttpClient http = Client();
                HttpResponseMessage resp = await http.GetAsync($"{BaseUrl()}/api/profile");
                if (!resp.IsSuccessStatusCode)
                {
                    HealIfRejected(resp); // 401 → session locale invalide, on purge (anti-usurpation)
                    return (null, null);
                }

                using JsonDocument doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
                JsonElement root = doc.RootElement;
                string name = root.TryGetProperty("username", out JsonElement u) ? u.GetString() : null;

                byte[] image = null;
                if (root.TryGetProperty("profile", out JsonElement prof) && prof.ValueKind == JsonValueKind.Object
                    && prof.TryGetProperty("image", out JsonElement im) && im.ValueKind == JsonValueKind.String)
                {
                    string b64 = im.GetString();
                    if (!string.IsNullOrEmpty(b64))
                    {
                        try { image = Convert.FromBase64String(b64); } catch { /* ignore */ }
                    }
                }

                return (name, image);
            }
            catch
            {
                return (null, null);
            }
        }

        // Creates a no-account GUEST online profile (the beta "play without an account"
        // path). On success the minted identity is persisted locally as the linked profile
        // (marked guest) so online play, friends and save sync all work; the player keeps
        // this profile and can rename it later. Returns (ok, errorMessage). No auth header —
        // this call MINTS the identity.
        public static async Task<(bool ok, string message)> CreateGuestAsync(string nickname)
        {
            try
            {
                using HttpClient http = new() { Timeout = TimeSpan.FromSeconds(15) };
                string payload = $"{{\"username\":{JsonSerializer.Serialize(nickname)}}}";
                using StringContent body = new(payload, Encoding.UTF8, "application/json");
                HttpResponseMessage resp = await http.PostAsync($"{BaseUrl()}/api/guest", body);
                using JsonDocument doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
                if (!resp.IsSuccessStatusCode)
                {
                    return (false, doc.RootElement.TryGetProperty("error", out JsonElement e) ? (e.GetString() ?? "Error") : "Error");
                }

                string nexToken = doc.RootElement.TryGetProperty("nex_token", out JsonElement nt) ? (nt.GetString() ?? "") : "";
                ulong pid = 0;
                string username = nickname, friendCode = "";
                if (doc.RootElement.TryGetProperty("account", out JsonElement acc) && acc.ValueKind == JsonValueKind.Object)
                {
                    if (acc.TryGetProperty("pid", out JsonElement p))
                    {
                        p.TryGetUInt64(out pid);
                    }
                    if (acc.TryGetProperty("username", out JsonElement u))
                    {
                        username = u.GetString() ?? nickname;
                    }
                    if (acc.TryGetProperty("friend_code", out JsonElement c))
                    {
                        friendCode = c.GetString() ?? "";
                    }
                }

                if (pid == 0 || string.IsNullOrEmpty(nexToken))
                {
                    return (false, "Invalid server response");
                }

                NextendoAccount.Save(pid, username, friendCode, nexToken, isGuest: true);
                return (true, "");
            }
            catch (Exception ex)
            {
                return (false, ex.Message);
            }
        }

        // Checks whether a guest nickname is free. true = available, false = already taken,
        // null = couldn't reach the server (the caller can let the create attempt decide).
        public static async Task<bool?> CheckNicknameAvailableAsync(string nickname)
        {
            try
            {
                using HttpClient http = new() { Timeout = TimeSpan.FromSeconds(8) };
                HttpResponseMessage resp = await http.GetAsync($"{BaseUrl()}/api/username-available?username={Uri.EscapeDataString(nickname)}");
                if (!resp.IsSuccessStatusCode)
                {
                    return null;
                }

                using JsonDocument doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
                return doc.RootElement.TryGetProperty("available", out JsonElement a) ? a.ValueKind == JsonValueKind.True : (bool?)null;
            }
            catch
            {
                return null;
            }
        }

        // Polls the remote kill-switch / forced-update config. Returns (config, reachable).
        // reachable=false on ANY failure (timeout, network, non-200) so the caller FAILS
        // CLOSED — blocks online and shows the "servers unreachable" message.
        public static async Task<(BetaConfig cfg, bool reachable)> GetBetaConfigAsync()
        {
            BetaConfig cfg = new();
            try
            {
                using HttpClient http = new() { Timeout = TimeSpan.FromSeconds(8) };
                HttpResponseMessage resp = await http.GetAsync($"{BaseUrl()}/api/beta-config?channel={ReleaseChannel}");
                if (!resp.IsSuccessStatusCode)
                {
                    return (cfg, false);
                }

                using JsonDocument doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
                JsonElement r = doc.RootElement;
                cfg.OnlineEnabled = r.TryGetProperty("online_enabled", out JsonElement oe) && oe.ValueKind == JsonValueKind.True;
                cfg.MinAppVersion = r.TryGetProperty("min_app_version", out JsonElement mv) ? (mv.GetString() ?? "0.0.0") : "0.0.0";
                cfg.MessageEn = r.TryGetProperty("message_en", out JsonElement me) ? (me.GetString() ?? "") : "";
                cfg.MessageFr = r.TryGetProperty("message_fr", out JsonElement mf) ? (mf.GetString() ?? "") : "";
                cfg.ForceUpdateUrl = r.TryGetProperty("force_update_url", out JsonElement fu) ? (fu.GetString() ?? "") : "";
                return (cfg, true);
            }
            catch
            {
                return (cfg, false);
            }
        }

        // Heartbeat: registers/refreshes the "connected" emulator session on the account so it
        // appears in the account's sessions list on the site. No-ops if no account is linked.
        public static async Task TouchSessionAsync()
        {
            if (string.IsNullOrEmpty(NextendoAccount.NexToken))
            {
                return;
            }

            try
            {
                using HttpClient http = Client();
                using StringContent body = new("{}", Encoding.UTF8, "application/json");
                using HttpResponseMessage resp = await http.PostAsync($"{BaseUrl()}/api/nex-session", body);
                HealIfRejected(resp); // token révoqué/expiré → purge la session locale (anti-usurpation)
            }
            catch { /* best-effort */ }
        }

        // Returns the account's accepted friends AND incoming friend requests.
        public static async Task<(List<Friend> friends, List<Friend> requests)> GetSocialAsync()
        {
            List<Friend> friends = [];
            List<Friend> requests = [];
            try
            {
                using HttpClient http = Client();
                HttpResponseMessage resp = await http.GetAsync($"{BaseUrl()}/api/friends");
                HealIfRejected(resp); // 401 → jeton révoqué/expiré, purge la session locale (anti-usurpation)
                using JsonDocument doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
                if (doc.RootElement.TryGetProperty("friends", out JsonElement fa) && fa.ValueKind == JsonValueKind.Array)
                {
                    foreach (JsonElement f in fa.EnumerateArray())
                    {
                        friends.Add(ParseFriend(f));
                    }
                }
                if (doc.RootElement.TryGetProperty("requests", out JsonElement ra) && ra.ValueKind == JsonValueKind.Array)
                {
                    foreach (JsonElement f in ra.EnumerateArray())
                    {
                        requests.Add(ParseFriend(f));
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Warning?.Print(LogClass.Application, $"[Nextendo] GetSocial failed: {ex.Message}");
            }

            return (friends, requests);
        }

        // Sends a friend request by friend code. Returns (ok, message).
        public static async Task<(bool ok, string message)> AddFriendAsync(string friendCode)
        {
            try
            {
                using HttpClient http = Client();
                string payload = $"{{\"friend_code\":{JsonSerializer.Serialize(friendCode)}}}";
                using StringContent body = new(payload, Encoding.UTF8, "application/json");
                HttpResponseMessage resp = await http.PostAsync($"{BaseUrl()}/api/friends", body);
                using JsonDocument doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
                if (resp.IsSuccessStatusCode && doc.RootElement.TryGetProperty("friend", out JsonElement f))
                {
                    string name = ParseFriend(f).Name;
                    bool already = doc.RootElement.TryGetProperty("already", out JsonElement a) && a.ValueKind == JsonValueKind.True;
                    return (true, already ? LocaleManager.Instance[LocaleKeys.Dialog_Nextendo_FriendAlreadyFriends] : LocaleManager.Instance.UpdateAndGetDynamicValue(LocaleKeys.Dialog_Nextendo_FriendRequestSent, name));
                }

                return (false, doc.RootElement.TryGetProperty("error", out JsonElement e) ? (e.GetString() ?? "Erreur") : "Erreur");
            }
            catch (Exception ex)
            {
                return (false, ex.Message);
            }
        }

        public static async Task<bool> AcceptFriendAsync(ulong pid)
        {
            try
            {
                using HttpClient http = Client();
                using StringContent body = new($"{{\"pid\":{pid}}}", Encoding.UTF8, "application/json");
                HttpResponseMessage resp = await http.PostAsync($"{BaseUrl()}/api/friends/accept", body);
                return resp.IsSuccessStatusCode;
            }
            catch { return false; }
        }

        /// <summary>
        /// [Nextendo] Accepts EVERY pending incoming friend request in one shot. Fetches the current
        /// request list from the server first — so it acts on the true pending set, not a possibly
        /// stale UI copy — then accepts each one. Returns how many were actually accepted.
        /// </summary>
        public static async Task<int> AcceptAllRequestsAsync()
        {
            (_, List<Friend> requests) = await GetSocialAsync();

            int accepted = 0;
            foreach (Friend r in requests)
            {
                if (r.Pid != 0 && await AcceptFriendAsync(r.Pid))
                {
                    accepted++;
                }
            }

            return accepted;
        }

        public static async Task DeclineFriendAsync(ulong pid)
        {
            try
            {
                using HttpClient http = Client();
                using StringContent body = new($"{{\"pid\":{pid}}}", Encoding.UTF8, "application/json");
                await http.PostAsync($"{BaseUrl()}/api/friends/decline", body);
            }
            catch (Exception ex)
            {
                Logger.Warning?.Print(LogClass.Application, $"[Nextendo] Decline failed: {ex.Message}");
            }
        }

        public static async Task RemoveFriendAsync(ulong pid)
        {
            try
            {
                using HttpClient http = Client();
                using StringContent body = new($"{{\"pid\":{pid}}}", Encoding.UTF8, "application/json");
                await http.PostAsync($"{BaseUrl()}/api/friends/remove", body);
            }
            catch (Exception ex)
            {
                Logger.Warning?.Print(LogClass.Application, $"[Nextendo] RemoveFriend failed: {ex.Message}");
            }
        }

        /// <summary>Stars/unstars a friend. Stored on the account → synced with the website.</summary>
        public static async Task SetFavoriteAsync(ulong pid, bool favorite)
        {
            try
            {
                using HttpClient http = Client();
                using StringContent body = new($"{{\"pid\":{pid},\"favorite\":{(favorite ? "true" : "false")}}}", Encoding.UTF8, "application/json");
                await http.PostAsync($"{BaseUrl()}/api/friends/favorite", body);
            }
            catch (Exception ex)
            {
                Logger.Warning?.Print(LogClass.Application, $"[Nextendo] SetFavorite failed: {ex.Message}");
            }
        }

        // -------------------------------------------------------------------
        // [Nextendo] Mon salon en direct, mes dernières rencontres, et le
        // signalement d'un joueur.
        //
        // Ces trois appels partagent un principe : le serveur ne renvoie JAMAIS
        // d'adresse IP. Elle existe côté serveur de jeu, mais dans un tableau
        // que l'endpoint ne lit pas — c'est délibéré, et c'est ce qui permet de
        // montrer un salon à un joueur sans exposer où habitent les autres.
        //
        // Les avatars arrivent en URL et non en base64 inline comme pour les
        // amis : à 8 joueurs rafraîchis toutes les 5 s, le base64 pèserait des
        // centaines de kilo-octets par sondage, et 2 Mo pour 50 rencontres.
        // On télécharge donc à part, une seule fois par PID (voir le cache).

        /// <summary>Un joueur tel qu'il apparaît dans un salon ou dans l'historique des rencontres.</summary>
        public sealed class NextendoPlayer
        {
            public ulong Pid;
            public string Name = "";

            /// <summary>False quand le PID ne correspond à aucun compte Nextendo connu :
            /// on ne peut alors ni l'ajouter en ami, ni le signaler utilement.</summary>
            public bool Known;

            public string AvatarUrl = "";

            /// <summary>Code ami, envoye par le serveur pour que le bouton d ajout
            /// rapide reutilise /api/friends, qui prend un code et non un PID.</summary>
            public string FriendCode = "";

            /// <summary>Hôte du salon. Vide de sens dans la liste des rencontres.</summary>
            public bool Host;

            /// <summary>C'est moi. Sert à ne pas m'afficher un bouton « signaler » sur moi-même.</summary>
            public bool IsMe;

            /// <summary>Title id du jeu où la rencontre a eu lieu.</summary>
            public string TitleId = "";

            /// <summary>Date de la rencontre, en heure locale. DateTime.MinValue si inconnue.</summary>
            public DateTime SeenAt = DateTime.MinValue;
        }

        /// <summary>L'état du salon courant.</summary>
        public sealed class NextendoLobby
        {
            public bool InLobby;
            public string TitleId = "";
            public string Type = "";

            /// <summary>Libellé brut publié par le serveur de jeu. Écrit pour le
            /// monitoring, donc EN FRANÇAIS : ne jamais l'afficher tel quel dans
            /// l'émulateur, qui suit la langue du joueur. Voir <see cref="StateCode"/>.</summary>
            public string State = "";

            /// <summary>Code stable dérivé de <see cref="State"/> par le serveur de
            /// comptes : « searching », « matched », ou vide si le serveur de jeu a
            /// publié un état qu'il ne sait pas classer. C'est CE champ qu'on traduit.</summary>
            public string StateCode = "";

            /// <summary>Identifiant du salon côté serveur de jeu. Sert d'identifiant de
            /// groupe pour Discord : deux joueurs du même salon doivent porter le même,
            /// sinon Discord affiche deux groupes là où il n'y en a qu'un.</summary>
            public ulong Id;

            public int Count;
            public int Max;
            public List<NextendoPlayer> Players = [];
        }

        /// <summary>Le salon où je me trouve MAINTENANT, et qui y est avec moi.</summary>
        public static async Task<NextendoLobby> GetMyLobbyAsync()
        {
            NextendoLobby lobby = new();
            try
            {
                using HttpClient http = Client();
                HttpResponseMessage resp = await http.GetAsync($"{BaseUrl()}/api/my-lobby");
                HealIfRejected(resp);
                using JsonDocument doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
                JsonElement root = doc.RootElement;

                lobby.InLobby = root.TryGetProperty("in_lobby", out JsonElement il) && il.ValueKind == JsonValueKind.True;
                if (!lobby.InLobby)
                {
                    return lobby;
                }

                lobby.TitleId = root.TryGetProperty("title_id", out JsonElement ti) ? (ti.GetString() ?? "") : "";
                if (root.TryGetProperty("lobby", out JsonElement l) && l.ValueKind == JsonValueKind.Object)
                {
                    lobby.Type = l.TryGetProperty("type", out JsonElement ty) ? (ty.GetString() ?? "") : "";
                    lobby.State = l.TryGetProperty("state", out JsonElement st) ? (st.GetString() ?? "") : "";
                    lobby.StateCode = l.TryGetProperty("state_code", out JsonElement sc) ? (sc.GetString() ?? "") : "";
                    lobby.Id = l.TryGetProperty("id", out JsonElement lid) && lid.TryGetUInt64(out ulong idv) ? idv : 0;
                    lobby.Count = l.TryGetProperty("count", out JsonElement c) ? c.GetInt32() : 0;
                    lobby.Max = l.TryGetProperty("max", out JsonElement mx) ? mx.GetInt32() : 0;
                }
                if (root.TryGetProperty("players", out JsonElement pa) && pa.ValueKind == JsonValueKind.Array)
                {
                    foreach (JsonElement p in pa.EnumerateArray())
                    {
                        lobby.Players.Add(ParseNextendoPlayer(p));
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Warning?.Print(LogClass.Application, $"[Nextendo] GetMyLobby failed: {ex.Message}");
            }

            return lobby;
        }

        /// <summary>Les 50 dernières personnes croisées en ligne, la plus récente d'abord.</summary>
        public static async Task<List<NextendoPlayer>> GetRecentPlayersAsync()
        {
            List<NextendoPlayer> players = [];
            try
            {
                using HttpClient http = Client();
                HttpResponseMessage resp = await http.GetAsync($"{BaseUrl()}/api/recent-players");
                HealIfRejected(resp);
                using JsonDocument doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
                if (doc.RootElement.TryGetProperty("players", out JsonElement pa) && pa.ValueKind == JsonValueKind.Array)
                {
                    foreach (JsonElement p in pa.EnumerateArray())
                    {
                        players.Add(ParseNextendoPlayer(p));
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Warning?.Print(LogClass.Application, $"[Nextendo] GetRecentPlayers failed: {ex.Message}");
            }

            return players;
        }

        /// <summary>Signale un joueur. Le serveur refuse si on ne l'a jamais croisé.</summary>
        public static async Task<(bool ok, string message)> ReportPlayerAsync(ulong pid, string reason, string comment)
        {
            try
            {
                using HttpClient http = Client();
                string payload = JsonSerializer.Serialize(new
                {
                    target_pid = pid,
                    reason = reason ?? "",
                    comment = comment ?? "",
                });
                using StringContent body = new(payload, Encoding.UTF8, "application/json");
                HttpResponseMessage resp = await http.PostAsync($"{BaseUrl()}/api/report-player", body);
                HealIfRejected(resp);

                if (resp.IsSuccessStatusCode)
                {
                    return (true, "");
                }

                // Le serveur distingue ses refus, et le joueur mérite de savoir
                // lequel : « déjà 10 signalements cette heure » n'appelle pas la
                // même réaction que « vous n'avez pas croisé ce joueur ».
                string erreur = "";
                try
                {
                    using JsonDocument doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
                    if (doc.RootElement.TryGetProperty("error", out JsonElement e))
                    {
                        erreur = e.GetString() ?? "";
                    }
                }
                catch (Exception)
                {
                    // Corps illisible : on retombe sur le code HTTP seul.
                }

                return (false, erreur);
            }
            catch (Exception ex)
            {
                Logger.Warning?.Print(LogClass.Application, $"[Nextendo] ReportPlayer failed: {ex.Message}");

                return (false, "network");
            }
        }

        // Cache d'avatars. Une photo de profil change rarement et sert à chaque
        // rafraîchissement du salon : la retélécharger toutes les 5 secondes
        // pour huit joueurs serait absurde. Borné pour ne pas croître sans fin
        // au fil des rencontres.
        private static readonly Dictionary<ulong, byte[]> _avatarCache = [];
        private const int AvatarCacheMax = 200;

        /// <summary>
        /// Client dédié aux avatars, SANS en-tête d'autorisation.
        ///
        /// ⚠️ /api/avatar est public et sans authentification : joindre le jeton du compte
        /// n'apporte rien et l'expose. Une version antérieure passait par Client(), qui pose le
        /// Bearer, sur une URL ABSOLUE venue de la réponse du serveur — c'est-à-dire qu'un champ
        /// JSON décidait où partait le jeton. C'est exactement le trou que NextendoEndpoint avait
        /// été écrit pour fermer sur la variable NEXTENDO_API.
        /// </summary>
        private static readonly HttpClient _avatarHttp = new() { Timeout = TimeSpan.FromSeconds(10) };

        /// <summary>Télécharge (et mémorise) la photo de profil d'un joueur. Null si indisponible.</summary>
        public static async Task<byte[]> GetAvatarAsync(ulong pid, string url)
        {
            if (pid == 0)
            {
                return null;
            }

            lock (_avatarCache)
            {
                if (_avatarCache.TryGetValue(pid, out byte[] cached))
                {
                    return cached;
                }
            }

            try
            {
                // L'URL est RECONSTRUITE ici, jamais reprise du serveur : le paramètre `url` ne
                // sert plus qu'à savoir si ce compte a une photo. Laisser une réponse choisir
                // l'hôte d'une requête sortante, c'est laisser un champ JSON faire visiter le
                // réseau local de la machine du joueur.
                if (string.IsNullOrEmpty(url))
                {
                    return null;
                }

                byte[] data = await _avatarHttp.GetByteArrayAsync(
                    $"{BaseUrl()}/api/avatar?pid={pid}");

                lock (_avatarCache)
                {
                    if (_avatarCache.Count >= AvatarCacheMax)
                    {
                        _avatarCache.Clear();
                    }
                    _avatarCache[pid] = data;
                }

                return data;
            }
            catch (Exception)
            {
                // Pas d'avatar : l'écran affiche l'initiale du pseudo. Ce n'est
                // pas une erreur digne d'un log à chaque rafraîchissement.
                return null;
            }
        }

        private static NextendoPlayer ParseNextendoPlayer(JsonElement p)
        {
            NextendoPlayer player = new()
            {
                Pid = p.TryGetProperty("pid", out JsonElement id) ? id.GetUInt64() : 0,
                Name = p.TryGetProperty("name", out JsonElement n) ? (n.GetString() ?? "") : "",
                Known = p.TryGetProperty("known", out JsonElement k) && k.ValueKind == JsonValueKind.True,
                AvatarUrl = p.TryGetProperty("avatar_url", out JsonElement a) ? (a.GetString() ?? "") : "",
                FriendCode = p.TryGetProperty("friend_code", out JsonElement fc) ? (fc.GetString() ?? "") : "",
                Host = p.TryGetProperty("host", out JsonElement h) && h.ValueKind == JsonValueKind.True,
                IsMe = p.TryGetProperty("is_me", out JsonElement m) && m.ValueKind == JsonValueKind.True,
                TitleId = p.TryGetProperty("title_id", out JsonElement t) ? (t.GetString() ?? "") : "",
            };

            if (p.TryGetProperty("seen_at", out JsonElement s)
                && DateTime.TryParse(s.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out DateTime vu))
            {
                player.SeenAt = vu.ToLocalTime();
            }

            return player;
        }

        // -------------------------------------------------------------------
        // [Nextendo] "Sign in with Nextendo" — OAuth 2.0 Authorization Code + PKCE with a loopback
        // redirect (RFC 8252), the standard secure flow for a native desktop app (same idea as
        // `gh auth login`). The emulator NEVER sees the password: the user authenticates in their
        // browser on nextendo.network, and we only ever receive a short-lived code that we exchange
        // (with PKCE) for the online token. This is what lets the website login sit behind Turnstile
        // without ever blocking the emulator.

        public static async Task<(bool ok, string error)> SignInWithBrowserAsync()
        {
            // PKCE (S256) + a CSRF `state` we verify on the way back.
            string verifier = RandUrl(32);
            string challenge = Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
            string state = RandUrl(24);

            // Loopback listener on a free port, 127.0.0.1 only (loopback needs no admin/URL ACL).
            // A RAW TcpListener, NOT HttpListener: on Linux/macOS HttpListener is a fragile managed
            // shim (only Windows gets the http.sys kernel driver), and its loopback callback never
            // fired for Linux users — the browser authorised, but this listener was never hit, so the
            // account never linked. Reading the single GET request line off a raw socket behaves
            // identically on every OS.
            TcpListener listener;
            int port;
            try
            {
                listener = new TcpListener(IPAddress.Loopback, 0);
                listener.Start();
                port = ((IPEndPoint)listener.LocalEndpoint).Port;
            }
            catch (Exception ex)
            {
                return (false, $"Impossible d'ouvrir un port local : {ex.Message}");
            }

            string redirectUri = $"http://127.0.0.1:{port}/callback";
            string authorizeUrl =
                $"{BaseUrl()}/api/oauth/authorize?response_type=code&client_id=nextendo-emulator" +
                $"&redirect_uri={Uri.EscapeDataString(redirectUri)}&scope=identity+friends" +
                $"&state={state}&code_challenge={challenge}&code_challenge_method=S256";

            try
            {
                Ryujinx.Common.Helper.OpenHelper.OpenUrl(authorizeUrl);
            }
            catch (Exception ex)
            {
                listener.Stop();
                return (false, $"Impossible d'ouvrir le navigateur : {ex.Message}");
            }

            Logger.Info?.Print(LogClass.Application,
                $"[Nextendo] OAuth: browser opened, waiting for loopback callback on 127.0.0.1:{port}");

            // Wait for the browser to hit the loopback callback (max 5 minutes).
            string code, gotState, oauthError;
            try
            {
                using System.Threading.CancellationTokenSource timeout = new(TimeSpan.FromMinutes(5));
                using Socket socket = await listener.AcceptSocketAsync(timeout.Token);
                using NetworkStream stream = new(socket, ownsSocket: false);

                string requestTarget = await ReadRequestTargetAsync(stream, timeout.Token);
                Uri callback = new("http://127.0.0.1" +
                    (requestTarget.StartsWith('/') ? requestTarget : "/" + requestTarget));
                Dictionary<string, string> query = ParseQuery(callback.Query);
                query.TryGetValue("code", out code);
                query.TryGetValue("state", out gotState);
                query.TryGetValue("error", out oauthError);

                bool good = string.IsNullOrEmpty(oauthError) && !string.IsNullOrEmpty(code) && gotState == state;
                Logger.Info?.Print(LogClass.Application,
                    $"[Nextendo] OAuth: loopback callback received (code={(string.IsNullOrEmpty(code) ? "none" : "yes")}, state_ok={gotState == state}, error={oauthError ?? "none"})");

                await WriteLoopbackResponseAsync(stream, good);
            }
            catch (OperationCanceledException)
            {
                Logger.Warning?.Print(LogClass.Application,
                    "[Nextendo] OAuth: no loopback callback within 5 min — the browser never reached 127.0.0.1 (sandboxed browser / firewall?)");
                return (false, "Délai de connexion dépassé.");
            }
            catch (Exception ex)
            {
                return (false, ex.Message);
            }
            finally
            {
                listener.Stop();
            }

            if (!string.IsNullOrEmpty(oauthError))
            {
                return (false, oauthError == "access_denied" ? "Connexion refusée." : oauthError);
            }
            if (string.IsNullOrEmpty(code))
            {
                return (false, "Aucun code reçu du navigateur.");
            }
            if (gotState != state)
            {
                return (false, "Vérification anti-CSRF échouée."); // never trust a mismatched state
            }

            // Exchange the code for the online token — public client, PKCE, no client secret.
            try
            {
                using HttpClient http = new() { Timeout = TimeSpan.FromSeconds(15) };
                using FormUrlEncodedContent form = new(new Dictionary<string, string>
                {
                    ["grant_type"] = "authorization_code",
                    ["code"] = code,
                    ["client_id"] = "nextendo-emulator",
                    ["redirect_uri"] = redirectUri,
                    ["code_verifier"] = verifier,
                });
                using HttpResponseMessage resp = await http.PostAsync($"{BaseUrl()}/api/oauth/token", form);
                string json = await resp.Content.ReadAsStringAsync();
                if (!resp.IsSuccessStatusCode)
                {
                    return (false, "Échec de l'échange du code d'autorisation.");
                }

                using JsonDocument doc = JsonDocument.Parse(json);
                JsonElement root = doc.RootElement;
                string nexToken = root.TryGetProperty("nex_token", out JsonElement nt) ? (nt.GetString() ?? "") : "";
                if (string.IsNullOrEmpty(nexToken) || !root.TryGetProperty("account", out JsonElement acct))
                {
                    return (false, "Réponse d'authentification invalide.");
                }
                ulong pid = acct.GetProperty("pid").GetUInt64();
                string username = acct.TryGetProperty("username", out JsonElement u) ? (u.GetString() ?? "") : "";
                string friendCode = acct.TryGetProperty("friend_code", out JsonElement f) ? (f.GetString() ?? "") : "";

                NextendoAccount.Save(pid, username, friendCode, nexToken);
                return (true, "");
            }
            catch (Exception ex)
            {
                return (false, ex.Message);
            }
        }

        // Read just the HTTP request line's target from the loopback callback, i.e. the
        // "/callback?code=...&state=..." in "GET /callback?... HTTP/1.1". We only need the first line;
        // the browser sends it immediately, so a single read of a few KB always contains it.
        private static async Task<string> ReadRequestTargetAsync(NetworkStream stream, System.Threading.CancellationToken ct)
        {
            byte[] buf = new byte[8192];
            int total = 0;
            while (total < buf.Length)
            {
                int n = await stream.ReadAsync(buf.AsMemory(total, buf.Length - total), ct);
                if (n <= 0)
                {
                    break;
                }

                total += n;
                if (Array.IndexOf(buf, (byte)'\n', 0, total) >= 0)
                {
                    break; // reached the end of the request line
                }
            }

            string firstLine = Encoding.ASCII.GetString(buf, 0, total).Split('\n', 2)[0].TrimEnd('\r');
            string[] parts = firstLine.Split(' ');
            return parts.Length >= 2 ? parts[1] : "/";
        }

        private static Dictionary<string, string> ParseQuery(string query)
        {
            Dictionary<string, string> result = new(StringComparer.Ordinal);
            foreach (string pair in query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
            {
                int eq = pair.IndexOf('=');
                string key = eq < 0 ? pair : pair[..eq];
                string value = eq < 0 ? "" : pair[(eq + 1)..];
                result[Uri.UnescapeDataString(key)] = Uri.UnescapeDataString(value);
            }

            return result;
        }

        private static async Task WriteLoopbackResponseAsync(NetworkStream stream, bool ok)
        {
            string inner = ok
                ? "<h1 style='color:#33E86B'>✓ Connexion réussie</h1><p>Tu peux fermer cet onglet et retourner à l'émulateur Nextendo.</p>"
                : "<h1 style='color:#ff8a8a'>Connexion annulée</h1><p>Retourne à l'émulateur Nextendo et réessaie.</p>";
            byte[] body = Encoding.UTF8.GetBytes(
                "<!doctype html><meta charset=utf-8><title>Nextendo</title>" +
                "<body style='font-family:system-ui,sans-serif;background:#0f1115;color:#e7e9ee;display:grid;place-items:center;height:100vh;margin:0'>" +
                "<div style='text-align:center;max-width:420px'>" + inner + "</div>");
            byte[] header = Encoding.ASCII.GetBytes(
                "HTTP/1.1 200 OK\r\n" +
                "Content-Type: text/html; charset=utf-8\r\n" +
                $"Content-Length: {body.Length}\r\n" +
                "Connection: close\r\n\r\n");
            try
            {
                await stream.WriteAsync(header);
                await stream.WriteAsync(body);
                await stream.FlushAsync();
            }
            catch { /* browser may have closed */ }
        }

        private static string RandUrl(int nbytes) => Base64Url(RandomNumberGenerator.GetBytes(nbytes));

        private static string Base64Url(byte[] b) =>
            Convert.ToBase64String(b).TrimEnd('=').Replace('+', '-').Replace('/', '_');

        // Returns (ok, error).
        public static async Task<(bool ok, string message)> SetUsernameAsync(string username)
        {
            try
            {
                using HttpClient http = Client();
                string payload = $"{{\"username\":{JsonSerializer.Serialize(username)}}}";
                using StringContent body = new(payload, Encoding.UTF8, "application/json");
                HttpResponseMessage resp = await http.PutAsync($"{BaseUrl()}/api/username", body);
                if (resp.IsSuccessStatusCode)
                {
                    return (true, "");
                }

                using JsonDocument doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
                return (false, doc.RootElement.TryGetProperty("error", out JsonElement e) ? (e.GetString() ?? "Erreur") : "Erreur");
            }
            catch (Exception ex)
            {
                return (false, ex.Message);
            }
        }

        // Change the account's profile picture: preserves the existing name + Mii.
        public static async Task<bool> SetProfileImageAsync(byte[] jpeg)
        {
            try
            {
                using HttpClient http = Client();

                string name = "", mii = "";
                try
                {
                    HttpResponseMessage cur = await http.GetAsync($"{BaseUrl()}/api/profile");
                    using JsonDocument doc = JsonDocument.Parse(await cur.Content.ReadAsStringAsync());
                    if (doc.RootElement.TryGetProperty("profile", out JsonElement prof) && prof.ValueKind == JsonValueKind.Object)
                    {
                        name = prof.TryGetProperty("name", out JsonElement n) ? (n.GetString() ?? "") : "";
                        mii = prof.TryGetProperty("mii", out JsonElement m) ? (m.GetString() ?? "") : "";
                    }
                }
                catch { /* no existing profile */ }

                if (string.IsNullOrEmpty(name))
                {
                    name = NextendoAccount.Username;
                }

                string image = Convert.ToBase64String(jpeg);
                string payload = $"{{\"name\":{JsonSerializer.Serialize(name)},\"image\":{JsonSerializer.Serialize(image)},\"mii\":{JsonSerializer.Serialize(mii)}}}";
                using StringContent body = new(payload, Encoding.UTF8, "application/json");
                HttpResponseMessage resp = await http.PutAsync($"{BaseUrl()}/api/profile", body);
                return resp.IsSuccessStatusCode;
            }
            catch (Exception ex)
            {
                Logger.Warning?.Print(LogClass.Application, $"[Nextendo] SetProfileImage failed: {ex.Message}");
                return false;
            }
        }

        private static Friend ParseFriend(JsonElement f)
        {
            Friend friend = new()
            {
                Pid = f.TryGetProperty("pid", out JsonElement p) ? p.GetUInt64() : 0,
                Username = f.TryGetProperty("username", out JsonElement u) ? (u.GetString() ?? "") : "",
                Name = f.TryGetProperty("name", out JsonElement n) ? (n.GetString() ?? "") : "",
                FriendCode = f.TryGetProperty("friend_code", out JsonElement c) ? (c.GetString() ?? "") : "",
                ImageBase64 = f.TryGetProperty("image", out JsonElement i) ? (i.GetString() ?? "") : "",
                Favorite = f.TryGetProperty("favorite", out JsonElement fav) && fav.ValueKind == JsonValueKind.True,
            };

            // The account server has always sent live presence here; the client just never read
            // it, so friends always looked offline in the emulator's own list.
            if (f.TryGetProperty("presence", out JsonElement pr) && pr.ValueKind == JsonValueKind.Object)
            {
                if (pr.TryGetProperty("status", out JsonElement st) && st.TryGetInt32(out int status))
                {
                    friend.OnlineStatus = status;
                }

                friend.AppId = pr.TryGetProperty("app_id", out JsonElement ai) ? (ai.GetString() ?? "") : "";
                friend.AppDetail = pr.TryGetProperty("app_detail", out JsonElement ad) ? (ad.GetString() ?? "") : "";
            }

            return friend;
        }
    }
}
