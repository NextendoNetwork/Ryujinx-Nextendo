using Ryujinx.Common.Configuration;
using Ryujinx.Common;
using System.Linq;
using System.IO;
using Ryujinx.Common.Logging;
using System;
using System.Collections.Generic;
using System.Net.Http;
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
            string url = Environment.GetEnvironmentVariable("NEXTENDO_SITE");
            if (string.IsNullOrEmpty(url))
            {
                url = "https://nextendo.network";
            }

            return url.TrimEnd('/');
        }

        private static HttpClient Client()
        {
            HttpClient http = new() { Timeout = TimeSpan.FromSeconds(15) };
            if (!string.IsNullOrEmpty(NextendoAccount.NexToken))
            {
                http.DefaultRequestHeaders.Add("Authorization", "Bearer " + NextendoAccount.NexToken);
            }

            return http;
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
                await http.PostAsync($"{BaseUrl()}/api/nex-session", body);
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
