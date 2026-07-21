using Gommon;
using Ryujinx.Ava.Systems;
using Ryujinx.Ava.Systems.AppLibrary;
using System;
using System.Collections.Concurrent;
using System.Linq;

namespace Ryujinx.Ava.Common
{
    /// <summary>
    /// [Nextendo] Turns a title id into a game name for presence ("Playing Super Mario Odyssey").
    ///
    /// Any title can show up here, not just the ones Nextendo serves online: a friend may be
    /// playing something we have nothing to do with, and "Online" tells you nothing when the
    /// name is right there to be found.
    /// </summary>
    public static class NextendoGameNames
    {
        // Resolution is per title id and never changes within a run, but presence refreshes every
        // 20s for every friend and the compatibility list is a linear scan of a few thousand rows.
        private static readonly ConcurrentDictionary<string, string> _cache = new();

        /// <summary>The game's name, or null when the title id is unknown or empty.</summary>
        public static string Resolve(string titleId)
        {
            if (string.IsNullOrWhiteSpace(titleId))
            {
                return null;
            }

            string key = titleId.Trim().ToLowerInvariant();

            // A miss is worth caching too — an unknown id stays unknown, and re-scanning the whole
            // compatibility list every refresh to rediscover that would be pure waste.
            return _cache.GetOrAdd(key, Lookup);
        }

        private static string Lookup(string titleId)
        {
            // The player's own library first: it carries the name as it appears in their game list,
            // in their language, including titles the compatibility list has never heard of.
            try
            {
                ApplicationLibrary library = RyujinxApp.MainWindow?.ApplicationLibrary;
                if (library is not null)
                {
                    ApplicationData match = library.Applications.Items
                        .FirstOrDefault(app => app.IdString.EqualsIgnoreCase(titleId));

                    if (!string.IsNullOrEmpty(match?.Name))
                    {
                        return match.Name;
                    }
                }
            }
            catch
            {
                // Library not ready yet: fall through, the bundled list still knows most games.
            }

            // Then the bundled compatibility list, which covers games the player does not own —
            // the usual case for a friend's title.
            try
            {
                CompatibilityEntry entry = CompatibilityDatabase.Find(titleId);
                if (!string.IsNullOrEmpty(entry?.GameName))
                {
                    return entry.GameName;
                }
            }
            catch
            {
                // Malformed or missing database: not worth breaking a friends list over.
            }

            // Backstop for the games Nextendo actually serves. The compatibility list only knows
            // Splatoon 2 as 01003BC0000A0000 and has no row for 0100F8F0000A2000 — the id our
            // players actually run — so without this a friend in Splatoon 2 reads as plain
            // "Online", on the one game we most want named.
            return _nextendoTitles.TryGetValue(titleId, out string known) ? known : null;
        }

        private static readonly System.Collections.Generic.Dictionary<string, string> _nextendoTitles = new()
        {
            ["0100152000022000"] = "Mario Kart 8 Deluxe",
            ["01006a800016e000"] = "Super Smash Bros. Ultimate",
            ["0100f8f0000a2000"] = "Splatoon 2",
            ["01003bc0000a0000"] = "Splatoon 2",
            ["01003c700009c800"] = "Splatoon 2",
        };
    }
}
