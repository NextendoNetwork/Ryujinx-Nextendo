using Ryujinx.Ava.Systems.AppLibrary;
using Ryujinx.Common.Configuration;
using Ryujinx.Common.Logging;
using System;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;

namespace Ryujinx.Ava.Common
{
    /// <summary>
    /// [Nextendo] Game-save cloud sync. On launch of a Nextendo-compatible title (only),
    /// the account's save for that title is downloaded and applied locally BEFORE the
    /// game runs; when the game closes, the local save is uploaded back to the account.
    /// So progression follows the Nextendo account across machines. Best-effort: any
    /// failure is logged and ignored (the game still runs with whatever is local).
    /// </summary>
    public static class NextendoSaveSync
    {
        private static string BaseUrl()
        {
            // [Nextendo] Une seule decision, dans NextendoEndpoint : c'est elle qui choisit qui
            // recoit le jeton du compte. Cette logique etait dupliquee ici et acceptait
            // n'importe quelle valeur de NEXTENDO_API.
            return NextendoEndpoint.BaseUrl();
        }

        // Download + import this title's cloud save. Call BEFORE the game starts.
        public static async Task PullAsync(ApplicationData app)
        {
            // [Nextendo beta] Cloud saves are excluded for GUEST profiles — skip pull entirely.
            if (app == null || !app.IsNextendoCompatible || !NextendoAccount.IsLinked || NextendoAccount.IsGuest || string.IsNullOrEmpty(NextendoAccount.NexToken))
            {
                return;
            }

            // Only sync on the profile bound to the Nextendo account (online profile). A local/other
            // profile must NOT pull the account's cloud save onto its own (empty) save.
            if (!ApplicationHelper.IsNextendoProfileActive())
            {
                Logger.Info?.Print(LogClass.Application, $"[Nextendo] save pull {app.IdString}: skipped (active profile is not the Nextendo one)");
                return;
            }

            // Never overwrite real local progress: the cloud copy is only restored onto a
            // machine that has NO local save yet (fresh install / new PC). With a local save
            // present, IT is the player's progress — pulling a possibly-older cloud copy every
            // launch would reset things (VR, last character, …). The push keeps the cloud backed up.
            if (ApplicationHelper.LocalSaveHasContent(app.Id))
            {
                Logger.Info?.Print(LogClass.Application, $"[Nextendo] save pull {app.IdString}: local save present -> kept (no overwrite)");
                return;
            }

            try
            {
                using HttpClient http = new() { Timeout = TimeSpan.FromSeconds(30) };
                http.DefaultRequestHeaders.Add("Authorization", "Bearer " + NextendoAccount.NexToken);

                HttpResponseMessage resp = await http.GetAsync($"{BaseUrl()}/api/save/{app.IdString}");
                if (resp.StatusCode == HttpStatusCode.NoContent || !resp.IsSuccessStatusCode)
                {
                    return; // no cloud save stored yet
                }

                byte[] zip = await resp.Content.ReadAsByteArrayAsync();
                if (zip.Length > 0)
                {
                    bool ok = ApplicationHelper.ImportNextendoSave(app.Id, app.ControlHolder, zip);
                    Logger.Info?.Print(LogClass.Application, $"[Nextendo] save pull {app.IdString}: {(ok ? "applied" : "skipped")} ({zip.Length} B)");
                }
            }
            catch (Exception ex)
            {
                Logger.Warning?.Print(LogClass.Application, $"[Nextendo] save pull failed: {ex.Message}");
            }
        }

        // Export + upload this title's local save. Call AFTER the game has fully closed.
        public static async Task PushAsync(ulong titleId, string idString)
        {
            Logger.Info?.Print(LogClass.Application, $"[Nextendo] save push START {idString} (linked={NextendoAccount.IsLinked} token={!string.IsNullOrEmpty(NextendoAccount.NexToken)})");

            // [Nextendo beta] Cloud saves are excluded for GUEST profiles — never push.
            if (!NextendoAccount.IsLinked || NextendoAccount.IsGuest || string.IsNullOrEmpty(NextendoAccount.NexToken))
            {
                return;
            }

            // Only the Nextendo-bound profile uploads its save to the account. A local/other profile
            // pushing its (empty) save would clobber the account's cloud backup.
            if (!ApplicationHelper.IsNextendoProfileActive())
            {
                Logger.Info?.Print(LogClass.Application, $"[Nextendo] save push {idString}: skipped (active profile is not the Nextendo one)");
                return;
            }

            try
            {
                byte[] zip = ApplicationHelper.ExportNextendoSave(titleId);
                if (zip == null || zip.Length == 0)
                {
                    Logger.Info?.Print(LogClass.Application, $"[Nextendo] save push {idString}: export empty/null -> nothing to upload");
                    return;
                }

                using HttpClient http = new() { Timeout = TimeSpan.FromSeconds(60) };
                http.DefaultRequestHeaders.Add("Authorization", "Bearer " + NextendoAccount.NexToken);

                using ByteArrayContent content = new(zip);
                HttpResponseMessage resp = await http.PutAsync($"{BaseUrl()}/api/save/{idString}", content);

                Logger.Info?.Print(LogClass.Application, $"[Nextendo] save push {idString}: HTTP {(int)resp.StatusCode} ({zip.Length} B)");
            }
            catch (Exception ex)
            {
                Logger.Warning?.Print(LogClass.Application, $"[Nextendo] save push failed: {ex.Message}");
            }
        }
    }
}
