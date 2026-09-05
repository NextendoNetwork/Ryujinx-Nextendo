using Avalonia.Media;
using System;

namespace Ryujinx.Ava.UI.Models
{
    // [Nextendo] Une entrée de la fenêtre « Joueurs » : soit quelqu'un présent dans
    // mon salon en ce moment, soit quelqu'un que j'ai croisé récemment.
    //
    // Le même modèle sert aux deux onglets parce que l'écran affiche les mêmes
    // choses (photo, pseudo, boutons) ; seuls diffèrent les champs remplis. Ce
    // qui vient du salon n'a pas de date, ce qui vient de l'historique n'a pas
    // d'hôte — d'où les propriétés de visibilité plutôt que deux modèles.
    public class NextendoLobbyPlayerModel
    {
        public ulong Pid { get; init; }
        public string Name { get; init; } = "";
        public byte[] Image { get; init; }

        /// <summary>Faux quand le PID ne correspond à aucun compte Nextendo connu.
        /// On masque alors les boutons : on ne peut ni l'ajouter, ni le signaler utilement.</summary>
        public bool Known { get; init; }

        /// <summary>Déjà dans ma liste d'amis : masque le bouton « ajouter ».</summary>
        public bool IsFriend { get; init; }

        /// <summary>Hôte du salon. Toujours faux dans l'onglet des rencontres.</summary>
        public bool Host { get; init; }

        /// <summary>C'est moi : pas de bouton « signaler » sur soi-même.</summary>
        public bool IsMe { get; init; }

        /// <summary>Nom du jeu où la rencontre a eu lieu, déjà résolu pour l'affichage.</summary>
        public string GameName { get; init; } = "";

        /// <summary>Date de la rencontre. MinValue dans l'onglet du salon.</summary>
        public DateTime SeenAt { get; init; } = DateTime.MinValue;

        /// <summary>« 14:32 · Mario Kart 8 Deluxe » — ce que le lecteur voit sous le pseudo.</summary>
        public string SeenLine
        {
            get
            {
                if (SeenAt == DateTime.MinValue)
                {
                    return GameName;
                }

                string quand = SeenAt.Date == DateTime.Now.Date
                    ? SeenAt.ToString("HH:mm")
                    : SeenAt.ToString("dd/MM HH:mm");

                return string.IsNullOrEmpty(GameName) ? quand : $"{quand} · {GameName}";
            }
        }

        /// <summary>Initiale affichée quand la photo de profil n'a pas pu être chargée.</summary>
        public string Initial => string.IsNullOrEmpty(Name) ? "?" : Name.Substring(0, 1).ToUpperInvariant();

        public bool HasImage => Image is { Length: > 0 };
        public bool ShowInitial => !HasImage;

        /// <summary>Les boutons n'ont de sens que sur un compte connu qui n'est pas moi.</summary>
        public bool CanAct => Known && !IsMe;

        /// <summary>Le bouton « ajouter » ne s'affiche que si la personne n'est pas déjà amie.</summary>
        public bool CanAdd => Known && !IsMe && !IsFriend;

        /// <summary>Le badge « hôte » ne s'affiche que dans l'onglet du salon.</summary>
        public bool ShowHostBadge => Host && !IsMe;

        public IBrush NameColor => IsMe ? MeBrush : NormalBrush;

        private static readonly IBrush MeBrush = Brush.Parse("#3EA9FF");
        private static readonly IBrush NormalBrush = Brush.Parse("#FFFFFF");
    }
}
