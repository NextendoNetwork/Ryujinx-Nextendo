namespace Ryujinx.Ava.UI.Models
{
    /// <summary>[Nextendo] One row in the Settings → Nextendo "Historique de jeu" list.</summary>
    public class NextendoHistoryModel
    {
        public string Name { get; set; }
        public byte[] Icon { get; set; }
        public string PlayedText { get; set; }
        public string LastText { get; set; }
    }
}
