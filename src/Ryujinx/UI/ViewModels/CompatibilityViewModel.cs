using Gommon;
using Ryujinx.Ava.Systems;
using Ryujinx.Ava.Systems.AppLibrary;
using System;
using System.Collections.Generic;
using System.Linq;
using Ryujinx.Ava.Common.Locale;

namespace Ryujinx.Ava.UI.ViewModels
{
    public class CompatibilityViewModel : BaseModel, IDisposable
    {
        private readonly ApplicationLibrary _appLibrary;

        private (int Status, int Name) _sorting;

        public bool IsSortedByTitle => true;
        public bool IsSortedByStatus => true;

        // Avalonia takes names of status from these variables
        public LocaleKeys IsStringPlayable => LocaleKeys.CompatibilityListPlayable;
        public LocaleKeys IsStringInGame => LocaleKeys.CompatibilityListIngame;
        public LocaleKeys IsStringMenus => LocaleKeys.CompatibilityListMenus;
        public LocaleKeys IsStringBoots => LocaleKeys.CompatibilityListBoots;
        public LocaleKeys IsStringNothing => LocaleKeys.CompatibilityListNothing;

        public string PlayableInfoText { get; set; }
        public string InGameInfoText { get; set; }
        public string MenusInfoText { get; set; }
        public string BootsInfoText { get; set; }
        public string NothingInfoText { get; set; }


        private IEnumerable<CompatibilityEntry> _currentEntries = CompatibilityDatabase.Entries;

        private string[] _ownedGameTitleIds = [];

        private Func<CompatibilityEntry, object> _sortKeySelector = x => x.GameName; // Default sort by GameName

        public IEnumerable<CompatibilityEntry> CurrentEntries => OnlyShowOwnedGames
            ? _currentEntries.Where(x =>
                x.TitleId.Check(tid => _ownedGameTitleIds.ContainsIgnoreCase(tid)))
            : _currentEntries;

        public CompatibilityViewModel() {}

        private void AppCountUpdated(object _, ApplicationCountUpdatedEventArgs __)
            => _ownedGameTitleIds = _appLibrary.Applications.Keys.Select(x => x.ToString("X16")).ToArray();

        public CompatibilityViewModel(ApplicationLibrary appLibrary)
        {
            _appLibrary = appLibrary;
            AppCountUpdated(null, null);
            CountByStatus();
            _appLibrary.ApplicationCountUpdated += AppCountUpdated;
        }

        public void CountByStatus()
        {
            PlayableInfoText =  LocaleManager.Instance[LocaleKeys.CompatibilityListPlayable]  + ": " +   CurrentEntries.Count(x => x.Status == LocaleKeys.CompatibilityListPlayable);
            InGameInfoText =    LocaleManager.Instance[LocaleKeys.CompatibilityListIngame]    + ": " +   CurrentEntries.Count(x => x.Status == LocaleKeys.CompatibilityListIngame);
            MenusInfoText =     LocaleManager.Instance[LocaleKeys.CompatibilityListMenus]     + ": " +   CurrentEntries.Count(x => x.Status == LocaleKeys.CompatibilityListMenus);
            BootsInfoText =     LocaleManager.Instance[LocaleKeys.CompatibilityListBoots]     + ": " +   CurrentEntries.Count(x => x.Status == LocaleKeys.CompatibilityListBoots);
            NothingInfoText =   LocaleManager.Instance[LocaleKeys.CompatibilityListNothing]   + ": " +   CurrentEntries.Count(x => x.Status == LocaleKeys.CompatibilityListNothing);

            _onlyShowOwnedGames = true;
        }

        void IDisposable.Dispose()
        {
            GC.SuppressFinalize(this);
            _appLibrary.ApplicationCountUpdated -= AppCountUpdated;
        }

        private bool _onlyShowOwnedGames;

        public bool OnlyShowOwnedGames
        {
            get => _onlyShowOwnedGames;
            set
            {
                OnPropertyChanging();
                OnPropertyChanging(nameof(CurrentEntries));
                _onlyShowOwnedGames = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(CurrentEntries));
            }
        }


        public void NameSorting(int nameSort = 0)
        {
            _sorting.Name = nameSort;
            SortApply();
            OnPropertyChanged();
            OnPropertyChanged(nameof(SortName));
        }

        public void StatusSorting(int statusSort = 0)
        {
            _sorting.Status = statusSort;
            SortApply();
            OnPropertyChanged();
            OnPropertyChanged(nameof(SortName));
        }

        public void Search(string searchTerm)
        {
            if (string.IsNullOrEmpty(searchTerm))
            {
                SetEntries(CompatibilityDatabase.Entries);
                SortApply();
                return;
            }

            SetEntries(CompatibilityDatabase.Entries.Where(x =>
                x.GameName.ContainsIgnoreCase(searchTerm)
                || x.TitleId.Check(tid => tid.ContainsIgnoreCase(searchTerm))));

            SortApply();
        }

        private void SetEntries(IEnumerable<CompatibilityEntry> entries)
        {
            _currentEntries = entries.ToList();
            OnPropertyChanged(nameof(CurrentEntries));
        }

        private void SortApply()
        {
            try
            {
                _currentEntries = (_sorting switch
                {
                    (0, 0) => _currentEntries.OrderBy(x => _sortKeySelector(x) ?? string.Empty), // A - Z
                    (0, 1) => _currentEntries.OrderByDescending(x => _sortKeySelector(x) ?? string.Empty), // Z - A
                    (1, 0) => _currentEntries.OrderBy(x => x.Status).ThenBy(x => x.GameName, StringComparer.OrdinalIgnoreCase), // Status Playable - Nothing, then A - Z
                    (1, 1) => _currentEntries.OrderBy(x => x.Status).ThenByDescending(x => x.GameName, StringComparer.OrdinalIgnoreCase), // Status Nothing - Playable, then A - Z
                    (2, 0) => _currentEntries.OrderByDescending(x => x.Status).ThenBy(x => x.GameName, StringComparer.OrdinalIgnoreCase), // Status Playable - Nothing, then Z - A
                    (2, 1) => _currentEntries.OrderByDescending(x => x.Status).ThenByDescending(x => x.GameName, StringComparer.OrdinalIgnoreCase), // Status Nothing - Playable, then Z - A
                    _ => _currentEntries.OrderBy(x => x.Status)
                }).ToList();
            }
            catch (Exception)
            {

            }
            
            OnPropertyChanged(nameof(CurrentEntries));
        }

        public string SortName
        {
            get
            {
                return (_sorting.Name) switch
                {
                    (0) => LocaleManager.Instance[LocaleKeys.GameListSortStatusNameAscending],
                    (1) => LocaleManager.Instance[LocaleKeys.GameListSortStatusNameDescending],
                    _ => string.Empty,
                };
            }
        }

    }
}
