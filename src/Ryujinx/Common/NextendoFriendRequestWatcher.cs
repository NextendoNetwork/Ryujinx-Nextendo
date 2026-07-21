using Ryujinx.Ava.Common.Locale;
using Ryujinx.Ava.UI.Helpers;
using Ryujinx.Ava.UI.Windows;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Ryujinx.Ava.Common
{
    /// <summary>
    /// [Nextendo] Raises a notification when somebody sends a friend request, so an invite does not
    /// sit unseen until the player happens to open the friends window.
    ///
    /// Best-effort like the online counters: a failed poll changes nothing and stays quiet.
    /// </summary>
    public static class NextendoFriendRequestWatcher
    {
        private static readonly TimeSpan _pollInterval = TimeSpan.FromSeconds(45);
        private static readonly object _lock = new();

        // PIDs already announced. Only the transition unseen -> seen fires a notification, so a
        // request left pending does not re-notify on every poll for as long as it is pending.
        private static readonly HashSet<ulong> _announced = [];

        private static Timer _timer;
        private static bool _primed;

        /// <summary>Starts watching. Safe to call twice.</summary>
        public static void Start()
        {
            if (_timer != null)
            {
                return;
            }

            _timer = new Timer(_ => _ = PollAsync(), null, TimeSpan.FromSeconds(10), _pollInterval);
        }

        /// <summary>
        /// Lets the friends window tell the watcher what it just saw, so acting on a request there
        /// does not leave a stale entry that re-notifies later.
        /// </summary>
        public static void Sync(IEnumerable<ulong> pendingPids)
        {
            lock (_lock)
            {
                _announced.Clear();
                _announced.UnionWith(pendingPids);
                _primed = true;
            }
        }

        private static async Task PollAsync()
        {
            if (!NextendoAccountLinked())
            {
                return;
            }

            try
            {
                (_, List<NextendoApi.Friend> requests) = await NextendoApi.GetSocialAsync();

                List<NextendoApi.Friend> fresh;

                lock (_lock)
                {
                    // First successful poll only records what is already pending: requests that
                    // arrived while the emulator was closed are not news, and announcing a pile of
                    // them at every launch would train players to ignore the popup.
                    if (!_primed)
                    {
                        _primed = true;
                        _announced.UnionWith(requests.Select(r => r.Pid));

                        return;
                    }

                    fresh = requests.Where(r => !_announced.Contains(r.Pid)).ToList();

                    _announced.Clear();
                    _announced.UnionWith(requests.Select(r => r.Pid));
                }

                foreach (NextendoApi.Friend request in fresh)
                {
                    string name = string.IsNullOrEmpty(request.Name) ? request.FriendCode : request.Name;

                    NotificationHelper.ShowInformation(
                        LocaleManager.Instance[LocaleKeys.Dialog_Nextendo_FriendRequestNotifTitle],
                        LocaleManager.Instance.UpdateAndGetDynamicValue(
                            LocaleKeys.Dialog_Nextendo_FriendRequestNotifFormat, name),
                        onClick: NextendoFriendsWindow.Open);
                }
            }
            catch
            {
                // Offline, or the account server is down: nothing to announce, nothing to log.
            }
        }

        private static bool NextendoAccountLinked()
        {
            try
            {
                return Ryujinx.Common.Configuration.NextendoAccount.IsLinked;
            }
            catch
            {
                return false;
            }
        }
    }
}
