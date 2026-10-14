using Avalonia.Threading;
using Ryujinx.Ava.Common.Locale;
using Ryujinx.Ava.UI.Models;
using Ryujinx.Common;
using Ryujinx.Common.Configuration;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Ryujinx.Ava.Common
{
    /// <summary>
    /// [Nextendo] Incoming game invitations. Polled while a game runs; a new invite for that game pops a
    /// toast with Accept/Decline, and every pending invite stays in the Friends window until it is
    /// answered or expires (5 minutes, server-side).
    /// </summary>
    public static class NextendoGameInvites
    {
        private static readonly TimeSpan _pollInterval = TimeSpan.FromSeconds(10);

        private static readonly object _lock = new();
        private static Timer _timer;
        private static List<NextendoApi.GameInvitation> _invites = [];
        private static HashSet<string> _seen = [];
        private static bool _fetching;

        /// <summary>Raised on the UI thread whenever the pending list may have changed.</summary>
        public static event Action Changed;

        public static void Initialize()
        {
            // Outside a game only the Friends window asks, so idle clients don't poll.
            _timer = new Timer(_ =>
            {
                if (RunningTitleId() != 0)
                {
                    _ = RefreshAsync();
                }
            }, null, _pollInterval, _pollInterval);
        }

        /// <summary>Unexpired invites, newest first.</summary>
        public static List<NextendoApi.GameInvitation> Pending()
        {
            lock (_lock)
            {
                return _invites.Where(i => !i.IsExpired).ToList();
            }
        }

        public static async Task RefreshAsync()
        {
            if (!NextendoAccount.IsLinked)
            {
                return;
            }

            lock (_lock)
            {
                if (_fetching)
                {
                    return;
                }

                _fetching = true;
            }

            try
            {
                List<NextendoApi.GameInvitation> fetched = await NextendoApi.GetGameInvitationsAsync();
                if (fetched == null)
                {
                    return;
                }

                ulong running = RunningTitleId();
                List<NextendoApi.GameInvitation> fresh;
                lock (_lock)
                {
                    _invites = fetched.Where(i => !i.IsExpired).OrderByDescending(i => i.ExpiresAt).ToList();
                    fresh = NextendoNotificationSettings.Enabled
                        ? _invites.Where(i => !_seen.Contains(i.Id) && i.TitleId == running).ToList()
                        : [];
                    _seen = _invites.Select(i => i.Id).ToHashSet();
                }

                foreach (NextendoApi.GameInvitation invite in fresh)
                {
                    byte[] avatar = await NextendoApi.GetAvatarAsync(invite.SenderPid, "/api/avatar");
                    string text = LocaleManager.Instance.UpdateAndGetDynamicValue(
                        LocaleKeys.Dialog_Nextendo_GameInviteToastFormat, invite.SenderName, GameName(invite.TitleId));
                    await Dispatcher.UIThread.InvokeAsync(() => NextendoInGameNotifications.PushInvite(new NextendoToastModel
                    {
                        Id = NextendoInGameNotifications.NextId(),
                        Image = avatar,
                        Title = LocaleManager.Instance[LocaleKeys.Dialog_Nextendo_GameInviteToastTitle],
                        Text = text,
                        InviteId = invite.Id,
                        OpensInvites = true,
                    }));
                }

                Dispatcher.UIThread.Post(() => Changed?.Invoke());
            }
            finally
            {
                lock (_lock)
                {
                    _fetching = false;
                }
            }
        }

        /// <summary>UI thread. Null on success, otherwise why the invite can't be joined right now.</summary>
        public static string Accept(string id)
        {
            NextendoApi.GameInvitation invite;
            lock (_lock)
            {
                invite = _invites.FirstOrDefault(i => i.Id == id);
            }

            if (invite == null || invite.IsExpired)
            {
                Remove(id);
                return LocaleManager.Instance[LocaleKeys.Dialog_Nextendo_GameInviteExpired];
            }

            HLE.HOS.Horizon system = RyujinxApp.MainWindow?.ViewModel?.AppHost?.Device?.System;
            if (system == null || !system.PushFriendInvitation(invite.TitleId, invite.UserData))
            {
                return LocaleManager.Instance[LocaleKeys.Dialog_Nextendo_GameInviteStartGame];
            }

            Decline(id);
            return null;
        }

        /// <summary>UI thread. Dismisses the invite on the server and drops it locally.</summary>
        public static void Decline(string id)
        {
            _ = NextendoApi.DismissGameInvitationAsync(id);
            Remove(id);
        }

        public static string GameName(ulong titleId)
        {
            string name = NextendoGameNames.Resolve(titleId.ToString("x16"));
            return string.IsNullOrEmpty(name) ? LocaleManager.Instance[LocaleKeys.Dialog_Nextendo_NotifAGame] : name;
        }

        private static void Remove(string id)
        {
            bool removed;
            lock (_lock)
            {
                removed = _invites.RemoveAll(i => i.Id == id) > 0;
            }

            if (removed)
            {
                Changed?.Invoke();
            }
        }

        private static ulong RunningTitleId()
        {
            return TitleIDs.CurrentApplication.Value.TryGet(out string tid) &&
                   ulong.TryParse(tid, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out ulong id)
                ? id
                : 0;
        }
    }
}
