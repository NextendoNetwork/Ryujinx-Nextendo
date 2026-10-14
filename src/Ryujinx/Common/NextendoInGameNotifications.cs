using Avalonia.Threading;
using Ryujinx.Ava.Common.Locale;
using Ryujinx.Ava.UI.Models;
using Ryujinx.Common;
using Ryujinx.Common.Logging;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Ryujinx.Ava.Common
{
    /// <summary>
    /// [Nextendo] Switch-style in-game toast notifications. Active ONLY while a game is running, and
    /// only for events that happen AFTER launch: the friend requests and friend game-launches that
    /// already exist when the game starts are the baseline and are never announced (so launching with
    /// 10 pending requests fires 0 toasts). New requests, and friends who start a game DURING play,
    /// each pop a toast. Stops and clears when the game exits.
    /// </summary>
    public static class NextendoInGameNotifications
    {
        private const int MaxToasts = 3;
        private static readonly TimeSpan _toastDuration = TimeSpan.FromSeconds(6);
        private static readonly TimeSpan _inviteToastDuration = TimeSpan.FromSeconds(20); // still listed in Friends afterwards
        private static readonly TimeSpan _pollInterval = TimeSpan.FromSeconds(15);

        /// <summary>The toasts on screen, bound by the overlay. Newest first (index 0 = top).</summary>
        public static readonly ObservableCollection<NextendoToastModel> Toasts = [];

        private static readonly object _lock = new();
        private static int _failedPolls;
        private static bool _serverLost;
        private static Timer _timer;
        private static bool _active;
        private static long _nextId;

        // Baseline captured at game launch, rolled forward each poll.
        private static HashSet<ulong> _knownRequests = [];
        // Friends online at the last poll (the HOME Menu says so when one comes online).
        private static HashSet<ulong> _knownOnline = [];
        // When each friend was last seen online (the console tells a friend coming back after
        // more than 30 minutes away, not one that flickers).
        private static Dictionary<ulong, DateTime> _lastSeenOnline = [];
        private static bool _summaryShown;
        // pid -> the game we last ANNOUNCED for that friend. Only a change to a different game (or a
        // first game after coming online) fires; a presence flicker to "online, no game" does NOT
        // reset it, so a friend staying in one game is announced exactly once.
        private static Dictionary<ulong, string> _announcedGame = [];

        /// <summary>Subscribe to game start/stop. Call once at startup.</summary>
        public static void Initialize()
        {
            _timer = new Timer(_ => _ = PollAsync(), null, Timeout.Infinite, Timeout.Infinite);

            TitleIDs.CurrentApplication.Event += (_, e) =>
            {
                bool inGame = e.NewValue.TryGet(out string tid) && !string.IsNullOrEmpty(tid);
                if (inGame)
                {
                    Start();
                }
                else
                {
                    Stop();
                }
            };
        }

        private static void Start()
        {
            lock (_lock)
            {
                if (_active)
                {
                    return;
                }

                _active = true;
            }

            // Capture the baseline BEFORE arming the poll, so nothing that predates launch is announced.
            _ = PrimeThenArm();
        }

        private static void Stop()
        {
            lock (_lock)
            {
                _active = false;
                _timer?.Change(Timeout.Infinite, Timeout.Infinite);
            }

            Dispatcher.UIThread.Post(() => Toasts.Clear());
        }

        private static async Task PrimeThenArm()
        {
            await PrimeAsync();

            lock (_lock)
            {
                if (_active)
                {
                    _timer.Change(_pollInterval, _pollInterval);
                }
            }
        }

        private static async Task PrimeAsync()
        {
            try
            {
                (List<NextendoApi.Friend> friends, List<NextendoApi.Friend> requests) = await NextendoApi.GetSocialAsync();

                lock (_lock)
                {
                    _knownRequests = requests.Select(r => r.Pid).ToHashSet();
                    _knownOnline = friends.Where(f => f.OnlineStatus != 0).Select(f => f.Pid).ToHashSet();

                    foreach (ulong pid in _knownOnline)
                    {
                        _lastSeenOnline[pid] = DateTime.UtcNow;
                    }
                    _announcedGame = friends
                        .Where(f => f.OnlineStatus != 0 && !string.IsNullOrEmpty(f.AppId))
                        .GroupBy(f => f.Pid)
                        .ToDictionary(g => g.Key, g => GameKey(g.First()));
                }

                Logger.Info?.Print(LogClass.Application,
                    $"[Nextendo][notif] in-game start — baseline {_knownRequests.Count} request(s), {friends.Count} friend(s), {_announcedGame.Count} in a game");
            }
            catch (Exception ex)
            {
                Logger.Info?.Print(LogClass.Application, $"[Nextendo][notif] prime failed: {ex.Message}");
            }
        }

        private static async Task PollAsync()
        {
            if (!_active || !NextendoNotificationSettings.Enabled)
            {
                return;
            }

            try
            {
                (List<NextendoApi.Friend> friends, List<NextendoApi.Friend> requests) = await NextendoApi.GetSocialAsync();

                // [Nextendo] The server unreachable for 45 seconds, then back: said once each.
                if (!NextendoApi.LastSocialReachable)
                {
                    if (++_failedPolls == 3)
                    {
                        _serverLost = true;
                        NextendoEventNotifications.ServerConnection(false);
                    }

                    return;
                }

                _failedPolls = 0;

                if (_serverLost)
                {
                    _serverLost = false;
                    NextendoEventNotifications.ServerConnection(true);
                }

                List<NextendoToastModel> fresh = new();

                // [Nextendo, HOME Menu work] On the HOME Menu, the console's own notifications
                // (drawn by its overlay applet) say it: a friend request, friends coming online.
                bool console = Ryujinx.HLE.HOS.HomeMenuStatus.Running;
                bool newRequest = false;
                int online = 0;
                List<ulong> toAnnounce = new();

                lock (_lock)
                {
                    if (!_active)
                    {
                        return;
                    }

                    HashSet<ulong> nowOnline = friends.Where(f => f.OnlineStatus != 0).Select(f => f.Pid).ToHashSet();
                    DateTime seenAt = DateTime.UtcNow;

                    foreach (ulong pid in nowOnline)
                    {
                        if (!_knownOnline.Contains(pid) &&
                            (!_lastSeenOnline.TryGetValue(pid, out DateTime seen) || seenAt - seen > TimeSpan.FromMinutes(30)))
                        {
                            toAnnounce.Add(pid);
                        }

                        _lastSeenOnline[pid] = seenAt;
                    }

                    online = nowOnline.Count;
                    _knownOnline = nowOnline;
                    newRequest = requests.Any(r => !_knownRequests.Contains(r.Pid));

                    // New friend requests since the baseline.
                    foreach (NextendoApi.Friend r in requests)
                    {
                        if (!_knownRequests.Contains(r.Pid) && !console)
                        {
                            fresh.Add(Build(r,
                                LocaleManager.Instance[LocaleKeys.Dialog_Nextendo_NotifFriendRequestTitle],
                                LocaleManager.Instance.UpdateAndGetDynamicValue(
                                    LocaleKeys.Dialog_Nextendo_NotifFriendRequestFormat, NameOf(r)),
                                opensInvites: true,
                                friendRequestPid: r.Pid));
                        }
                    }
                    _knownRequests = requests.Select(r => r.Pid).ToHashSet();

                    // Friends who STARTED (or switched to) a game. A flicker to "online, no game"
                    // does NOT reset the announced game, so we never re-announce the same launch.
                    foreach (NextendoApi.Friend f in friends)
                    {
                        if (f.OnlineStatus == 0)
                        {
                            // Offline: skip, but KEEP the announced game. Presence flickers offline for
                            // a poll or two, and removing it here made the friend re-announce on the
                            // next poll — the "same person 4×" bug. A friend staying in one game is
                            // announced exactly once for the whole session.
                            continue;
                        }

                        string now = GameKey(f);
                        if (string.IsNullOrEmpty(now))
                        {
                            continue; // online but in a menu — not a launch
                        }

                        _announcedGame.TryGetValue(f.Pid, out string announced);
                        if (now != announced && console)
                        {
                            // On the HOME Menu the console says it (friend online, with the game).
                            if (!toAnnounce.Contains(f.Pid))
                            {
                                toAnnounce.Add(f.Pid);
                            }

                            _announcedGame[f.Pid] = now;
                        }
                        else if (now != announced)
                        {
                            fresh.Add(Build(f,
                                LocaleManager.Instance[LocaleKeys.Dialog_Nextendo_NotifPlayingTitle],
                                LocaleManager.Instance.UpdateAndGetDynamicValue(
                                    LocaleKeys.Dialog_Nextendo_NotifPlayingFormat, NameOf(f), GameNameOf(f))));
                            _announcedGame[f.Pid] = now;
                        }
                    }
                }

                if (console)
                {
                    if (newRequest)
                    {
                        NextendoSystemNotifications.FriendRequestReceived();
                    }

                    // How many are online, once (as the console says it when it starts).
                    if (!_summaryShown && online > 0)
                    {
                        _summaryShown = true;
                        NextendoSystemNotifications.FriendsOnline(online);
                    }

                    foreach (ulong pid in toAnnounce)
                    {
                        NextendoSystemNotifications.FriendOnline(pid);
                    }
                }

                Logger.Debug?.Print(LogClass.Application,
                    $"[Nextendo][notif] poll — {requests.Count} request(s), {friends.Count} friend(s) → {fresh.Count} new toast(s)");

                if (fresh.Count > 0)
                {
                    await Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        foreach (NextendoToastModel t in fresh)
                        {
                            Push(t);
                        }
                    });
                }
            }
            catch (Exception ex)
            {
                Logger.Info?.Print(LogClass.Application, $"[Nextendo][notif] poll failed: {ex.Message}");
            }
        }

        // A region- and update-tolerant identity for the friend's current game, so a presence flicker
        // between Splatoon 2's regional title ids doesn't read as a new launch. "" when not in a game.
        private static string GameKey(NextendoApi.Friend f)
        {
            if (f.OnlineStatus == 0)
            {
                return "";
            }

            string appId = f.AppId ?? "";
            if (appId.Length == 0)
            {
                return "";
            }

            string baseId = ulong.TryParse(appId, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out ulong id)
                ? (id & ~0x1FFFUL).ToString("x16")
                : appId;

            // Prefer the resolved game name (folds Splatoon 2 EU/US/JP into one), NORMALISED. The same
            // game can resolve to two spellings depending on the source: the compatibility list returns
            // "Splatoon™ 2" while our own fallback returns "Splatoon 2", and a friend's presence flickers
            // between the game server's title id and their emulator's — one path per poll. Comparing the
            // raw name would read that as a launch every flip (the "same person 4×" bug), so the dedup
            // key is stripped to letters/digits ("splatoon2"). Unknown games fall back to the base id.
            string name = NextendoGameNames.Resolve(baseId) ?? NextendoGameNames.Resolve(appId);
            return name != null ? Canon(name) : baseId;
        }

        // Fold a display name to a stable comparison key: letters and digits only, lower-cased. So
        // "Splatoon™ 2" and "Splatoon 2" both become "splatoon2".
        private static string Canon(string name)
        {
            StringBuilder sb = new(name.Length);
            foreach (char c in name)
            {
                if (char.IsLetterOrDigit(c))
                {
                    sb.Append(char.ToLowerInvariant(c));
                }
            }

            return sb.ToString();
        }

        private static string NameOf(NextendoApi.Friend f)
            => string.IsNullOrEmpty(f.Name) ? f.FriendCode : f.Name;

        // Display name for the toast: drop trademark/copyright marks so the same game always reads the
        // same way ("Splatoon 2", never "Splatoon™ 2" for one friend and "Splatoon 2" for another).
        private static string GameNameOf(NextendoApi.Friend f)
        {
            string name = NextendoGameNames.Resolve(f.AppId);
            if (string.IsNullOrEmpty(name))
            {
                return LocaleManager.Instance[LocaleKeys.Dialog_Nextendo_NotifAGame];
            }

            return name.Replace("™", "").Replace("®", "").Replace("©", "")
                       .Replace("  ", " ").Trim();
        }

        private static NextendoToastModel Build(
            NextendoApi.Friend f,
            string title,
            string text,
            bool opensInvites = false,
            ulong? friendRequestPid = null)
        {
            byte[] img = null;
            if (!string.IsNullOrEmpty(f.ImageBase64))
            {
                try { img = Convert.FromBase64String(f.ImageBase64); } catch { /* ignore */ }
            }

            return new NextendoToastModel
            {
                Id = NextId(),
                Image = img,
                Title = title,
                Text = text,
                OpensInvites = opensInvites,
                FriendRequestPid = friendRequestPid,
            };
        }

        /// <summary>UI thread: a plain message toast with no avatar.</summary>
        public static void PushNotice(string title, string text) =>
            Push(new NextendoToastModel { Id = NextId(), Title = title, Text = text });

        /// <summary>UI thread: a game invitation toast, kept longer than the others.</summary>
        public static void PushInvite(NextendoToastModel toast) => Push(toast, _inviteToastDuration);

        /// <summary>UI thread: removes a toast before it expires (an answered invitation).</summary>
        public static void Dismiss(long id)
        {
            NextendoToastModel present = Toasts.FirstOrDefault(t => t.Id == id);
            if (present != null)
            {
                Toasts.Remove(present);
            }
        }

        public static long NextId() => Interlocked.Increment(ref _nextId);

        // UI thread: newest on top, cap at 3 (drop the oldest), auto-expire after a few seconds.
        private static void Push(NextendoToastModel toast) =>
            Push(toast, toast.IsFriendRequest ? _inviteToastDuration : _toastDuration);

        private static void Push(NextendoToastModel toast, TimeSpan duration)
        {
            // [Nextendo, HOME Menu work] In the HOME Menu, the notifications are the system's own.
            if (Ryujinx.HLE.HOS.HomeMenuStatus.Running)
            {
                return;
            }

            Toasts.Insert(0, toast);

            while (Toasts.Count > MaxToasts)
            {
                Toasts.RemoveAt(Toasts.Count - 1);
            }

            DispatcherTimer.RunOnce(() => Dismiss(toast.Id), duration);
        }
    }
}
