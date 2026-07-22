using Avalonia.Media;
using Ryujinx.Ava.Common;
using Ryujinx.Ava.Common.Locale;

namespace Ryujinx.Ava.UI.Models
{
    // [Nextendo] One entry in the friends list (Settings → Nextendo, and the standalone
    // friends window).
    public class NextendoFriendModel
    {
        public ulong Pid { get; init; }
        public string Name { get; init; } = "";
        public string FriendCode { get; init; } = "";
        public byte[] Image { get; init; }

        /// <summary>Live presence: 0 = offline, non-zero = playing.</summary>
        public int OnlineStatus { get; init; }

        /// <summary>Title id the friend is playing, empty when offline.</summary>
        public string AppId { get; init; } = "";

        /// <summary>In-game detail from the play report ("Single Player"); empty for most games.</summary>
        public string AppDetail { get; init; } = "";

        public bool IsOnline => OnlineStatus != 0;

        /// <summary>
        /// Green when online, dimmed grey when not — the dot on the avatar. Typed as a brush
        /// rather than a hex string: every other colour binding in the app is, and a string would
        /// depend on implicit conversion that fails silently into an invisible dot.
        /// </summary>
        public IBrush StatusColor => IsOnline ? OnlineBrush : OfflineBrush;

        private static readonly IBrush OnlineBrush = Brush.Parse("#33E86B");
        private static readonly IBrush OfflineBrush = Brush.Parse("#55808080");

        /// <summary>True when this friend is starred as a favorite (synced with the website).</summary>
        public bool Favorite { get; init; }

        /// <summary>Gold when favorited, dim grey otherwise — the star's colour.</summary>
        public IBrush FavoriteColor => Favorite ? FavoriteBrush : FavoriteDimBrush;

        private static readonly IBrush FavoriteBrush = Brush.Parse("#F5C518");
        private static readonly IBrush FavoriteDimBrush = Brush.Parse("#55808080");

        /// <summary>
        /// "Playing Splatoon 2" / "Online" / "Offline". Naming the game is the whole point of a
        /// friends list: it tells you whether it is worth inviting them right now.
        /// </summary>
        public string StatusText
        {
            get
            {
                if (!IsOnline)
                {
                    return LocaleManager.Instance[LocaleKeys.Dialog_Nextendo_FriendOffline];
                }

                string game = NextendoGameNames.Resolve(AppId);

                if (game is null)
                {
                    return LocaleManager.Instance[LocaleKeys.Dialog_Nextendo_FriendOnline];
                }

                // The detail only exists for the handful of games with a play-report spec; the
                // rest just get their name, which is the honest thing to show.
                return string.IsNullOrEmpty(AppDetail)
                    ? LocaleManager.Instance.UpdateAndGetDynamicValue(
                        LocaleKeys.Dialog_Nextendo_FriendPlayingFormat, game)
                    : LocaleManager.Instance.UpdateAndGetDynamicValue(
                        LocaleKeys.Dialog_Nextendo_FriendPlayingDetailFormat, game, AppDetail);
            }
        }
    }
}
