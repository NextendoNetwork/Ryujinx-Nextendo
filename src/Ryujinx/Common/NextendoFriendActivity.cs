using Avalonia.Threading;
using Ryujinx.Ava.Systems.AppLibrary;
using Ryujinx.Ava.UI.Models;
using Ryujinx.Common.Logging;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Ryujinx.Ava.Common
{
    /// <summary>
    /// [Nextendo] Discord-style "playing this game" avatars on each game row: the round overlapping
    /// pictures of the friends currently in that title, shown on the right of the row.
    ///
    /// Polls friend presence, groups the in-game friends by base title id, and pushes the matching
    /// (capped) avatar sets into each ApplicationData. Best-effort like NextendoOnlineCounts: a
    /// failed poll leaves the last avatars in place rather than clearing every row, and never blocks
    /// or logs noisily.
    /// </summary>
    public static class NextendoFriendActivity
    {
        private const int MaxAvatars = 5;
        private static readonly TimeSpan _pollInterval = TimeSpan.FromSeconds(15);
        private static Timer _timer;
        private static ApplicationLibrary _library;

        /// <summary>Starts polling presence and pushing avatars into the library. Safe to call twice.</summary>
        public static void Start(ApplicationLibrary library)
        {
            if (_timer != null)
            {
                return;
            }

            _library = library;
            _timer = new Timer(_ => _ = RefreshAsync(), null, TimeSpan.FromSeconds(3), _pollInterval);
        }

        public static async Task RefreshAsync()
        {
            try
            {
                (List<NextendoApi.Friend> friends, _) = await NextendoApi.GetSocialAsync();

                // Group the in-game friends by BASE title id (region/update tolerant). byGameAll keeps
                // everyone for the "Friends playing:" names row; byGame caps at 5 for the avatar row.
                Dictionary<string, List<NextendoFriendModel>> byGameAll = new();
                foreach (NextendoApi.Friend f in friends)
                {
                    if (f.OnlineStatus == 0 || string.IsNullOrEmpty(f.AppId))
                    {
                        continue;
                    }

                    string baseId = BaseId(f.AppId);
                    if (baseId == null)
                    {
                        continue;
                    }

                    string key = Canonical(baseId);
                    if (!byGameAll.TryGetValue(key, out List<NextendoFriendModel> list))
                    {
                        list = new List<NextendoFriendModel>();
                        byGameAll[key] = list;
                    }

                    byte[] img = null;
                    if (!string.IsNullOrEmpty(f.ImageBase64))
                    {
                        try { img = Convert.FromBase64String(f.ImageBase64); } catch { /* ignore */ }
                    }

                    list.Add(new NextendoFriendModel
                    {
                        Pid = f.Pid,
                        Name = f.Name,
                        Image = img,
                        OnlineStatus = f.OnlineStatus,
                        AppId = f.AppId,
                        AppDetail = f.AppDetail,
                    });
                }

                Dictionary<string, List<NextendoFriendModel>> byGame = new();
                foreach (KeyValuePair<string, List<NextendoFriendModel>> pair in byGameAll)
                {
                    byGame[pair.Key] = pair.Value.Take(MaxAvatars).ToList();
                }

                await Dispatcher.UIThread.InvokeAsync(() => Publish(byGame, byGameAll));
            }
            catch
            {
                // Offline or not logged in: keep the last known avatars rather than clearing the UI.
            }
        }

        // Base title id (update bits masked), lowercase 16-hex, matching ApplicationData.IdBaseString.
        private static string BaseId(string appId)
        {
            if (!ulong.TryParse(appId, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out ulong id))
            {
                return null;
            }

            return (id & ~0x1FFFUL).ToString("x16");
        }

        // Known multi-region title groups: every regional id folds to one canonical id, so a friend
        // on the US/JP build of a game shows on the row of the EU build (and vice-versa). Games not
        // listed fold to their own base id — the common case, since most titles have a single id.
        private static readonly Dictionary<string, string> _regionCanonical = new()
        {
            ["0100f8f0000a2000"] = "0100f8f0000a2000", // Splatoon 2 EU  -> EU
            ["01003bc0000a0000"] = "0100f8f0000a2000", // Splatoon 2 US  -> EU
            ["01003c700009c800"] = "0100f8f0000a2000", // Splatoon 2 JP  -> EU
        };

        private static string Canonical(string baseId)
            => baseId != null && _regionCanonical.TryGetValue(baseId, out string c) ? c : baseId;

        // Runs on the UI thread: mutates the per-app ObservableCollections the game rows bind to.
        private static void Publish(Dictionary<string, List<NextendoFriendModel>> byGame,
                                    Dictionary<string, List<NextendoFriendModel>> byGameAll)
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
                    byGame.TryGetValue(Canonical(app.IdBaseString), out List<NextendoFriendModel> list);
                    byGameAll.TryGetValue(Canonical(app.IdBaseString), out List<NextendoFriendModel> all);
                    app.SetFriendsInGame(list);
                    app.SetAllFriendsInGame(all);
                }
            }
            catch (Exception ex)
            {
                Logger.Debug?.Print(LogClass.Application, $"[Nextendo] friend-activity publish failed: {ex.Message}");
            }
        }
    }
}
