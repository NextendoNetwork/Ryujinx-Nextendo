using CommunityToolkit.Mvvm.ComponentModel;

namespace Ryujinx.Ava.UI.Models
{
    // [Nextendo] One mod offered by the curated Mod Store (server manifest entry).
    public partial class ModStoreItem : ObservableObject
    {
        public string Id { get; init; }
        public string Folder { get; init; }   // actual on-disk mod folder name (zip top-level)
        public string Name { get; init; }
        public string Description { get; init; }
        public long Size { get; init; }

        [ObservableProperty] private bool _installed;
        [ObservableProperty] private bool _busy;

        public string SizeText =>
            Size < 1024 * 1024 ? $"{Size / 1024.0:0.#} Ko" : $"{Size / (1024.0 * 1024.0):0.#} Mo";
    }
}
