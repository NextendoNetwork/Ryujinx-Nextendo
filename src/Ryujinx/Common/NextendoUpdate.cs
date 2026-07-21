using Ryujinx.Ava.Common.Locale;
using Ryujinx.Ava.UI.Helpers;
using System;
using System.Threading.Tasks;

namespace Ryujinx.Ava.Common
{
    /// <summary>
    /// [Nextendo] Mise à jour OBLIGATOIRE. Quand le serveur indique qu'une version plus récente
    /// de Ryujinx-Nextendo est requise (min_app_version pour ce canal), l'émulateur ne peut PAS
    /// être utilisé tant qu'il n'est pas mis à jour : au démarrage on affiche un popup bloquant
    /// qui renvoie vers la page de téléchargement (les releases GitHub, via force_update_url),
    /// puis on quitte l'application. C'est ainsi que « si une nouvelle version sort, l'installation
    /// est obligatoire sous peine de ne pas pouvoir utiliser l'émulateur ».
    /// </summary>
    public static class NextendoUpdate
    {
        // Page de téléchargement par défaut si le serveur ne fournit pas force_update_url.
        // (Le serveur reste la source de vérité ; ceci n'est qu'un filet de sécurité.)
        private const string DefaultReleasesUrl = "https://github.com/NextendoNetwork/Ryujinx-Nextendo/releases/latest";

        /// <summary>Shows the blocking mandatory-update dialog and EXITS the app. Call on the UI thread.</summary>
        public static async Task ShowMandatoryUpdateAsync()
        {
            string url = NextendoBeta.ForceUpdateUrl;
            if (string.IsNullOrWhiteSpace(url))
            {
                url = DefaultReleasesUrl;
            }

            // Localised through the normal locale system: a hand-rolled `fr ? ... : ...`
            // toggle used to live here, which gave every non-French player English even
            // though the emulator ships 20 languages.
            string primary   = LocaleManager.Instance[LocaleKeys.Dialog_Nextendo_UpdateDialogPrimary];
            string secondary = LocaleManager.Instance[LocaleKeys.Dialog_Nextendo_UpdateDialogSecondary];
            string accept    = LocaleManager.Instance[LocaleKeys.Dialog_Nextendo_UpdateDialogAccept];
            string quit      = LocaleManager.Instance[LocaleKeys.Dialog_Nextendo_UpdateDialogQuit];
            string title     = LocaleManager.Instance[LocaleKeys.Dialog_Nextendo_UpdateDialogTitle];

            UserResult res = await ContentDialogHelper.CreateInfoDialog(primary, secondary, accept, quit, title);

            if (res == UserResult.Ok)
            {
                try { Ryujinx.Common.Helper.OpenHelper.OpenUrl(url); } catch { /* best-effort */ }
            }

            // Obligatoire : on ne laisse jamais utiliser une version périmée.
            Environment.Exit(0);
        }
    }
}
