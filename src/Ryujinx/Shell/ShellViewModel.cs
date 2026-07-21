using Avalonia.Media.Imaging;
using DynamicData;
using Ryujinx.Ava.Systems.AppLibrary;
using Ryujinx.Ava.Systems.Configuration;
using Ryujinx.Common.Logging;
using Ryujinx.HLE.FileSystem;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;

namespace Ryujinx.Ava.Shell
{
    // One card in a rail. Icon is the real game art (NACP icon); null -> placeholder glyph.
    public sealed class GameItem
    {
        public string Name { get; init; } = "";
        public Bitmap Icon { get; init; }
        public bool HasIcon => Icon != null;
        public bool ShowProgress { get; init; }
        public double Progress { get; init; }
        public double ProgressWidth => Math.Clamp(Progress, 0, 1) * 150.0;
    }

    // Loads the user's real game library (icons included) off the UI thread and exposes it
    // for the Harbor-style home. Pure data — no engine boot. Fails soft to placeholders.
    public sealed class ShellViewModel : INotifyPropertyChanged
    {
        public ObservableCollection<GameItem> Recents { get; } = new();
        public ObservableCollection<GameItem> Library { get; } = new();

        private Bitmap _heroIcon;
        public Bitmap HeroIcon { get => _heroIcon; private set => Set(ref _heroIcon, value); }

        private bool _hasHero;
        public bool HasHero { get => _hasHero; private set => Set(ref _hasHero, value); }

        private string _heroName = "Nextendo";
        public string HeroName { get => _heroName; private set => Set(ref _heroName, value); }

        private string _heroSubtitle = "Ajoute tes jeux pour commencer.";
        public string HeroSubtitle { get => _heroSubtitle; private set => Set(ref _heroSubtitle, value); }

        private string _heroMeta = "Nextendo Network";
        public string HeroMeta { get => _heroMeta; private set => Set(ref _heroMeta, value); }

        public ShellViewModel() => _ = LoadAsync();

        private async Task LoadAsync()
        {
            List<ApplicationData> apps = await Task.Run(LoadLibrarySafe);

            if (apps is not { Count: > 0 })
            {
                // Fail soft: keep the rails populated so the home never looks broken.
                for (int i = 0; i < 8; i++)
                {
                    Library.Add(new GameItem());
                    if (i < 4)
                    {
                        Recents.Add(new GameItem { ShowProgress = true, Progress = 0.4 });
                    }
                }

                return;
            }

            List<ApplicationData> ordered = apps
                .OrderByDescending(a => a.LastPlayed ?? DateTime.MinValue)
                .ThenBy(a => a.Name)
                .ToList();

            ApplicationData hero = ordered.FirstOrDefault(a => a.IsNextendoCompatible) ?? ordered[0];

            HeroIcon = ToBitmap(hero.Icon);
            HeroName = hero.Name;
            HeroSubtitle = hero.IsNextendoCompatible
                ? "Jouable en ligne sur les serveurs Nextendo — salons, amis et classements."
                : "Prêt à jouer. Reprends là où tu t'es arrêté.";
            HeroMeta = hero.IsNextendoCompatible ? "En ligne · Nextendo" : "Hors ligne";
            HasHero = HeroIcon != null;

            foreach (ApplicationData a in ordered.Where(a => a.HasPlayedPreviously).Take(10))
            {
                Recents.Add(new GameItem
                {
                    Name = a.Name,
                    Icon = ToBitmap(a.Icon),
                    ShowProgress = true,
                    Progress = 0.35,
                });
            }

            // If nothing has been played yet, seed "Reprendre" with the first few titles.
            if (Recents.Count == 0)
            {
                foreach (ApplicationData a in ordered.Take(6))
                {
                    Recents.Add(new GameItem { Name = a.Name, Icon = ToBitmap(a.Icon) });
                }
            }

            foreach (ApplicationData a in ordered.Take(24))
            {
                Library.Add(new GameItem { Name = a.Name, Icon = ToBitmap(a.Icon) });
            }
        }

        private static List<ApplicationData> LoadLibrarySafe()
        {
            try
            {
                VirtualFileSystem vfs = VirtualFileSystem.CreateInstance();
                vfs.ReloadKeySet();

                ApplicationLibrary lib = new(vfs, ConfigurationState.Instance.System.IntegrityCheckLevel)
                {
                    DesiredLanguage = ConfigurationState.Instance.System.Language,
                };

                lib.LoadApplications(ConfigurationState.Instance.UI.GameDirs);

                return lib.Applications.Items.ToList();
            }
            catch (Exception ex)
            {
                Logger.Warning?.Print(LogClass.Application, $"[Shell] Library load failed: {ex.Message}");
                return null;
            }
        }

        private static Bitmap ToBitmap(byte[] bytes)
        {
            try
            {
                return bytes is { Length: > 0 } ? new Bitmap(new MemoryStream(bytes)) : null;
            }
            catch
            {
                return null;
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        private void Set<T>(ref T field, T value, [CallerMemberName] string name = null)
        {
            field = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }
}
