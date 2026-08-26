using Ryujinx.Common.Logging;
using System;
using System.IO;
using System.Net;
using System.Text.Json;

namespace Ryujinx.Common.Configuration
{
    /// <summary>
    /// [Nextendo] Serveur personnalisé : faire tourner l'émulateur sur le serveur de
    /// quelqu'un d'autre, entièrement en dehors de Nextendo Network — compte compris.
    ///
    /// ⚠️ C'EST UN BASCULEMENT COMPLET, PAS UNE SIMPLE REDIRECTION DE TRAFIC DE JEU. Activer
    /// ce mode exige TROIS champs, pas deux : l'IP du serveur de jeu, l'IP du contrôle NAT,
    /// ET le domaine du backend de compte (<see cref="CustomDomain"/>) — voir plus bas. Les
    /// trois ensemble redirigent tout ce que l'émulateur fait en ligne (jeu, NAT, compte,
    /// amis, sauvegardes) vers l'infrastructure d'un tiers au lieu de la nôtre. Si l'un des
    /// trois manque ou est invalide, <see cref="HorsNextendo"/> retombe sur l'état sûr : plus
    /// rien ne parle à Nextendo (pas de compte, pas de jeton, pas d'amis, pas de sauvegarde en
    /// ligne, pas de présence, pas de contrôle de version, pas de statut Discord Nextendo) —
    /// jamais un retour silencieux à NOS serveurs sur une simple faute de frappe.
    ///
    /// Pourquoi le domaine de compte est verrouillé à une saisie explicite : chaque requête
    /// vers l'API de compte porte le jeton du compte, qui donne un accès complet à celui-ci.
    /// Voir la note sur <see cref="CustomDomain"/> plus bas pour le détail de cette protection.
    ///
    /// DEUX adresses, pas une. Pia exige que les deux répondeurs du contrôle NAT soient
    /// à des adresses PUBLIQUES DISTINCTES : face à une seule, il les déduplique,
    /// n'envoie jamais la seconde sonde, et le contrôle n'aboutit jamais (2618-201).
    /// Le second champ vide ne peut donc plus retomber sur notre répondeur — ce serait
    /// « quelque chose qui vient de Nextendo » — il faut deux adresses de son côté.
    /// ServerIp/NatIp restent des adresses IP LITTÉRALES, volontairement : ce sont les
    /// adresses que consomme <see cref="Ryujinx.HLE.HOS.Services.Sockets.Sfdnsres.Proxy.DnsMitmResolver"/>
    /// pour répondre à la place du DNS émulé de la console, donc rien à résoudre ici.
    ///
    /// TROISIÈME champ, séparé et OBLIGATOIRE dès que le mode est actif :
    /// <see cref="CustomDomain"/>. Les deux champs ci-dessus routent le TRAFIC DE JEU (DNS
    /// des hôtes Nintendo) ; ils ne rallument rien côté compte, exprès (voir plus haut).
    /// CustomDomain, lui, sert précisément à rallumer le compte — mais contre un AUTRE
    /// backend compatible Nextendo (une instance d'un ami, une communauté qui fait tourner
    /// le même serveur) plutôt que le nôtre : connexion, amis, sauvegardes en ligne
    /// redeviennent actifs, simplement adressés ailleurs. Obligatoire parce qu'un « serveur
    /// personnalisé » sans backend de compte derrière n'a plus de raison d'être dans cette
    /// version du réglage : soit on reste chez Nextendo, soit on bascule entièrement chez
    /// un autre backend, y compris le compte — pas d'entre-deux « jeu perso, compte coupé ».
    ///
    /// Ça n'entre PAS en conflit avec le verrou de <see cref="NextendoEndpoint"/> (qui, lui,
    /// protège contre une variable d'environnement plantée dans un .bat ou collée depuis un
    /// chat — un réglage que la victime n'a pas tapé elle-même). Ici la personne qui joue
    /// tape ELLE-MÊME l'adresse dans les Réglages : c'est un choix explicite et informé, pas
    /// quelque chose qu'on peut lui glisser sous le tapis. NextendoEndpoint et NextendoApi.
    /// SiteUrl() consultent d'ailleurs ce réglage AVANT leurs propres règles — voir ces deux
    /// classes. HTTPS exigé (sauf boucle locale, pour les tests) : le jeton du compte ne doit
    /// jamais voyager en clair, même vers un serveur en qui on a confiance.
    /// </summary>
    public static class NextendoServerOverride
    {
        private sealed class Reglages
        {
            public bool Enabled { get; set; }
            public string ServerIp { get; set; } = "";
            public string NatIp { get; set; } = "";
            public string CustomDomain { get; set; } = "";
        }

        private static string FilePath => Path.Combine(AppDataManager.BaseDirPath, "nextendo_server_override.json");

        private static Reglages _cache;
        private static bool _charge;

        private static Reglages Courant()
        {
            if (_charge)
            {
                return _cache;
            }

            _charge = true;
            _cache = new Reglages();

            try
            {
                if (File.Exists(FilePath))
                {
                    _cache = JsonSerializer.Deserialize<Reglages>(File.ReadAllText(FilePath)) ?? new Reglages();
                }
            }
            catch (Exception ex)
            {
                // Un fichier illisible ne doit pas empêcher l'émulateur de démarrer :
                // on retombe sur les serveurs officiels, ce qui est l'état sûr.
                Logger.Warning?.Print(LogClass.Application, $"[Nextendo] override illisible: {ex.Message}");
            }

            return _cache;
        }

        /// <summary>La redirection est-elle active ET utilisable ?</summary>
        public static bool IsActive
        {
            get
            {
                Reglages r = Courant();

                return r.Enabled && ValiderIp(r.ServerIp) is not null;
            }
        }

        /// <summary>L'adresse à substituer aux serveurs de jeu, ou null si inactive.</summary>
        public static IPAddress ServerAddress => IsActive ? ValiderIp(Courant().ServerIp) : null;

        /// <summary>
        /// L'adresse du SECOND répondeur NAT. Null si non renseignée : l'appelant doit
        /// alors garder la sienne, et surtout PAS réutiliser ServerAddress — deux
        /// répondeurs à la même adresse font échouer le contrôle NAT en silence.
        /// </summary>
        public static IPAddress NatAddress => IsActive ? ValiderIp(Courant().NatIp) : null;

        public static bool Enabled => Courant().Enabled;
        public static string ServerIpText => Courant().ServerIp ?? "";
        public static string NatIpText => Courant().NatIp ?? "";
        public static string CustomDomainText => Courant().CustomDomain ?? "";

        /// <summary>
        /// L'URL de base (https://hôte, sans slash final) du backend Nextendo-compatible où
        /// rediriger compte / connexion / amis / sauvegardes en ligne, ou null si le mode
        /// n'est pas actif ou si le champ est vide/invalide. Jamais résolue en IP ici : c'est
        /// une URL HTTP ordinaire, consommée par HttpClient côté hôte, qui fait sa propre
        /// résolution DNS réelle — rien à voir avec <see cref="ServerAddress"/>/<see cref="NatAddress"/>.
        /// </summary>
        public static string AccountDomainUrl => Courant().Enabled ? ValiderDomaineCompte(Courant().CustomDomain) : null;

        /// <summary>Vrai si une redirection de compte valide est configurée (voir <see cref="AccountDomainUrl"/>).</summary>
        public static bool HasCustomDomain => AccountDomainUrl is not null;

        /// <summary>
        /// ⚠️ LA question à poser avant tout ce qui touche à Nextendo. Vraie dès que le mode
        /// « serveur personnalisé » est coché SANS domaine de compte valide, MÊME si
        /// l'adresse IP/serveur saisie est invalide.
        ///
        /// C'est volontaire, et c'est le point délicat : <see cref="IsActive"/> exige une
        /// adresse utilisable, parce qu'on ne peut pas rediriger vers rien. Ici c'est
        /// l'inverse — quelqu'un qui a coché la case sans donner de domaine de compte a dit
        /// qu'il ne veut pas de nos services, et une faute de frappe dans son adresse ne doit
        /// surtout pas le reconnecter en silence à notre compte et à nos serveurs. Le mode
        /// dégradé, c'est « hors ligne », pas « retour chez Nextendo ».
        ///
        /// Quand un domaine de compte valide EST renseigné, c'est l'inverse qu'on veut : le
        /// compte doit rester actif, simplement adressé ailleurs — <see cref="HasCustomDomain"/>
        /// débranche donc HorsNextendo dans ce cas précis.
        /// </summary>
        public static bool HorsNextendo => Courant().Enabled && !HasCustomDomain;

        /// <summary>Enregistre les réglages et les applique au prochain démarrage.</summary>
        public static void Save(bool enabled, string serverIp, string natIp, string customDomain)
        {
            Reglages r = new()
            {
                Enabled = enabled,
                ServerIp = (serverIp ?? "").Trim(),
                NatIp = (natIp ?? "").Trim(),
                CustomDomain = (customDomain ?? "").Trim(),
            };

            _cache = r;
            _charge = true;

            try
            {
                File.WriteAllText(FilePath, JsonSerializer.Serialize(r, new JsonSerializerOptions { WriteIndented = true }));

                Logger.Info?.Print(LogClass.Application,
                    enabled
                        ? $"[Nextendo] redirection reseau ACTIVE : jeu -> {r.ServerIp}, NAT -> {(string.IsNullOrEmpty(r.NatIp) ? "(inchange)" : r.NatIp)}, compte -> {(string.IsNullOrEmpty(r.CustomDomain) ? "(desactive)" : r.CustomDomain)}"
                        : "[Nextendo] redirection reseau desactivee, retour aux serveurs officiels");
            }
            catch (Exception ex)
            {
                Logger.Error?.Print(LogClass.Application, $"[Nextendo] override non enregistre: {ex.Message}");
            }
        }

        /// <summary>Enlève espaces et guillemets superflus ; "" si rien d'utilisable.</summary>
        private static string Nettoyer(string brut)
        {
            return (brut ?? "").Trim().Trim('"', '\'', '“', '”', '‘', '’');
        }

        /// <summary>Une adresse IP littérale, ou null. Pas de résolution de nom ici : voir la note de classe.</summary>
        private static IPAddress ValiderIp(string brut)
        {
            string s = Nettoyer(brut);

            return s.Length > 0 && IPAddress.TryParse(s, out IPAddress ip) ? ip : null;
        }

        /// <summary>
        /// Valide et normalise l'URL du domaine de compte : une URL absolue en boucle locale
        /// (n'importe quel schéma — tests en local), ou une URL https:// absolue sinon. http://
        /// distant est refusé : ça enverrait le jeton du compte en clair, même vers un serveur
        /// de confiance. Rend l'URL sans slash final, ou null si vide ou inutilisable.
        /// </summary>
        private static string ValiderDomaineCompte(string brut)
        {
            string s = Nettoyer(brut);

            if (s.Length == 0 || !Uri.TryCreate(s, UriKind.Absolute, out Uri uri))
            {
                return null;
            }

            if (!uri.IsLoopback && uri.Scheme != Uri.UriSchemeHttps)
            {
                return null;
            }

            return s.TrimEnd('/');
        }

        /// <summary>
        /// Vrai si le champ « domaine de compte » est une URL utilisable — voir
        /// <see cref="ValiderDomaineCompte"/>. Le champ est OBLIGATOIRE dès que le mode est
        /// actif (voir la note de classe) : un texte vide n'est donc PAS valide ici, à la
        /// différence d'un champ facultatif. Utilisé pour la validation du formulaire de
        /// réglages.
        /// </summary>
        public static bool EstDomaineCompteValide(string brut)
        {
            return ValiderDomaineCompte(brut) is not null;
        }
    }
}
