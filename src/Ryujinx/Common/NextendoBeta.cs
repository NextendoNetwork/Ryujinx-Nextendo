using Ryujinx.Ava.Common.Locale;
using Ryujinx.Common;
using System;
using System.Threading.Tasks;

namespace Ryujinx.Ava.Common
{
    /// <summary>
    /// [Nextendo] Remote beta gate — the kill-switch + forced-update control. Polls
    /// <c>/api/beta-config</c> and FAILS CLOSED: until a successful response says otherwise,
    /// online play is blocked. The operator disables online for everyone (online_enabled=false)
    /// or forces a minimum client version (min_app_version) by editing the server config alone;
    /// no client update is needed to flip the switch.
    /// </summary>
    public static class NextendoBeta
    {
        public enum BlockReason
        {
            None,
            Unreachable,
            Disabled,
            UpdateRequired,
        }

        private static readonly object _lock = new();
        private static NextendoApi.BetaConfig _cfg = new();
        private static bool _reachable;
        private static bool _polled;

        /// <summary>True once at least one poll (success or failure) has completed. The launch gate
        /// awaits an initial poll before evaluating so the first online launch isn't falsely blocked.</summary>
        public static bool HasPolled
        {
            get { lock (_lock) { return _polled; } }
        }

        /// <summary>Download page for the mandatory update (the GitHub release), as set by the
        /// server (force_update_url). Empty if none — the update popup then falls back to the
        /// public releases page.</summary>
        public static string ForceUpdateUrl
        {
            get { lock (_lock) { return _cfg.ForceUpdateUrl ?? string.Empty; } }
        }

        /// <summary>Fetches the latest remote config. Safe to call repeatedly (startup + each launch).</summary>
        public static async Task RefreshAsync()
        {
            (NextendoApi.BetaConfig cfg, bool reachable) = await NextendoApi.GetBetaConfigAsync();
            lock (_lock)
            {
                _cfg = cfg;
                _reachable = reachable;
                _polled = true;
            }
        }

        /// <summary>Whether online is currently blocked, and why. FAIL CLOSED: blocked until a
        /// successful poll confirms online is enabled and the client is up to date.</summary>
        public static BlockReason Evaluate()
        {
            lock (_lock)
            {
                if (!_polled || !_reachable)
                {
                    return BlockReason.Unreachable;
                }

                if (!_cfg.OnlineEnabled)
                {
                    return BlockReason.Disabled;
                }

                if (IsClientOutdated(_cfg.MinAppVersion))
                {
                    return BlockReason.UpdateRequired;
                }

                return BlockReason.None;
            }
        }

        /// <summary>Localized, user-facing reason online is unavailable (Nintendo-style).</summary>
        public static string Message(BlockReason reason)
        {
            bool fr = LocaleManager.Instance.CurrentLanguageCode?.StartsWith("fr", StringComparison.OrdinalIgnoreCase) ?? false;

            switch (reason)
            {
                case BlockReason.Disabled:
                    // Prefer the operator's own message (set on the server) if present.
                    string srv = fr ? _cfg.MessageFr : _cfg.MessageEn;
                    if (string.IsNullOrWhiteSpace(srv))
                    {
                        srv = fr ? _cfg.MessageEn : _cfg.MessageFr;
                    }

                    return !string.IsNullOrWhiteSpace(srv)
                        ? srv
                        : LocaleManager.Instance[LocaleKeys.Dialog_Nextendo_OnlineDisabledByOperator];

                case BlockReason.UpdateRequired:
                    return LocaleManager.Instance[LocaleKeys.Dialog_Nextendo_OnlineUpdateRequired];

                case BlockReason.Unreachable:
                default:
                    return LocaleManager.Instance[LocaleKeys.Dialog_Nextendo_OnlineServersUnreachable];
            }
        }

        private static bool IsClientOutdated(string minVersion)
        {
            if (!TryParseVersion(minVersion, out Version min))
            {
                return false; // no/!invalid minimum -> don't gate on version
            }

            if (!TryParseVersion(ReleaseInformation.Version, out Version cur))
            {
                return false; // can't read our own version -> don't hard-block on it
            }

            return cur < min;
        }

        // Parses a leading "major.minor[.build[.rev]]" and ignores any suffix (e.g. "-dirty",
        // build metadata) so "1.0.0-dirty" compares as 1.0.0.
        private static bool TryParseVersion(string s, out Version v)
        {
            v = null;
            if (string.IsNullOrWhiteSpace(s))
            {
                return false;
            }

            int i = 0;
            while (i < s.Length && (char.IsDigit(s[i]) || s[i] == '.'))
            {
                i++;
            }

            string core = s[..i].Trim('.');
            return Version.TryParse(core, out v);
        }
    }
}
