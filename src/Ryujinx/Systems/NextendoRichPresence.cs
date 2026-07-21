using Ryujinx.Ava.Systems.AppLibrary;
using Ryujinx.Ava.Systems.PlayReport;
using Ryujinx.Common;
using Ryujinx.Common.Configuration;
using Ryujinx.Common.Logging;
using Ryujinx.Horizon;
using System;
using System.Collections.Generic;
using System.Text.Json;

namespace Ryujinx.Ava.Systems
{
    /// <summary>
    /// [Nextendo] Feeds the game's own play report into Nextendo presence, so friends read
    /// "Playing Mario Kart 8 Deluxe — Single Player" instead of just the title.
    ///
    /// This is the same source Discord rich presence uses, but wired independently: presence for
    /// your Nextendo friends must not depend on whether you enabled Discord integration.
    ///
    /// Coverage is NOT every game, and cannot be. A play report is a per-title structure, and the
    /// text comes from a hand-written spec that decodes that specific title's fields — about 13
    /// games have one (MK8DX, Smash, Odyssey, the Zeldas, Pokémon S/V…). Everything else reports
    /// nothing here, and friends simply see the game name: inventing a mode would be fabricating
    /// data about what someone is doing.
    /// </summary>
    public static class NextendoRichPresence
    {
        /// <summary>Subscribes to play reports. Safe to call twice; call once at startup.</summary>
        public static void Initialize()
        {
            if (_started)
            {
                return;
            }

            _started = true;

            HorizonStatic.PlayReport += HandlePlayReport;
        }

        private static bool _started;

        private static void HandlePlayReport(Horizon.Prepo.Types.PlayReport playReport)
        {
            try
            {
                if (!NextendoAccount.IsLinked)
                {
                    return;
                }

                if (!TitleIDs.CurrentApplication.Value.TryGet(out string titleId))
                {
                    return;
                }

                ApplicationMetadata appMeta = ApplicationLibrary.LoadAndSaveMetaData(titleId);
                FormattedValue formatted = PlayReports.Analyzer.Format(titleId, appMeta, playReport);

                // Unhandled: this title has no spec, or the report is one the spec ignores. Reset:
                // the spec is explicitly saying "back to nothing in particular".
                if (!formatted.Handled || formatted.Reset)
                {
                    Publish("");

                    return;
                }

                Publish(ExtractDetail(formatted.FormattedString));
            }
            catch (Exception ex)
            {
                // Presence detail is a nicety; a malformed report must never disturb the game.
                Logger.Debug?.Print(LogClass.Application, $"[Nextendo] play report ignored: {ex.Message}");
            }
        }

        /// <summary>
        /// Specs return either a JSON object with Details/State, or a bare string. Both shapes are
        /// live in the codebase, so handle both rather than assuming the newer one.
        /// </summary>
        private static string ExtractDetail(string formattedString)
        {
            if (string.IsNullOrWhiteSpace(formattedString))
            {
                return "";
            }

            try
            {
                Dictionary<string, string> parsed =
                    JsonSerializer.Deserialize<Dictionary<string, string>>(formattedString);

                // "Details" is the mode ("Single Player"); "State" is the finer-grained line. Prefer
                // Details, since presence is one short line and the mode is what identifies it.
                if (parsed is not null)
                {
                    if (parsed.TryGetValue("Details", out string details) && !string.IsNullOrWhiteSpace(details))
                    {
                        return Clamp(details);
                    }

                    if (parsed.TryGetValue("State", out string state) && !string.IsNullOrWhiteSpace(state))
                    {
                        return Clamp(state);
                    }

                    return "";
                }
            }
            catch (JsonException)
            {
                // Old format: the whole string is the value.
            }

            return Clamp(formattedString);
        }

        // The account server stores this and hands it to every friend; a spec returning something
        // huge should not become a huge row in everyone's friends list.
        private const int MaxDetailLength = 64;

        private static string Clamp(string value)
        {
            value = value.Trim();

            return value.Length <= MaxDetailLength ? value : value[..MaxDetailLength];
        }

        private static void Publish(string detail)
        {
            if (NextendoFriends.CurrentAppDetail == detail)
            {
                return;
            }

            NextendoFriends.CurrentAppDetail = detail;
            NextendoFriends.PublishGame();
        }
    }
}
