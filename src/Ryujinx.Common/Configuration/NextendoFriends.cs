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
            // The friend's NSA id (nn::friends NetworkServiceAccountId). Splatoon 3 gets its friend
            // list from NPLN, which names each friend by nsa_id, then asks the console's LOCAL
            // friend: service for their details BY THAT SAME nsa_id. Keyed only by Pid we could
            // resolve none of them, and the in-game "Amis" screen stayed empty.
            public readonly ulong Nsa;
            // Statut de presence rapporte par le serveur de comptes (0 = hors ligne). Sans lui on
            // annoncait TOUS les amis « en train de jouer », ce qui est faux et pousse le jeu a
            // proposer de rejoindre des parties qui n'existent pas.
            public readonly int Status;
            // The friend's live S2 presence "AppField" (nn::friends AppKeyValueStorage, up to
            // 0xC0 bytes) relayed by the account server — encodes SessionId/Full/etc. so S2 sees
            // the friend as in a JOINABLE private battle. Empty when the friend isn't in a room.
            public readonly byte[] AppField;

            // What the HOME Menu's friend page shows: the game being played (title id), the
            // platform it is played on, whether the friend is a favourite, and since when
            // (Unix seconds) the two accounts are friends.
            public ulong AppId { get; init; }
            public string Game { get; init; }
            // What the friend is doing in that game, from its play report ("Offline · VS · 150cc"),
            // as Discord shows it; empty for most games.
            public string Detail { get; init; }
            public string Platform { get; init; }
            public bool Favorite { get; init; }
            public long Since { get; init; }
            // When the friend was last seen (Unix seconds, 0 when the server does not say): the
            // HOME Menu's "last online" of a friend who is offline.
            public long LastSeen { get; init; }
            public string FriendCode { get; init; }
            // [Nextendo, HOME Menu work] What the friend's page shows of a Nextendo profile: the
            // badges (as the account server names them: "donor", "booster_3", "team"...) and the
            // colour chosen for the profile ("#rrggbb", empty when none).
            public string[] Badges { get; init; }
            public string Color { get; init; }
            // The banner of the profile: where its picture is (a path of the account server, or
            // an address at KLIPY for a GIF picked there) and the frame chosen in it (x, y,
            // width, height, as fractions of the picture). Empty when the profile has none.
            public string BannerUrl { get; init; }
            // The Discord avatar decoration of a staff member or a donor (address of its picture
            // at Discord), or "": drawn over the friend's picture in the HOME Menu.
            public string Decoration { get; init; }
            public float[] BannerCrop { get; init; }
            public long RequestedAt { get; init; }
            public bool HasPicture { get; init; }

            public Entry(ulong pid, string name, byte[] appField, ulong nsa = 0, int status = 0)
            {
                Pid = pid;
                Name = name;
                AppField = appField;
                Nsa = nsa;
                Status = status;
            }
        }

        private static readonly object _lock = new();
        private static List<Entry> _cache = new();

        // [Nextendo, HOME Menu work] The friend requests of the account: received and sent.
        private static List<Entry> _requests = new();
        private static List<Entry> _sent = new();

        /// <summary>The friend requests received, as of the last reading of the friend list.</summary>
        public static IReadOnlyList<Entry> Requests()
        {
            Get();

            lock (_lock)
            {
                return _requests;
            }
        }

        /// <summary>The friend requests sent and not answered yet.</summary>
        public static IReadOnlyList<Entry> Sent()
        {
            Get();

            lock (_lock)
            {
                return _sent;
            }
        }

        private static List<Entry> ParseRequests(JsonElement root, string property)
        {
            List<Entry> found = new();

            if (!root.TryGetProperty(property, out JsonElement arr) || arr.ValueKind != JsonValueKind.Array)
            {
                return found;
            }

            foreach (JsonElement r in arr.EnumerateArray())
            {
                ulong pid = r.TryGetProperty("pid", out JsonElement p) && p.ValueKind == JsonValueKind.Number ? p.GetUInt64() : 0;
                string name = r.TryGetProperty("name", out JsonElement n) ? (n.GetString() ?? "") : "";
                ulong nsa = 0;

                if (r.TryGetProperty("nsa", out JsonElement nsaEl) && nsaEl.ValueKind == JsonValueKind.String)
                {
                    _ = ulong.TryParse(nsaEl.GetString(), System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture, out nsa);
                }

                long at = 0;

                // The date of the request ("requested_at", kept by the server since 10/10/2026); for
                // older requests, the "created_at" of the account it comes from, what a Switch shows
                // for them. "vu" is the last time the player was seen, not the request.
                if ((r.TryGetProperty("requested_at", out JsonElement atEl) || r.TryGetProperty("created_at", out atEl)) && atEl.ValueKind == JsonValueKind.String &&
                    DateTimeOffset.TryParse(atEl.GetString(), System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AssumeUniversal, out DateTimeOffset when))
                {
                    at = when.ToUnixTimeSeconds();
                }

                if (pid != 0)
                {
                    found.Add(new Entry(pid, string.IsNullOrEmpty(name) && r.TryGetProperty("username", out JsonElement u) ? u.GetString() ?? "" : name, null, nsa)
                    {
                        FriendCode = r.TryGetProperty("friend_code", out JsonElement fc) ? fc.GetString() ?? "" : "",
                        RequestedAt = at,
                        HasPicture = r.TryGetProperty("image", out JsonElement img) && img.ValueKind == JsonValueKind.String && img.GetString().Length > 0,
                    });
                }
            }

            return found;
        }

        /// <summary>Accepts or declines a received friend request; the list is read again after.</summary>
        public static bool Answer(ulong pid, bool accept)
        {
            return Post(accept ? "/api/friends/accept" : "/api/friends/decline", $"{{\"pid\":{pid}}}");
        }

        /// <summary>Sends a friend request to the owner of a friend code ("SW-xxxx-xxxx-xxxx").</summary>
        public static bool SendRequest(string friendCode)
        {
            return Post("/api/friends", $"{{\"friend_code\":{JsonSerializer.Serialize(friendCode)}}}");
        }

        /// <summary>Cancels a friend request sent.</summary>
        public static bool CancelRequest(ulong pid)
        {
            return Post("/api/friends/remove", $"{{\"pid\":{pid}}}");
        }

        private static bool Post(string path, string json)
        {
            string baseUrl = NextendoEndpoint.BaseUrl();

            if (string.IsNullOrEmpty(NextendoAccount.NexToken) || string.IsNullOrEmpty(baseUrl))
            {
                return false;
            }

            try
            {
                using HttpRequestMessage req = new(HttpMethod.Post, baseUrl.TrimEnd('/') + path)
                {
                    Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json"),
                };
                req.Headers.Add("Authorization", "Bearer " + NextendoAccount.NexToken);

                using HttpResponseMessage resp = _http.SendAsync(req).GetAwaiter().GetResult();

                // The lists change: read again at the next look.
                lock (_lock)
                {
                    _lastFetchMs = 0;
                }

                return resp.IsSuccessStatusCode;
            }
            catch
            {
                return false;
            }
        }
        private static long _lastFetchMs;
        private static int _refreshing;

        /// <summary>
        /// [Nextendo] Liste d'amis GARANTIE chaude, avec attente BORNEE.
        ///
        /// Get() ne bloque jamais et remplit en tache de fond — indispensable pour les jeux NEX, qui
        /// interrogent en boucle depuis le fil online : un blocage la-bas coupe les acquittements
        /// PRUDP et le serveur declare l'erreur de communication. Mais Splatoon 3, lui, demande sa
        /// liste UNE SEULE FOIS, tot (mesure : a 39 s de boot, cache encore vide) et repart avec
        /// zero ami sans jamais redemander. Pour ce cas precis on accepte une attente COURTE et
        /// plafonnee, jamais sur le chemin des jeux NEX.
        /// </summary>
        public static IReadOnlyList<Entry> GetWarm(int timeoutMs)
        {
            IReadOnlyList<Entry> list = Get(); // declenche le rafraichissement de fond si besoin
            if (list.Count > 0 || !NextendoAccount.IsLinked)
            {
                return list;
            }

            long deadline = Environment.TickCount64 + timeoutMs;
            while (Environment.TickCount64 < deadline)
            {
                System.Threading.Thread.Sleep(50);
                list = Get();
                if (list.Count > 0)
                {
                    break;
                }
            }

            return list;
        }

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

        // [Nextendo] Photos de profil des amis, servies au service friend: LOCAL de la console
        // (GetFriendProfileImage, cmd 10110). Elles ne viennent pas de la liste d'amis — celle-ci ne
        // porte qu'une URL — mais de /api/avatar?pid=<pid>, qui rend un JPEG. On les recupere donc a
        // la demande, une seule fois par ami, et on garde le resultat (y compris l'echec, en tableau
        // vide) pour ne jamais re-solliciter le serveur sur le fil d'appel du jeu.
        private static readonly object _imgLock = new();
        private static readonly Dictionary<ulong, byte[]> _images = new();
        private static readonly HashSet<ulong> _imgFetching = new();

        /// <summary>
        /// Photo de profil d'un ami (JPEG), ou null tant qu'elle n'est pas arrivee. Ne bloque
        /// jamais : la recuperation part en tache de fond, comme la liste d'amis.
        /// </summary>
        public static byte[] ProfileImage(ulong pid)
        {
            if (pid == 0)
            {
                return null;
            }

            lock (_imgLock)
            {
                if (_images.TryGetValue(pid, out byte[] cached))
                {
                    return cached.Length > 0 ? cached : null;
                }

                if (!_imgFetching.Add(pid))
                {
                    return null; // deja en cours
                }
            }

            System.Threading.Tasks.Task.Run(() =>
            {
                byte[] data = System.Array.Empty<byte>();
                try
                {
                    string baseUrl = NextendoEndpoint.BaseUrl().TrimEnd('/');
                    using HttpRequestMessage req = new(HttpMethod.Get, baseUrl + "/api/avatar?pid=" + pid);
                    using HttpResponseMessage resp = _http.SendAsync(req).GetAwaiter().GetResult();
                    if (resp.IsSuccessStatusCode)
                    {
                        byte[] body = resp.Content.ReadAsByteArrayAsync().GetAwaiter().GetResult();
                        // La console attend un JPEG : on ne sert que ce qui en est un (SOI ff d8).
                        if (body.Length > 2 && body[0] == 0xFF && body[1] == 0xD8)
                        {
                            data = NextendoImages.Fit(body, 256);
                        }
                    }
                    else if (resp.StatusCode == System.Net.HttpStatusCode.NotFound)
                    {
                        // No picture chosen: the default avatar, in this player's tint.
                        data = NextendoImages.Default(pid, 256) ?? data;
                    }

                    byte[] decorated = Decorated(pid, data);

                    // [Nextendo] without a decoration: made round by the emulator (the friend
                    // list has no round mask, for the decorations to go past the circle)
                    data = ReferenceEquals(decorated, data) && data.Length > 0 && NextendoImages.MarkRound != null && !NextendoHomeMenuVanilla.Enabled
                        ? NextendoImages.MarkRound(data) ?? data
                        : decorated;
                }
                catch
                {
                    // reseau indisponible : on retient l'echec (tableau vide) plutot que de boucler
                }

                lock (_imgLock)
                {
                    _images[pid] = data;
                    _imgFetching.Remove(pid);
                }
            });

            return null;
        }

        // [Nextendo] The friends' pictures without their decoration (the page of a friend draws
        // the decoration on its own, moving and past the edge of the picture).
        private static readonly System.Collections.Concurrent.ConcurrentDictionary<ulong, byte[]> _plainImages = new();

        /// <summary>The picture of a friend without its decoration (JPEG), or null while it has not come.</summary>
        public static byte[] PlainImage(ulong pid) => _plainImages.TryGetValue(pid, out byte[] picture) ? picture : null;

        // [Nextendo] The friend's Discord avatar decoration over their picture, when they have one
        // (staff and donors: the server only gives it to them). The still picture of the decoration.
        private static byte[] Decorated(ulong pid, byte[] picture)
        {
            if (picture == null || picture.Length == 0 || NextendoImages.Decorate == null || NextendoHomeMenuVanilla.Enabled)
            {
                return picture;
            }

            _plainImages[pid] = picture;

            string address = "";

            foreach (Entry friend in Get())
            {
                if (friend.Pid == pid)
                {
                    address = friend.Decoration ?? "";
                    break;
                }
            }

            if (!Uri.TryCreate(address, UriKind.Absolute, out Uri uri) || uri.Scheme != Uri.UriSchemeHttps || uri.Host != "cdn.discordapp.com" ||
                !uri.AbsolutePath.StartsWith("/avatar-decoration-presets/", StringComparison.Ordinal))
            {
                return picture;
            }

            try
            {
                string still = uri.AbsoluteUri.Replace("passthrough=true", "passthrough=false");
                using HttpRequestMessage req = new(HttpMethod.Get, still);
                using HttpResponseMessage resp = _http.SendAsync(req).GetAwaiter().GetResult();

                if (!resp.IsSuccessStatusCode)
                {
                    return picture;
                }

                byte[] decoration = resp.Content.ReadAsByteArrayAsync().GetAwaiter().GetResult();

                return NextendoImages.Decorate(pid, picture, decoration) ?? picture;
            }
            catch (Exception)
            {
                return picture; // the picture without its decoration
            }
        }

        /// <summary>Photo de profil avec attente BORNEE — meme raison que GetWarm.</summary>
        public static byte[] ProfileImageWarm(ulong pid, int timeoutMs)
        {
            byte[] img = ProfileImage(pid);
            if (img != null)
            {
                return img;
            }

            long deadline = Environment.TickCount64 + timeoutMs;
            while (Environment.TickCount64 < deadline)
            {
                System.Threading.Thread.Sleep(50);
                img = ProfileImage(pid);
                if (img != null)
                {
                    return img;
                }

                lock (_imgLock)
                {
                    if (_images.ContainsKey(pid))
                    {
                        return null; // resolu en echec, inutile d'attendre plus
                    }
                }
            }

            return null;
        }

        private static readonly object _nameLock = new();
        private static readonly Dictionary<ulong, string> _names = new();
        private static readonly HashSet<ulong> _nameFetching = new();

        /// <summary>
        /// Public display name of any player, friend or not (/api/names), cached like ProfileImage;
        /// waits at most timeoutMs for a first lookup. Null when unknown.
        /// </summary>
        public static string PublicName(ulong pid, int timeoutMs)
        {
            if (pid == 0)
            {
                return null;
            }

            lock (_nameLock)
            {
                if (_names.TryGetValue(pid, out string cached))
                {
                    return cached.Length > 0 ? cached : null;
                }

                if (_nameFetching.Add(pid))
                {
                    System.Threading.Tasks.Task.Run(() => FetchName(pid));
                }
            }

            long deadline = Environment.TickCount64 + timeoutMs;
            while (Environment.TickCount64 < deadline)
            {
                System.Threading.Thread.Sleep(50);
                lock (_nameLock)
                {
                    if (_names.TryGetValue(pid, out string name))
                    {
                        return name.Length > 0 ? name : null;
                    }
                }
            }

            return null;
        }

        private static void FetchName(ulong pid)
        {
            string name = "";
            try
            {
                string baseUrl = NextendoEndpoint.BaseUrl().TrimEnd('/');
                using HttpRequestMessage req = new(HttpMethod.Get, baseUrl + "/api/names?pids=" + pid);
                using HttpResponseMessage resp = _http.SendAsync(req).GetAwaiter().GetResult();
                if (resp.IsSuccessStatusCode)
                {
                    using JsonDocument doc = JsonDocument.Parse(resp.Content.ReadAsStringAsync().GetAwaiter().GetResult());
                    if (doc.RootElement.TryGetProperty("names", out JsonElement names)
                        && names.TryGetProperty(pid.ToString(), out JsonElement entry)
                        && entry.TryGetProperty("name", out JsonElement n))
                    {
                        name = n.GetString() ?? "";
                    }
                }
            }
            catch
            {
                // network unavailable: remember the miss rather than retry on the game's thread
            }

            lock (_nameLock)
            {
                _names[pid] = name;
                _nameFetching.Remove(pid);
            }
        }

        /// <summary>
        /// [Nextendo, HOME Menu work] The banner in an account as the server describes it
        /// ("banner": {"url", "crop": [x, y, width, height]}): its address, or "" when there is none.
        /// </summary>
        public static string BannerOf(JsonElement account, out float[] crop)
        {
            crop = null;

            if (!account.TryGetProperty("banner", out JsonElement banner) || banner.ValueKind != JsonValueKind.Object ||
                !banner.TryGetProperty("url", out JsonElement url) || url.ValueKind != JsonValueKind.String ||
                !banner.TryGetProperty("crop", out JsonElement frame) || frame.ValueKind != JsonValueKind.Array || frame.GetArrayLength() != 4)
            {
                return "";
            }

            float[] values = new float[4];
            int i = 0;

            foreach (JsonElement value in frame.EnumerateArray())
            {
                if (value.ValueKind != JsonValueKind.Number)
                {
                    return "";
                }

                values[i++] = (float)value.GetDouble();
            }

            crop = values;

            return url.GetString() ?? "";
        }

        /// <summary>
        /// [Nextendo, HOME Menu work] The page of a friend opens in the HOME Menu (the friend's id):
        /// the emulator paints the banner and the badges of the page.
        /// </summary>
        public static event Action<ulong> FriendPageShown;

        public static void TellFriendPageShown(ulong pid) => FriendPageShown?.Invoke(pid);

        private static List<Played> _own = new();
        private static long _ownAt;
        private static int _ownAsking;

        /// <summary>
        /// The user's own play history, as last known: the profile page waits for the answer that
        /// carries it, so it is never asked to the server while the page waits. Asked behind when
        /// it is older than two minutes (and ahead, with the friend list).
        /// </summary>
        public static IReadOnlyList<Played> OwnHistory()
        {
            if ((_ownAt == 0 || Environment.TickCount64 - _ownAt > 120_000) && !string.IsNullOrEmpty(NextendoAccount.NexToken) &&
                System.Threading.Interlocked.Exchange(ref _ownAsking, 1) == 0)
            {
                System.Threading.Tasks.Task.Run(() =>
                {
                    try
                    {
                        List<Played> list = new(History(NextendoAccount.Pid, 8000, direct: true));

                        if (list.Count > 0 || _own.Count == 0)
                        {
                            _own = list;
                        }

                        _ownAt = Environment.TickCount64;
                    }
                    catch
                    {
                        // kept as it was
                    }
                    finally
                    {
                        System.Threading.Interlocked.Exchange(ref _ownAsking, 0);
                    }
                });
            }

            return _own;
        }

        private static void Refresh()
        {
            try
            {
                // what the pages of the user will ask, ahead of them
                OwnHistory();
                NextendoAccountLink.Prepare();

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
                        int status = 0;
                        if (f.TryGetProperty("presence", out JsonElement pres0) && pres0.ValueKind == JsonValueKind.Object
                            && pres0.TryGetProperty("status", out JsonElement st) && st.ValueKind == JsonValueKind.Number)
                        {
                            status = st.GetInt32();
                        }
                        if (f.TryGetProperty("presence", out JsonElement pres) && pres.ValueKind == JsonValueKind.Object
                            && pres.TryGetProperty("app_field", out JsonElement af) && af.ValueKind == JsonValueKind.String)
                        {
                            string b64 = af.GetString();
                            if (!string.IsNullOrEmpty(b64))
                            {
                                try { appField = Convert.FromBase64String(b64); } catch { appField = null; }
                            }
                        }
                        ulong nsa = 0;
                        if (f.TryGetProperty("nsa", out JsonElement nsaEl) && nsaEl.ValueKind == JsonValueKind.String)
                        {
                            _ = ulong.TryParse(nsaEl.GetString(), System.Globalization.NumberStyles.HexNumber,
                                System.Globalization.CultureInfo.InvariantCulture, out nsa);
                        }
                        ulong appId = 0;
                        string game = "";
                        string platform = "";
                        string detail = "";

                        if (f.TryGetProperty("presence", out JsonElement played) && played.ValueKind == JsonValueKind.Object)
                        {
                            if (played.TryGetProperty("app_id", out JsonElement idEl) && idEl.ValueKind == JsonValueKind.String)
                            {
                                _ = ulong.TryParse(idEl.GetString(), System.Globalization.NumberStyles.HexNumber,
                                    System.Globalization.CultureInfo.InvariantCulture, out appId);
                            }

                            game = played.TryGetProperty("jeu", out JsonElement gameEl) && gameEl.ValueKind == JsonValueKind.String ? gameEl.GetString() ?? "" : "";
                            platform = played.TryGetProperty("plateforme", out JsonElement pfEl) && pfEl.ValueKind == JsonValueKind.String ? pfEl.GetString() ?? "" : "";
                            detail = played.TryGetProperty("app_detail", out JsonElement detailEl) && detailEl.ValueKind == JsonValueKind.String ? detailEl.GetString() ?? "" : "";

                            byte[] icon = null;

                            if (played.TryGetProperty("icone", out JsonElement iconEl) && iconEl.ValueKind == JsonValueKind.String && !string.IsNullOrEmpty(iconEl.GetString()))
                            {
                                try { icon = Convert.FromBase64String(iconEl.GetString()); } catch { icon = null; }
                            }

                            // A game the server has no name for still gets its card: named by what the
                            // player is on (the HOME Menu of a console), else by its id; the icon is
                            // asked of the catalogue when the card is drawn.
                            string cardName = game;

                            if (string.IsNullOrEmpty(cardName) && appId != 0)
                            {
                                cardName = appId == NextendoGames.HomeMenuId ? "Menu HOME" : appId.ToString("X16");
                            }

                            if (!NextendoGames.Knows(appId) || !string.IsNullOrEmpty(game))
                            {
                                NextendoGames.Remember(appId, cardName, icon);
                            }
                        }

                        bool favorite = f.TryGetProperty("favorite", out JsonElement favEl) && favEl.ValueKind == JsonValueKind.True;
                        // When the two accounts became friends (Unix seconds). Only this field says
                        // it: "created_at" is the day the friend's account was opened.
                        long since = f.TryGetProperty("since", out JsonElement sinceEl) && sinceEl.ValueKind == JsonValueKind.Number ? sinceEl.GetInt64() : 0;

                        long lastSeen = f.TryGetProperty("vu", out JsonElement seenEl) && seenEl.ValueKind == JsonValueKind.String &&
                            DateTimeOffset.TryParse(seenEl.GetString(), System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AssumeUniversal, out DateTimeOffset seen)
                                ? seen.ToUnixTimeSeconds() : 0;

                        if (pid != 0)
                        {
                            list.Add(new Entry(pid, name, appField, nsa, status)
                            {
                                LastSeen = lastSeen,
                                AppId = appId,
                                Game = game,
                                Detail = detail,
                                Platform = platform,
                                Favorite = favorite,
                                Since = since,
                                Badges = f.TryGetProperty("badges", out JsonElement badgesEl) && badgesEl.ValueKind == JsonValueKind.Array
                                    ? [.. System.Linq.Enumerable.Where(System.Linq.Enumerable.Select(badgesEl.EnumerateArray(), b => b.GetString() ?? ""), b => b.Length > 0)]
                                    : [],
                                Color = f.TryGetProperty("color", out JsonElement colorEl) && colorEl.ValueKind == JsonValueKind.String ? colorEl.GetString() ?? "" : "",
                                BannerUrl = BannerOf(f, out float[] bannerCrop),
                                Decoration = f.TryGetProperty("avatar_decoration", out JsonElement decorationEl) && decorationEl.ValueKind == JsonValueKind.String ? decorationEl.GetString() ?? "" : "",
                                BannerCrop = bannerCrop,
                            });
                        }
                    }
                }

                List<Entry> requests = ParseRequests(doc.RootElement, "requests");
                List<Entry> sent = ParseRequests(doc.RootElement, "sent");

                lock (_lock)
                {
                    _cache = list;
                    _requests = requests;
                    _sent = sent;
                    _lastFetchMs = Environment.TickCount64;
                }
            }
            catch
            {
                // keep the stale cache on any failure
            }
        }

        /// <summary>One game of the play history of a friend.</summary>
        public readonly record struct Played(ulong TitleId, string Name, long Seconds, long LastPlayed);

        private static readonly Dictionary<ulong, (long At, List<Played> List)> _histories = new();

        /// <summary>
        /// The play history of a friend (/api/friends/history, friends only), most recent first,
        /// kept two minutes. Waits for the server at most <paramref name="timeoutMs"/>: the page
        /// that shows it asks once.
        /// </summary>
        private static readonly Queue<(long At, ulong Pid)> _historyAsks = new();
        private static (long At, Dictionary<ulong, List<Played>> ByFriend) _feed;
        private static readonly object _feedLock = new();

        /// <summary>
        /// What all friends played lately, in one request (/api/friends/feed): the answer to a
        /// page that asks the history of every friend in a row.
        /// </summary>
        private static Dictionary<ulong, List<Played>> Feed(int timeoutMs)
        {
            lock (_feedLock)
            {
                if (_feed.ByFriend != null && Environment.TickCount64 - _feed.At < 120_000)
                {
                    return _feed.ByFriend;
                }

                Dictionary<ulong, List<Played>> byFriend = new();

                try
                {
                    string baseUrl = NextendoEndpoint.BaseUrl().TrimEnd('/');

                    using System.Threading.CancellationTokenSource limit = new(timeoutMs);
                    using HttpRequestMessage req = new(HttpMethod.Get, baseUrl + "/api/friends/feed?limit=200");
                    req.Headers.Add("Authorization", "Bearer " + NextendoAccount.NexToken);
                    using HttpResponseMessage resp = _http.SendAsync(req, limit.Token).GetAwaiter().GetResult();

                    if (!resp.IsSuccessStatusCode)
                    {
                        return byFriend;
                    }

                    using JsonDocument doc = JsonDocument.Parse(resp.Content.ReadAsStringAsync(limit.Token).GetAwaiter().GetResult());

                    if (doc.RootElement.TryGetProperty("events", out JsonElement events) && events.ValueKind == JsonValueKind.Array)
                    {
                        foreach (JsonElement e in events.EnumerateArray())
                        {
                            ulong friend = e.TryGetProperty("pid", out JsonElement pidEl) && pidEl.ValueKind == JsonValueKind.Number ? pidEl.GetUInt64() : 0;
                            string idText = e.TryGetProperty("title_id", out JsonElement idEl) ? idEl.GetString() ?? "" : "";
                            string name = e.TryGetProperty("jeu", out JsonElement nameEl) ? nameEl.GetString() ?? "" : "";

                            if (friend == 0 || name.Length == 0 ||
                                !ulong.TryParse(idText, System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture, out ulong titleId) || titleId == 0)
                            {
                                continue;
                            }

                            long seconds = e.TryGetProperty("secondes", out JsonElement secEl) && secEl.ValueKind == JsonValueKind.Number ? secEl.GetInt64() : 0;
                            long last = 0;

                            if (e.TryGetProperty("quand", out JsonElement lastEl) && lastEl.ValueKind == JsonValueKind.String &&
                                DateTimeOffset.TryParse(lastEl.GetString(), System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AssumeUniversal, out DateTimeOffset when))
                            {
                                last = when.ToUnixTimeSeconds();
                            }

                            NextendoGames.Remember(titleId, name, null);

                            if (!byFriend.TryGetValue(friend, out List<Played> list))
                            {
                                byFriend[friend] = list = new List<Played>();
                            }

                            list.Add(new Played(titleId, name, seconds, last));
                        }
                    }
                }
                catch
                {
                    return byFriend; // out of reach: asked again next time
                }

                Ryujinx.Common.Logging.Logger.Info?.Print(Ryujinx.Common.Logging.LogClass.ServiceFriend, $"[Nextendo] activity feed: {byFriend.Count} friends with recent games");
                _feed = (Environment.TickCount64, byFriend);

                return byFriend;
            }
        }

        /// <summary>True while a page is asking the history of every friend in a row.</summary>
        public static bool Sweeping { get; private set; }

        /// <param name="direct">Asked for itself (a friend request: not in the friends' activity feed).</param>
        public static IReadOnlyList<Played> History(ulong pid, int timeoutMs, bool direct = false)
        {
            // A page that walks the whole friend list (three friends or more within three
            // seconds) is answered from the activity feed: one request for everybody instead
            // of one per friend.
            bool sweep;

            lock (_historyAsks)
            {
                long now = Environment.TickCount64;

                _historyAsks.Enqueue((now, pid));

                while (_historyAsks.Count > 0 && now - _historyAsks.Peek().At > 3000)
                {
                    _historyAsks.Dequeue();
                }

                HashSet<ulong> asked = new();

                foreach ((long _, ulong who) in _historyAsks)
                {
                    asked.Add(who);
                }

                sweep = asked.Count >= 3;
                Sweeping = sweep;
            }

            if (sweep && !direct && !string.IsNullOrEmpty(NextendoAccount.NexToken))
            {
                lock (_histories)
                {
                    if (_histories.TryGetValue(pid, out var known) && Environment.TickCount64 - known.At < 120_000)
                    {
                        return known.List;
                    }
                }

                return Feed(4000).TryGetValue(pid, out List<Played> recent) ? recent : new List<Played>();
            }

            lock (_histories)
            {
                if (_histories.TryGetValue(pid, out var known) && Environment.TickCount64 - known.At < 120_000)
                {
                    return known.List;
                }
            }

            List<Played> list = new();

            if (string.IsNullOrEmpty(NextendoAccount.NexToken))
            {
                return list;
            }

            try
            {
                string baseUrl = NextendoEndpoint.BaseUrl().TrimEnd('/');

                using System.Threading.CancellationTokenSource limit = new(timeoutMs);
                // The user's own history has its own address (the friends' one refuses it).
                using HttpRequestMessage req = new(HttpMethod.Get, baseUrl + (pid == NextendoAccount.Pid ? "/api/history" : "/api/friends/history?pid=" + pid));
                req.Headers.Add("Authorization", "Bearer " + NextendoAccount.NexToken);
                using HttpResponseMessage resp = _http.SendAsync(req, limit.Token).GetAwaiter().GetResult();

                if (!resp.IsSuccessStatusCode)
                {
                    return list;
                }

                using JsonDocument doc = JsonDocument.Parse(resp.Content.ReadAsStringAsync(limit.Token).GetAwaiter().GetResult());

                if (doc.RootElement.TryGetProperty("history", out JsonElement entries) && entries.ValueKind == JsonValueKind.Array)
                {
                    foreach (JsonElement e in entries.EnumerateArray())
                    {
                        string idText = e.TryGetProperty("title_id", out JsonElement idEl) ? idEl.GetString() ?? "" : "";

                        if (!ulong.TryParse(idText, System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture, out ulong titleId) || titleId == 0)
                        {
                            continue;
                        }

                        string name = e.TryGetProperty("name", out JsonElement nameEl) ? nameEl.GetString() ?? "" : "";
                        long seconds = e.TryGetProperty("seconds", out JsonElement secEl) && secEl.ValueKind == JsonValueKind.Number ? secEl.GetInt64() : 0;
                        long last = 0;

                        if (e.TryGetProperty("last_played", out JsonElement lastEl) && lastEl.ValueKind == JsonValueKind.String &&
                            DateTimeOffset.TryParse(lastEl.GetString(), System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AssumeUniversal, out DateTimeOffset when))
                        {
                            last = when.ToUnixTimeSeconds();
                        }

                        byte[] icon = null;

                        if (e.TryGetProperty("icon", out JsonElement iconEl) && iconEl.ValueKind == JsonValueKind.String && !string.IsNullOrEmpty(iconEl.GetString()))
                        {
                            try { icon = Convert.FromBase64String(iconEl.GetString()); } catch { icon = null; }
                        }

                        NextendoGames.Remember(titleId, name, icon);
                        list.Add(new Played(titleId, name, seconds, last));
                    }
                }

                list.Sort((a, b) => b.LastPlayed.CompareTo(a.LastPlayed));

                // Icons the history does not carry are fetched ahead, all at once, while the page opens.
                foreach (Played game in list.GetRange(0, Math.Min(20, list.Count)))
                {
                    ulong id = game.TitleId;
                    System.Threading.Tasks.Task.Run(() => NextendoGames.TryGet(id, out _));
                }
            }
            catch
            {
                return list; // server out of reach: nothing is kept, the next page asks again
            }

            lock (_histories)
            {
                _histories[pid] = (Environment.TickCount64, list);
            }

            return list;
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

            // The HOME Menu (or an applet of the system) is no game: online, playing nothing.
            if (string.Equals(appId, "0100000000001000", StringComparison.OrdinalIgnoreCase))
            {
                // HOME is an online activity, not an unspecified idle emulator.
                // Put the display label in the existing opaque field as well: older servers
                // already relay it even when they do not expose a client platform field.
                appId = "0100000000001000";
                appDetail = "HOME";
                appField = Encoding.UTF8.GetBytes("sys_description\0HOME Menu · Ryujinx\0");
                status = 1;
            }
            else if (NextendoGames.IsSystemProgram(appId))
            {
                appId = "";
                appDetail = "";
                status = 1;
            }
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
