using CommunityToolkit.Mvvm.ComponentModel;
using Gommon;
using System;
using System.Collections.Generic;
using System.Linq;
using Ryujinx.Ava.Systems.AppLibrary;
using Ryujinx.Ava.UI.Models;
using System.ComponentModel;
using System.Net.Http;
using System.Threading.Tasks;

namespace Ryujinx.Ava.UI.ViewModels
{
    public partial class LdnGamesListViewModel : BaseModel, IDisposable
    {
        public MainWindowViewModel Mwvm { get; }

        private readonly HttpClient _refreshClient;

        private (int PlayerCount, int Name) _sorting;

        private IEnumerable<LdnGameModel> _visibleEntries;

        private string[] _ownedGameTitleIds = [];

        private Func<LdnGameModel, object> _sortKeySelector = x => x.Title.Name; // Default sort by Title name

        public IEnumerable<LdnGameModel> VisibleEntries => ApplyFilters();

        private IEnumerable<LdnGameModel> ApplyFilters()
        {
            if (_visibleEntries is null)
            {
                _visibleEntries = Mwvm.LdnModels;
                SortApply();
            }

            IEnumerable<LdnGameModel> filtered = _visibleEntries;

            if (OnlyShowForOwnedGames)
                filtered = filtered.Where(x => _ownedGameTitleIds.ContainsIgnoreCase(x.Title.Id));
            
            if (OnlyShowPublicGames)
                filtered = filtered.Where(x => x.IsPublic);
            
            if (OnlyShowJoinableGames)
                filtered = filtered.Where(x => x.IsJoinable);

            return filtered;
        }

        public LdnGamesListViewModel()
        {
            if (Program.PreviewerDetached)
            {
                Mwvm = RyujinxApp.MainWindow.ViewModel;
            }
        }
        
        private void AppCountUpdated(object _, ApplicationCountUpdatedEventArgs __)
            => _ownedGameTitleIds = Mwvm.ApplicationLibrary.Applications.Keys.Select(x => x.ToString("X16")).ToArray();

        public LdnGamesListViewModel(MainWindowViewModel mwvm)
        {
            if (Program.PreviewerDetached)
            {
                Mwvm = mwvm;
                _visibleEntries = Mwvm.LdnModels;
                _refreshClient = new HttpClient();
                AppCountUpdated(null, null);
                Mwvm.ApplicationLibrary.ApplicationCountUpdated += AppCountUpdated;
                Mwvm.PropertyChanged += Mwvm_OnPropertyChanged;
            }
        }
        
        void IDisposable.Dispose()
        {
            if (Program.PreviewerDetached)
            {
                _visibleEntries = null;
                _refreshClient.Dispose();
                Mwvm.ApplicationLibrary.ApplicationCountUpdated -= AppCountUpdated;
                Mwvm.PropertyChanged -= Mwvm_OnPropertyChanged;
            }
            GC.SuppressFinalize(this);
        }

        private void Mwvm_OnPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName is nameof(MainWindowViewModel.LdnModels))
                OnPropertyChanged(nameof(VisibleEntries));
        }

        [ObservableProperty]
        public partial bool IsRefreshing { get; set; }

        public async Task RefreshAsync()
        {
            IsRefreshing = true;

            await Mwvm.ApplicationLibrary.RefreshLdn();

            IsRefreshing = false;

            OnPropertyChanged(nameof(VisibleEntries));
        }

        public bool OnlyShowForOwnedGames
        {
            get;
            set
            {
                OnPropertyChanging();
                OnPropertyChanging(nameof(VisibleEntries));
                field = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(VisibleEntries));
            }
        }

        public bool OnlyShowPublicGames
        {
            get;
            set
            {
                OnPropertyChanging();
                OnPropertyChanging(nameof(VisibleEntries));
                field = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(VisibleEntries));
            }
        } = true;

        public bool OnlyShowJoinableGames
        {
            get;
            set
            {
                OnPropertyChanging();
                OnPropertyChanging(nameof(VisibleEntries));
                field = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(VisibleEntries));
            }
        } = true;


        public void NameSorting(int nameSort = 0)
        {
            _sorting.Name = nameSort;
            SortApply();
        }

        public void StatusSorting(int statusSort = 0)
        {
            _sorting.PlayerCount = statusSort;
            SortApply();
        }

        public void Search(string searchTerm)
        {
            if (string.IsNullOrEmpty(searchTerm))
            {
                SetEntries(Mwvm.LdnModels);
                SortApply();
                return;
            }

            SetEntries(Mwvm.LdnModels.Where(x =>
                x.Title.Name.ContainsIgnoreCase(searchTerm)
                || x.Title.Id.ContainsIgnoreCase(searchTerm)));

            SortApply();
        }

        private void SetEntries(IEnumerable<LdnGameModel> entries)
        {
            entries ??= [];

            _visibleEntries = entries.ToList();
            OnPropertyChanged(nameof(VisibleEntries));
        }

        private void SortApply()
        {
            try
            {
                _visibleEntries = (_sorting switch
                {
                    (0, 0) => _visibleEntries.OrderBy(x => _sortKeySelector(x) ?? string.Empty), // A - Z
                    (0, 1) => _visibleEntries.OrderByDescending(x => _sortKeySelector(x) ?? string.Empty), // Z - A
                    (1, 0) => _visibleEntries.OrderBy(x => x.PlayerCount).ThenBy(x => x.Title.Name, StringComparer.OrdinalIgnoreCase), // Player count low - high, then A - Z
                    (1, 1) => _visibleEntries.OrderBy(x => x.PlayerCount).ThenByDescending(x => x.Title.Name, StringComparer.OrdinalIgnoreCase), // Player count high - low, then A - Z
                    (2, 0) => _visibleEntries.OrderByDescending(x => x.PlayerCount).ThenBy(x => x.Title.Name, StringComparer.OrdinalIgnoreCase), // Player count low - high, then Z - A
                    (2, 1) => _visibleEntries.OrderByDescending(x => x.PlayerCount).ThenByDescending(x => x.Title.Name, StringComparer.OrdinalIgnoreCase), // Player count high - low, then Z - A
                    _ => _visibleEntries.OrderBy(x => x.PlayerCount)
                }).ToList();
            }
            catch
            {

            }

            OnPropertyChanged(nameof(VisibleEntries));
        }
    }
}
