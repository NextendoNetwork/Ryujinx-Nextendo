using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using System.Runtime.Versioning;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DynamicData;
using DynamicData.Binding;
using FluentAvalonia.UI.Controls;
using Gommon;
using LibHac.Common;
using LibHac.Fs;
using LibHac.Ns;
using LibHac.Tools.FsSystem.NcaUtils;
using Ryujinx.Ava.Common;
using Ryujinx.Ava.Common.Locale;
using Ryujinx.Ava.Common.Models;
using Ryujinx.Ava.Input;
using Ryujinx.Ava.Systems;
using Ryujinx.Ava.Systems.AppLibrary;
using Ryujinx.Ava.Systems.Configuration;
using Ryujinx.Ava.UI.Controls;
using Ryujinx.Ava.UI.Helpers;
using Ryujinx.Ava.UI.Models;
using Ryujinx.Ava.UI.Models.Generic;
using Ryujinx.Ava.UI.Renderer;
using Ryujinx.Ava.UI.Views.Dialog;
using Ryujinx.Ava.UI.Windows;
using Ryujinx.Ava.Utilities;
using Ryujinx.Common;
using Ryujinx.Common.Configuration;
using Ryujinx.Common.Configuration.Multiplayer;
using Ryujinx.Common.Helper;
using Ryujinx.Common.Logging;
using Ryujinx.Common.UI;
using Ryujinx.Common.Utilities;
using Ryujinx.Cpu;
using Ryujinx.Graphics.RenderDocApi;
using Ryujinx.HLE;
using Ryujinx.HLE.FileSystem;
using Ryujinx.HLE.HOS;
using Ryujinx.HLE.HOS.Services.Account.Acc;
using Ryujinx.HLE.HOS.Services.Nfc.AmiiboDecryption;
using Ryujinx.HLE.UI;
using Ryujinx.Input.HLE;
using SkiaSharp;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Key = Ryujinx.Input.Key;
using MissingKeyException = LibHac.Common.Keys.MissingKeyException;
using Path = System.IO.Path;
using ShaderCacheLoadingState = Ryujinx.Graphics.Gpu.Shader.ShaderCacheState;

namespace Ryujinx.Ava.UI.ViewModels
{
    public partial class MainWindowViewModel : BaseModel
    {
        private const int HotKeyPressDelayMs = 500;

        private delegate int LoadContentFromFolderDelegate(List<string> dirs, out int numRemoved);

        [ObservableProperty] public partial ObservableCollectionExtended<ApplicationData> Applications { get; set; }

        [ObservableProperty] public partial string AspectRatioStatusText { get; set; }

        [ObservableProperty] public partial string LoadHeading { get; set; }

        [ObservableProperty] public partial string CacheLoadStatus { get; set; }
        
        [ObservableProperty] public partial string Splash { get; set; }

        [ObservableProperty] public partial string DockedStatusText { get; set; }

        [ObservableProperty] public partial string FifoStatusText { get; set; }

        [ObservableProperty] public partial string GameStatusText { get; set; }

        [ObservableProperty] public partial string VolumeStatusText { get; set; }

        [ObservableProperty] public partial string GpuNameText { get; set; }

        [ObservableProperty] public partial string BackendText { get; set; }

        [ObservableProperty] public partial string ShaderCountText { get; set; }

        [ObservableProperty] public partial bool ShowShaderCompilationHint { get; set; }

        [ObservableProperty] public partial bool IsFullScreen { get; set; }

        [ObservableProperty] public partial int ProgressMaximum { get; set; }

        [ObservableProperty] public partial int ProgressValue { get; set; }

        [ObservableProperty] public partial bool ShowMenuAndStatusBar { get; set; } = true;

        [ObservableProperty] public partial bool ShowStatusSeparator { get; set; }

        [ObservableProperty] public partial Brush ProgressBarForegroundColor { get; set; }

        [ObservableProperty] public partial Brush ProgressBarBackgroundColor { get; set; }

#pragma warning disable MVVMTK0042 // Must stay a normal observable field declaration since this is used as an out parameter target
        [ObservableProperty] private ReadOnlyObservableCollection<ApplicationData> _appsObservableList;

        // [Nextendo] Carousel-specific ordering: favorites, then Nextendo-compatible titles,
        // then everything else, alphabetical within each group. Independent of the user's
        // global sort mode so list/grid views are not affected.
        [ObservableProperty] private ReadOnlyObservableCollection<ApplicationData> _carouselAppsObservableList;
#pragma warning restore MVVMTK0042

        [ObservableProperty] public partial Brush VSyncModeColor { get; set; }
#nullable enable
        [ObservableProperty] public partial byte[]? SelectedIcon { get; set; }
#nullable disable
        [ObservableProperty] public partial int StatusBarProgressMaximum { get; set; }

        [ObservableProperty] public partial int StatusBarProgressValue { get; set; }

        [ObservableProperty] public partial string StatusBarProgressStatusText { get; set; }

        [ObservableProperty] public partial bool StatusBarProgressStatusVisible { get; set; }

        [ObservableProperty] public partial bool IsPaused { get; set; }

        [ObservableProperty] public partial bool IsLoadingIndeterminate { get; set; } = true;

        [ObservableProperty] public partial bool ShowAll { get; set; }

        [ObservableProperty] public partial string LastScannedAmiiboId { get; set; }

        [ObservableProperty] public partial long LastFullscreenToggle { get; set; } = Environment.TickCount64;
        [ObservableProperty] public partial bool ShowContent { get; set; } = true;

        [ObservableProperty] public partial float VolumeBeforeMute { get; set; }

        [ObservableProperty] public partial Cursor Cursor { get; set; }

        [ObservableProperty] public partial string Title { get; set; }

        [ObservableProperty] public partial WindowState WindowState { get; set; }

        [ObservableProperty] public partial double WindowWidth { get; set; }

        [ObservableProperty] public partial double WindowHeight { get; set; }

        [ObservableProperty] public partial bool IsActive { get; set; }

        [ObservableProperty] public partial bool IsSubMenuOpen { get; set; }

        [ObservableProperty] public partial ApplicationContextMenu ListAppContextMenu { get; set; }

        [ObservableProperty] public partial ApplicationContextMenu GridAppContextMenu { get; set; }

        [ObservableProperty] public partial bool IsRyuLdnEnabled { get; set; }

        [ObservableProperty] public partial bool UpdateAvailable { get; set; }

        [ObservableProperty] public partial string SkylanderButtonHeader { get; set; } = string.Empty;

        [ObservableProperty] public partial bool SkylanderButtonEnabled { get; set; }

        public static AsyncRelayCommand UpdateCommand { get; } = Commands.Create(async () =>
        {
            // [Nextendo] Our builds update from the Nextendo release page, not from the Ryujinx
            // update server, which knows nothing about this fork.
            if (Updater.CanUpdate(true))
                await Updater.BeginNextendoUpdateAsync(true);
        });

        private bool _isGameRunning;
        private string _searchText;
        private Timer _searchTimer;
        private string _showUiKey = "F4";
        private string _pauseKey = "F5";
        private string _screenshotKey = "F8";
        private float _volume;
        private ApplicationData _currentApplicationData;
        // [Nextendo] Periodic cloud-save push while a title runs (see LoadApplication).
        private System.Threading.Timer _nextendoSaveTimer;
        private bool _pendingRestart;
        private readonly AutoResetEvent _rendererWaitEvent;
        private int _customVSyncInterval;
        private int _customVSyncIntervalPercentageProxy;

        // Key is Title ID
        /// <summary>
        ///     At any given time, this dictionary contains the filtered data from <see cref="_ldnModels"/>.
        ///     Filtered in this case meaning installed games only.
        /// </summary>
        public SafeDictionary<string, LdnGameModel.Array> UsableLdnData = [];

        private LdnGameModel[] _ldnModels = [];

        public LdnGameModel[] LdnModels
        {
            get => _ldnModels;
            set
            {
                _ldnModels = value;
                LocaleManager.Associate(LocaleKeys.LdnGameListTitle, value.Length);
                LocaleManager.Associate(LocaleKeys.LdnGameListSearchBoxWatermark, value.Length);
                OnPropertyChanged();
            }
        }

        public MainWindow Window { get; init; }

        // [Nextendo] Tile size (width/height) used by the Switch-style game carousel. Driven by
        // the same "Grid size" slider as the list/grid views (1 = small .. 4 = huge) so both
        // library modes stay in sync. Bigger tiles than the old fixed 180, sized to leave room
        // for the selection scale-up without covers stacking on top of each other.
        private static readonly double[] CarouselItemSizes = { 170, 240, 320, 430 };

        public double CarouselItemSize
        {
            get
            {
                int gridSize = Math.Clamp(ConfigurationState.Instance.UI.GridSize - 1, 0, CarouselItemSizes.Length - 1);
                return CarouselItemSizes[gridSize];
            }
        }

        // [Nextendo] Carousel viewport width for exactly five tiles (tile + 32px of margins each,
        // plus the ListBox padding) so the row never shows an arbitrary half-cut tile: you get a
        // clean 5 on screen, or 4-and-a-half with symmetric peeks when the list overflows.
        public double CarouselViewportWidth => CarouselItemSize * 5 + 180;

        internal AppHost AppHost { get; set; }

        public RelayCommand ToggleSkylanderCommand { get; }

        public MainWindowViewModel()
        {
            Applications = [];

            Applications.ToObservableChangeSet()
                .Filter(Filter)
                .Sort(GetComparer())
                .OnItemAdded(_ => OnPropertyChanged(nameof(AppsObservableList)))
                .OnItemRemoved(_ => OnPropertyChanged(nameof(AppsObservableList)))
                .Bind(out _appsObservableList)
                .Subscribe();

            // [Nextendo] Second, fixed-order pipeline that only feeds the game carousel.
            Applications.ToObservableChangeSet()
                .Filter(Filter)
                .Sort(GetCarouselComparer())
                .Bind(out _carouselAppsObservableList)
                .Subscribe();

            _rendererWaitEvent = new AutoResetEvent(false);

            LocaleManager.Instance.PropertyChanged += (sender, args) =>
                {
                    RefreshFileTypeAssociationToggle();
                };

            if (Program.PreviewerDetached)
            {
                LoadConfigurableHotKeys();

                IsRyuLdnEnabled = ConfigurationState.Instance.Multiplayer.Mode.Value is MultiplayerMode.LdnRyu;
                ConfigurationState.Instance.Multiplayer.Mode.Event += OnLdnModeChanged;
                ConfigurationState.Instance.UI.ShowNames.Event += OnShowNamesChanged;

                Volume = ConfigurationState.Instance.System.AudioVolume;
                CustomVSyncInterval = ConfigurationState.Instance.Graphics.CustomVSyncInterval.Value;
            }

            ToggleSkylanderCommand = new RelayCommand(ExecuteToggleSkylander);
        }

        ~MainWindowViewModel()
        {
            if (Program.PreviewerDetached)
            {
                ConfigurationState.Instance.Multiplayer.Mode.Event -= OnLdnModeChanged;
                ConfigurationState.Instance.UI.ShowNames.Event -= OnShowNamesChanged;
            }
        }

        private void OnShowNamesChanged(object sender, ReactiveEventArgs<bool> e) =>
            OnPropertyChanged(nameof(CarouselShowNames));

        private void OnLdnModeChanged(object sender, ReactiveEventArgs<MultiplayerMode> e) =>
            Dispatcher.UIThread.Post(() =>
            {
                IsRyuLdnEnabled = e.NewValue is MultiplayerMode.LdnRyu;
            });

        public void Initialize(
            ContentManager contentManager,
            IStorageProvider storageProvider,
            ApplicationLibrary applicationLibrary,
            VirtualFileSystem virtualFileSystem,
            AccountManager accountManager,
            InputManager inputManager,
            UserChannelPersistence userChannelPersistence,
            LibHacHorizonManager libHacHorizonManager,
            IHostUIHandler uiHandler,
            Action<bool> showLoading,
            Action<bool> switchToGameControl,
            Action<Control> setMainContent,
            TopLevel topLevel)
        {
            ContentManager = contentManager;
            StorageProvider = storageProvider;
            ApplicationLibrary = applicationLibrary;
            VirtualFileSystem = virtualFileSystem;
            AccountManager = accountManager;
            InputManager = inputManager;
            UserChannelPersistence = userChannelPersistence;
            LibHacHorizonManager = libHacHorizonManager;
            UiHandler = uiHandler;

            ShowLoading = showLoading;
            SwitchToGameControl = switchToGameControl;
            SetMainContent = setMainContent;
            TopLevel = topLevel;

#if DEBUG
            topLevel.AttachDevTools(new KeyGesture(Avalonia.Input.Key.F12, KeyModifiers.Control));
#endif

            Window.ApplicationLibrary.TotalTimePlayedRecalculated += TotalTimePlayed_Recalculated;

            // [Nextendo] Start polling the live "N players online" counts now the library exists;
            // the badge next to each supported game reads them.
            Ryujinx.Ava.Common.NextendoOnlineCounts.Start(applicationLibrary);

            // [Nextendo] And the Discord-style avatars of friends currently playing each game,
            // shown as overlapping round pictures on the right of the game row.
            Ryujinx.Ava.Common.NextendoFriendActivity.Start(applicationLibrary);

            // [Nextendo] Friend requests (and friends starting a game) now surface as in-game toasts
            // ONLY — NextendoInGameNotifications, wired via NextendoNotificationOverlayWindow.Attach —
            // and only for events after launch. The old emulator-wide bottom-right request popup is
            // intentionally not started, so notifications never appear outside a game.
        }

        #region Properties

        public string SearchText
        {
            get => _searchText;
            set
            {
                _searchText = value;

                _searchTimer?.Dispose();

                _searchTimer = new Timer(_ =>
                {
                    RefreshView();

                    _searchTimer.Dispose();
                    _searchTimer = null;
                }, null, 1000, 0);
            }
        }

        public bool CanUpdate
        {
            get => field && EnableNonGameRunningControls && Updater.CanUpdate();
            set
            {
                field = value;
                OnPropertyChanged();
            }
        } = true;

        public bool StatusBarVisible
        {
            get => field && EnableNonGameRunningControls;
            set
            {
                field = value;

                OnPropertyChanged();
            }
        }

        public bool EnableNonGameRunningControls => !IsGameRunning;

        public bool ShowFirmwareStatus => !ShowLoadProgress;

        public bool IsGameRunning
        {
            get => _isGameRunning;
            set
            {
                _isGameRunning = value;

                if (!value)
                {
                    ShowMenuAndStatusBar = false;
                }

                OnPropertyChanged();
                OnPropertyChanged(nameof(EnableNonGameRunningControls));
                OnPropertyChanged(nameof(IsAppletMenuActive));
                OnPropertyChanged(nameof(StatusBarVisible));
                OnPropertyChanged(nameof(ShowFirmwareStatus));
            }
        }

        public bool IsAmiiboRequested
        {
            get => field && _isGameRunning;
            set
            {
                field = value;

                OnPropertyChanged();
            }
        }

        public bool IsAmiiboBinRequested
        {
            get => field && _isGameRunning;
            set
            {
                field = value;

                OnPropertyChanged();
            }
        }

        public bool CanScanAmiiboBinaries => AmiiboBinReader.HasAmiiboKeyFile;

        [RelayCommand]
        private void UpdateSkylanderButton()
        {
            SkylanderButtonHeader = HasSkylander
                ? LocaleManager.Instance[LocaleKeys.MenuBar_Actions_RemoveSkylanderButton]
                : LocaleManager.Instance[LocaleKeys.MenuBar_Actions_ScanSkylanderButton];

            SkylanderButtonEnabled = HasSkylander || IsSkylanderRequested;

            OnPropertyChanged(nameof(SkylanderButtonHeader));
            OnPropertyChanged(nameof(SkylanderButtonEnabled));

            Dispatcher.UIThread.Post(() =>
            {
                OnPropertyChanged(nameof(SkylanderButtonHeader));
            }, DispatcherPriority.Render);
        }
        
        private void UpdateSkylanderState()
        {
            if (AppHost?.Device?.System == null) return;

            IsSkylanderRequested = AppHost.Device.System.SearchingForSkylander(out _);
            HasSkylander = AppHost.Device.System.HasSkylander(out _);

            UpdateSkylanderButton();
        }

        private async void ExecuteToggleSkylander()
        {
            if (!IsGameRunning) return;

            if (HasSkylander)
            {
                await RemoveSkylander();
            }
            else
            {
                await ScanSkylander();
            }

            UpdateSkylanderState();
        }

        public bool IsSkylanderRequested
        {
            get => field && _isGameRunning;
            set
            {
                field = value;

                OnPropertyChanged();
                UpdateSkylanderButton();
            }
        }

        public bool HasSkylander
        {
            get => field && _isGameRunning;
            set
            {
                field = value;

                OnPropertyChanged();
                UpdateSkylanderButton();
            }
        }

        public bool ShowLoadProgress
        {
            get;
            set
            {
                field = value;

                OnPropertyChanged();
                OnPropertyChanged(nameof(ShowFirmwareStatus));
            }
        }

        private void TotalTimePlayed_Recalculated(Optional<TimeSpan> ts)
        {
            if (ts.HasValue)
            {
                string formattedPlayTime = ValueFormatUtils.FormatTimeSpan(ts.Value);
                LocaleManager.Associate(LocaleKeys.GameListLabelTotalTimePlayed, formattedPlayTime);
                ShowTotalTimePlayed = formattedPlayTime != string.Empty;
                return;
            }

            ShowTotalTimePlayed = ts.HasValue;
        }

        public bool ShowTotalTimePlayed
        {
            get => field && EnableNonGameRunningControls;
            set
            {
                field = value;
                OnPropertyChanged();
            }
        }

        public ApplicationData ListSelectedApplication
        {
            get;
            set
            {
                field = value;

                if (field != null && ListAppContextMenu == null)
                    ListAppContextMenu = new ApplicationContextMenu();
                else if (field == null && ListAppContextMenu != null)
                    ListAppContextMenu = null!;

                OnPropertyChanged();
            }
        }

        public ApplicationData GridSelectedApplication
        {
            get;
            set
            {
                field = value;

                if (field != null && GridAppContextMenu == null)
                    GridAppContextMenu = new ApplicationContextMenu();
                else if (field == null && GridAppContextMenu != null)
                    GridAppContextMenu = null!;

                OnPropertyChanged();
            }
        }

        public ApplicationData CarouselSelectedApplication
        {
            get;
            set
            {
                field = value;
                OnPropertyChanged();
            }
        }

        public ApplicationData SelectedApplication
        {
            get
            {
                if (IsCarousel)
                {
                    return CarouselSelectedApplication;
                }

                return Glyph switch
                {
                    Glyph.List => ListSelectedApplication,
                    Glyph.Grid => GridSelectedApplication,
                    _ => null,
                };
            }
            set
            {
                ListSelectedApplication = value;
                GridSelectedApplication = value;
                CarouselSelectedApplication = value;
            }
        }

        public bool HasCompatibilityEntry => SelectedApplication.HasPlayabilityInfo;

        public bool IsXCIFile => Path.GetExtension(SelectedApplication.Path)?.ToLower() == ".xci";

        public bool HasDlc => ApplicationLibrary.HasDlcs(SelectedApplication.Id);

        public bool OpenUserSaveDirectoryEnabled => SelectedApplication.HasControlHolder &&
                                                    SelectedApplication.ControlHolder.Value.UserAccountSaveDataSize > 0;

        public bool OpenDeviceSaveDirectoryEnabled => SelectedApplication.HasControlHolder &&
                                                      SelectedApplication.ControlHolder.Value.DeviceSaveDataSize > 0;

        public bool TrimXCIEnabled =>
            XCIFileTrimmer.CanTrim(SelectedApplication.Path, new XCITrimmerLog.MainWindow(this));

        public bool OpenBcatSaveDirectoryEnabled => SelectedApplication.HasControlHolder &&
                                                    SelectedApplication.ControlHolder.Value
                                                        .BcatDeliveryCacheStorageSize > 0;

        public bool ShowCustomVSyncIntervalPicker
            => _isGameRunning && AppHost.Device.VSyncMode == VSyncMode.Custom;

        public void UpdateVSyncIntervalPicker()
        {
            OnPropertyChanged(nameof(ShowCustomVSyncIntervalPicker));
        }

        public int CustomVSyncIntervalPercentageProxy
        {
            get => _customVSyncIntervalPercentageProxy;
            set
            {
                int newInterval = (int)((value / 100f) * 60);
                _customVSyncInterval = newInterval;
                _customVSyncIntervalPercentageProxy = value;
                if (_isGameRunning)
                {
                    AppHost.Device.CustomVSyncInterval = newInterval;
                    AppHost.Device.UpdateVSyncInterval();
                }

                OnPropertyChanged((nameof(CustomVSyncInterval)));
                OnPropertyChanged((nameof(CustomVSyncIntervalPercentageText)));
            }
        }

        public string CustomVSyncIntervalPercentageText
        {
            get
            {
                string text = CustomVSyncIntervalPercentageProxy.ToString() + "%";
                return text;
            }
            set
            {
            }
        }

        public int CustomVSyncInterval
        {
            get => _customVSyncInterval;
            set
            {
                _customVSyncInterval = value;
                int newPercent = (int)((value / 60f) * 100);
                _customVSyncIntervalPercentageProxy = newPercent;
                if (_isGameRunning)
                {
                    AppHost.Device.CustomVSyncInterval = value;
                    AppHost.Device.UpdateVSyncInterval();
                }

                OnPropertyChanged(nameof(CustomVSyncIntervalPercentageProxy));
                OnPropertyChanged(nameof(CustomVSyncIntervalPercentageText));
                OnPropertyChanged();
            }
        }

        public string VSyncModeText
        {
            get;
            set
            {
                field = value;

                OnPropertyChanged();
                OnPropertyChanged(nameof(ShowCustomVSyncIntervalPicker));
            }
        }

        public bool VolumeMuted => _volume == 0;

        public float Volume
        {
            get => _volume;
            set
            {
                _volume = value;

                if (_isGameRunning)
                {
                    AppHost.Device.SetVolume(_volume);
                }

                OnPropertyChanged(nameof(VolumeStatusText));
                OnPropertyChanged(nameof(VolumeMuted));
                OnPropertyChanged();
            }
        }

        public bool IsAppletMenuActive
        {
            get => field && EnableNonGameRunningControls;
            set
            {
                field = value;

                OnPropertyChanged();
            }
        }

        public bool IsGrid => !IsCarousel && Glyph == Glyph.Grid;

        public bool IsList => !IsCarousel && Glyph == Glyph.List;

        // [Nextendo] The Switch-style home carousel is the default launcher view.
        public bool IsCarousel { get; private set; } = true;

        internal void Sort(bool isAscending)
        {
            IsAscending = isAscending;

            RefreshView();
        }

        internal void Sort(ApplicationSort sort)
        {
            SortMode = sort;

            RefreshView();
        }

        public bool StartGamesInFullscreen
        {
            get => ConfigurationState.Instance.UI.StartFullscreen;
            set
            {
                ConfigurationState.Instance.UI.StartFullscreen.Value = value;

                ConfigurationState.Instance.ToFileFormat().SaveConfig(Program.ConfigurationPath);

                OnPropertyChanged();
            }
        }

        public bool StartGamesWithoutUi
        {
            get => ConfigurationState.Instance.UI.StartNoUI;
            set
            {
                ConfigurationState.Instance.UI.StartNoUI.Value = value;

                ConfigurationState.Instance.ToFileFormat().SaveConfig(Program.ConfigurationPath);

                OnPropertyChanged();
            }
        }

        public bool ShowConsole
        {
            get => ConfigurationState.Instance.UI.ShowConsole;
            set
            {
                bool restartRequired = value && !ConsoleHelper.HasConsoleWindow;

                ConfigurationState.Instance.UI.ShowConsole.Value = value;

                ConfigurationState.Instance.ToFileFormat().SaveConfig(Program.ConfigurationPath);

                if (restartRequired)
                {
                    NotificationHelper.ShowInformation(
                        LocaleManager.Instance[LocaleKeys.SettingsAppRequiredRestartMessage],
                        LocaleManager.Instance[LocaleKeys.SettingsShowConsoleRestartMessage]);
                }

                OnPropertyChanged();
            }
        }

        public bool ShowConsoleVisible
        {
            get => ConsoleHelper.SetConsoleWindowStateSupported;
        }

        public Glyph Glyph
        {
            get => (Glyph)ConfigurationState.Instance.UI.GameListViewMode.Value;
            set
            {
                ConfigurationState.Instance.UI.GameListViewMode.Value = (int)value;

                OnPropertyChanged();
                OnPropertyChanged(nameof(IsGrid));
                OnPropertyChanged(nameof(IsList));

                ConfigurationState.Instance.ToFileFormat().SaveConfig(Program.ConfigurationPath);
            }
        }

        public bool ShowNames
        {
            get => ConfigurationState.Instance.UI.ShowNames && ConfigurationState.Instance.UI.GridSize > 1;
            set
            {
                ConfigurationState.Instance.UI.ShowNames.Value = value;

                OnPropertyChanged();
                OnPropertyChanged(nameof(GridSizeScale));
                OnPropertyChanged(nameof(GridItemSelectorSize));

                ConfigurationState.Instance.ToFileFormat().SaveConfig(Program.ConfigurationPath);
            }
        }

        // [Nextendo] Carousel-only read of the "Mostrar Nombres" option. No GridSize gating: it
        // simply decides whether the selected game's name appears under the cover in the carousel.
        // Raised live via OnShowNamesChanged so a Settings toggle applies immediately.
        public bool CarouselShowNames => ConfigurationState.Instance.UI.ShowNames;

        internal ApplicationSort SortMode
        {
            get => (ApplicationSort)ConfigurationState.Instance.UI.ApplicationSort.Value;
            private set
            {
                ConfigurationState.Instance.UI.ApplicationSort.Value = (int)value;

                OnPropertyChanged();
                OnPropertyChanged(nameof(SortName));

                ConfigurationState.Instance.ToFileFormat().SaveConfig(Program.ConfigurationPath);
            }
        }

        public int ListItemSelectorSize
        {
            get
            {
                return ConfigurationState.Instance.UI.GridSize.Value switch
                {
                    1 => 78,
                    2 => 100,
                    3 => 120,
                    4 => 140,
                    _ => 16,
                };
            }
        }

        public int GridItemSelectorSize
        {
            get
            {
                return ConfigurationState.Instance.UI.GridSize.Value switch
                {
                    1 => 120,
                    2 => ShowNames ? 210 : 150,
                    3 => ShowNames ? 240 : 180,
                    4 => ShowNames ? 280 : 220,
                    _ => 16,
                };
            }
        }

        public int GridSizeScale
        {
            get => ConfigurationState.Instance.UI.GridSize;
            set
            {
                ConfigurationState.Instance.UI.GridSize.Value = value;

                if (value < 2)
                {
                    ShowNames = false;
                }

                OnPropertyChanged();
                OnPropertyChanged(nameof(IsGridSmall));
                OnPropertyChanged(nameof(IsGridMedium));
                OnPropertyChanged(nameof(IsGridLarge));
                OnPropertyChanged(nameof(IsGridHuge));
                OnPropertyChanged(nameof(ListItemSelectorSize));
                OnPropertyChanged(nameof(GridItemSelectorSize));
                OnPropertyChanged(nameof(CarouselItemSize));
                OnPropertyChanged(nameof(CarouselViewportWidth));
                OnPropertyChanged(nameof(ShowNames));

                ConfigurationState.Instance.ToFileFormat().SaveConfig(Program.ConfigurationPath);
            }
        }

        public string SortName
        {
            get
            {
                return SortMode switch
                {
                    ApplicationSort.Favorite => LocaleManager.Instance[LocaleKeys.CommonFavorite],
                    ApplicationSort.TitleId => LocaleManager.Instance[LocaleKeys.DlcManagerTableHeadingTitleIdLabel],
                    ApplicationSort.Title => LocaleManager.Instance[LocaleKeys.Common_Sort_NameLabel],
                    ApplicationSort.Developer => LocaleManager.Instance[LocaleKeys.GameListSortDeveloper],
                    ApplicationSort.LastPlayed => LocaleManager.Instance[LocaleKeys.GameListSortLastPlayed],
                    ApplicationSort.TotalTimePlayed => LocaleManager.Instance[LocaleKeys.GameListSortTimePlayed],
                    ApplicationSort.FileType => LocaleManager.Instance[LocaleKeys.GameListSortFileExtension],
                    ApplicationSort.FileSize => LocaleManager.Instance[LocaleKeys.GameListSortFileSize],
                    ApplicationSort.Path => LocaleManager.Instance[LocaleKeys.GameListSortPath],
                    _ => string.Empty,
                };
            }
        }

        public bool IsAscending
        {
            get => ConfigurationState.Instance.UI.IsAscendingOrder;
            private set
            {
                ConfigurationState.Instance.UI.IsAscendingOrder.Value = value;

                OnPropertyChanged();
                OnPropertyChanged(nameof(SortMode));
                OnPropertyChanged(nameof(SortName));

                ConfigurationState.Instance.ToFileFormat().SaveConfig(Program.ConfigurationPath);
            }
        }

        public KeyGesture ShowUiKey
        {
            get => KeyGesture.Parse(_showUiKey);
            set
            {
                _showUiKey = value.ToString();

                OnPropertyChanged();
            }
        }

        public KeyGesture ScreenshotKey
        {
            get => KeyGesture.Parse(_screenshotKey);
            set
            {
                _screenshotKey = value.ToString();

                OnPropertyChanged();
            }
        }

        public KeyGesture PauseKey
        {
            get => KeyGesture.Parse(_pauseKey);
            set
            {
                _pauseKey = value.ToString();

                OnPropertyChanged();
            }
        }

        public ContentManager ContentManager { get; private set; }
        public IStorageProvider StorageProvider { get; private set; }
        public ApplicationLibrary ApplicationLibrary { get; private set; }
        public VirtualFileSystem VirtualFileSystem { get; private set; }
        public AccountManager AccountManager { get; private set; }
        public InputManager InputManager { get; private set; }
        public UserChannelPersistence UserChannelPersistence { get; private set; }
        public Action<bool> ShowLoading { get; private set; }
        public Action<bool> SwitchToGameControl { get; private set; }
        public Action<Control> SetMainContent { get; private set; }
        public TopLevel TopLevel { get; private set; }
        public RendererHost RendererHostControl { get; private set; }
        public bool IsClosing { get; set; }
        public LibHacHorizonManager LibHacHorizonManager { get; internal set; }
        public IHostUIHandler UiHandler { get; internal set; }
        public bool IsSortedByFavorite => SortMode == ApplicationSort.Favorite;
        public bool IsSortedByTitle => SortMode == ApplicationSort.Title;
        public bool IsSortedByTitleId => SortMode == ApplicationSort.TitleId;
        public bool IsSortedByDeveloper => SortMode == ApplicationSort.Developer;
        public bool IsSortedByLastPlayed => SortMode == ApplicationSort.LastPlayed;
        public bool IsSortedByTimePlayed => SortMode == ApplicationSort.TotalTimePlayed;
        public bool IsSortedByType => SortMode == ApplicationSort.FileType;
        public bool IsSortedBySize => SortMode == ApplicationSort.FileSize;
        public bool IsSortedByPath => SortMode == ApplicationSort.Path;
        public bool IsGridSmall => ConfigurationState.Instance.UI.GridSize == 1;
        public bool IsGridMedium => ConfigurationState.Instance.UI.GridSize == 2;
        public bool IsGridLarge => ConfigurationState.Instance.UI.GridSize == 3;
        public bool IsGridHuge => ConfigurationState.Instance.UI.GridSize == 4;

        #endregion

        #region PrivateMethods

        private static SortExpressionComparer<ApplicationData> CreateComparer(bool ascending,
            Func<ApplicationData, IComparable> selector) =>
            ascending
                ? SortExpressionComparer<ApplicationData>.Ascending(selector)
                : SortExpressionComparer<ApplicationData>.Descending(selector);

        private IComparer<ApplicationData> GetComparer()
            => SortMode switch
            {
#pragma warning disable IDE0055 // Disable formatting
                ApplicationSort.Title => CreateComparer(IsAscending, app => app.Name),
                ApplicationSort.Developer => CreateComparer(IsAscending, app => app.Developer),
                ApplicationSort.LastPlayed => new LastPlayedSortComparer(IsAscending),
                ApplicationSort.TotalTimePlayed => new TimePlayedSortComparer(IsAscending),
                ApplicationSort.FileType => CreateComparer(IsAscending, app => app.FileExtension),
                ApplicationSort.FileSize => CreateComparer(IsAscending, app => app.FileSize),
                ApplicationSort.Path => CreateComparer(IsAscending, app => app.Path),
                ApplicationSort.Favorite => CreateComparer(IsAscending, app => new AppListFavoriteComparable(app)),
                ApplicationSort.TitleId => CreateComparer(IsAscending, app => app.Id),
                _ => null,
#pragma warning restore IDE0055
            };

        // [Nextendo] Fixed carousel order: favorites first, then Nextendo-compatible games,
        // then the rest — alphabetical (case-insensitive) inside each group.
        private static IComparer<ApplicationData> GetCarouselComparer()
        {
            return Comparer<ApplicationData>.Create((a, b) =>
            {
                int favorite = b.Favorite.CompareTo(a.Favorite);
                if (favorite != 0)
                {
                    return favorite;
                }

                int nextendo = b.IsNextendoCompatible.CompareTo(a.IsNextendoCompatible);
                if (nextendo != 0)
                {
                    return nextendo;
                }

                return string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
            });
        }

        public void RefreshView()
        {
            RefreshGrid();
        }

        private void RefreshGrid()
        {
            IObservableList<ApplicationData> appsList = Applications.ToObservableChangeSet()
                .Filter(Filter)
                .Sort(GetComparer())
                .Bind(out ReadOnlyObservableCollection<ApplicationData> apps)
                .AsObservableList();

            AppsObservableList = apps;

            // [Nextendo] Rebuild the carousel's fixed ordering too (its comparer ignores SortMode).
            IObservableList<ApplicationData> carouselAppsList = Applications.ToObservableChangeSet()
                .Filter(Filter)
                .Sort(GetCarouselComparer())
                .Bind(out ReadOnlyObservableCollection<ApplicationData> carouselApps)
                .AsObservableList();

            CarouselAppsObservableList = carouselApps;
        }

        private bool Filter(object arg)
        {
            if (arg is ApplicationData app)
            {
                if (string.IsNullOrWhiteSpace(_searchText))
                {
                    return true;
                }

                CompareInfo compareInfo = CultureInfo.CurrentCulture.CompareInfo;

                return compareInfo.IndexOf(app.Name, _searchText,
                    CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace) >= 0;
            }

            return false;
        }

        public bool FileTypeAssociationsVisible
        {
            get => FileAssociationHelper.IsTypeAssociationSupported;
        }

        private void RefreshFileTypeAssociationToggle()
            {
                OnPropertyChanged(nameof(FileTypeAssociationsMenuHeader));
                OnPropertyChanged(nameof(FileTypeAssociationsIcon));
            }

        private bool _areMimeTypesRegistered = FileAssociationHelper.AreMimeTypesRegistered;

        public bool AreMimeTypesRegistered
        {
            get => _areMimeTypesRegistered;
            set
            {
                if (_areMimeTypesRegistered != value)
                {
                    _areMimeTypesRegistered = value;
                    RefreshFileTypeAssociationToggle();
                }
            }
        }

        public string FileTypeAssociationsMenuHeader =>
            AreMimeTypesRegistered
                ? LocaleManager.Instance[LocaleKeys.MenuBar_File_RemoveFileTypeAssociationsButton]
                : LocaleManager.Instance[LocaleKeys.MenuBar_File_AssociateFileTypesButton];

        public string FileTypeAssociationsIcon =>
            AreMimeTypesRegistered
                ? "fa-solid fa-link-slash"
                : "fa-solid fa-link";

        [RelayCommand]
        private async Task ToggleFileTypeAssociations()
        {
            if (AreMimeTypesRegistered)
                await RemoveFileTypeAssociations();
            else
                await AssociateFileTypes();
        }

        [RelayCommand]
        private async Task AssociateFileTypes()
        {
            AreMimeTypesRegistered = FileAssociationHelper.Install();
            if (AreMimeTypesRegistered)
                await ContentDialogHelper.CreateInfoDialog(LocaleManager.Instance[LocaleKeys.Dialog_FileTypeAssociations_AssociationSuccessMessage], string.Empty, LocaleManager.Instance[LocaleKeys.InputDialogOk], string.Empty, string.Empty);
            else
                await ContentDialogHelper.CreateErrorDialog(LocaleManager.Instance[LocaleKeys.Dialog_FileTypeAssociations_AssociationFailedMessage]);
        }

        [RelayCommand]
        private async Task RemoveFileTypeAssociations()
        {
            AreMimeTypesRegistered = !FileAssociationHelper.Uninstall();
            if (!AreMimeTypesRegistered)
                await ContentDialogHelper.CreateInfoDialog(LocaleManager.Instance[LocaleKeys.Dialog_FileTypeAssociations_RemoveAssociationSuccessMessage], string.Empty, LocaleManager.Instance[LocaleKeys.InputDialogOk], string.Empty, string.Empty);
            else
                await ContentDialogHelper.CreateErrorDialog(LocaleManager.Instance[LocaleKeys.Dialog_FileTypeAssociations_RemoveAssociationFailedMessage]);
        }

        public async Task HandleFirmwareInstallation(string filename)
        {
            try
            {
                SystemVersion firmwareVersion = ContentManager.VerifyFirmwarePackage(filename);

                if (firmwareVersion == null)
                {
                    await ContentDialogHelper.CreateErrorDialog(
                        LocaleManager.Instance.UpdateAndGetDynamicValue(
                            LocaleKeys.Dialog_Firmware_InstallerFirmwareNotFound, filename));

                    return;
                }

                string dialogTitle = LocaleManager.Instance.UpdateAndGetDynamicValue(
                    LocaleKeys.Dialog_Firmware_InstallerTitle, firmwareVersion.VersionString);
                string dialogMessage = LocaleManager.Instance.UpdateAndGetDynamicValue(
                    LocaleKeys.Dialog_Firmware_InstallerMainMessage, firmwareVersion.VersionString);

                SystemVersion currentVersion = ContentManager.GetCurrentFirmwareVersion();
                if (currentVersion != null)
                {
                    dialogMessage += LocaleManager.Instance.UpdateAndGetDynamicValue(
                        LocaleKeys.Dialog_Firmware_InstallerSubMessage, currentVersion.VersionString);
                }

                dialogMessage +=
                    LocaleManager.Instance[LocaleKeys.Dialog_Firmware_InstallerConfirmMessage];

                UserResult result = await ContentDialogHelper.CreateConfirmationDialog(
                    dialogTitle,
                    dialogMessage,
                    LocaleManager.Instance[LocaleKeys.InputDialogYes],
                    LocaleManager.Instance[LocaleKeys.InputDialogNo],
                    LocaleManager.Instance[LocaleKeys.RyujinxConfirm]);

                UpdateWaitWindow waitingDialog = new(dialogTitle,
                    LocaleManager.Instance[LocaleKeys.Dialog_Firmware_InstallerWaitMessage]);

                if (result == UserResult.Yes)
                {
                    Logger.Info?.Print(LogClass.Application, $"Installing firmware {firmwareVersion.VersionString}");

                    Thread thread = new(() =>
                    {
                        Dispatcher.UIThread.InvokeAsync(delegate
                        {
                            waitingDialog.Show();
                        });

                        try
                        {
                            ContentManager.InstallFirmware(filename);

                            Dispatcher.UIThread.InvokeAsync(async delegate
                            {
                                waitingDialog.Close();

                                string message = LocaleManager.Instance.UpdateAndGetDynamicValue(
                                    LocaleKeys.Dialog_Firmware_InstallerSuccessMessage,
                                    firmwareVersion.VersionString);

                                await ContentDialogHelper.CreateInfoDialog(
                                    dialogTitle,
                                    message,
                                    LocaleManager.Instance[LocaleKeys.InputDialogOk],
                                    string.Empty,
                                    LocaleManager.Instance[LocaleKeys.RyujinxInfo]);

                                Logger.Info?.Print(LogClass.Application, message);

                                // Purge Applet Cache.

                                DirectoryInfo miiEditorCacheFolder = new(Path.Combine(AppDataManager.GamesDirPath,
                                    "0100000000001009", "cache"));

                                if (miiEditorCacheFolder.Exists)
                                {
                                    miiEditorCacheFolder.Delete(true);
                                }
                            });
                        }
                        catch (Exception ex)
                        {
                            Dispatcher.UIThread.InvokeAsync(async () =>
                            {
                                waitingDialog.Close();

                                await ContentDialogHelper.CreateErrorDialog(ex.Message);
                            });
                        }
                        finally
                        {
                            RefreshFirmwareStatus();
                        }
                    }) { Name = "GUI.FirmwareInstallerThread", };

                    thread.Start();
                }
            }
            catch (MissingKeyException ex)
            {
                if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime)
                {
                    Logger.Error?.Print(LogClass.Application, ex.ToString());

                    await UserErrorDialog.ShowUserErrorDialog(UserError.NoKeys);
                }
            }
            catch (Exception ex)
            {
                await ContentDialogHelper.CreateErrorDialog(ex.Message);
            }
        }

        private async Task HandleKeysInstallation(string filename)
        {
            try
            {
                string systemDirectory = AppDataManager.KeysDirPath;
                if (AppDataManager.Mode == AppDataManager.LaunchMode.UserProfile &&
                    Directory.Exists(AppDataManager.KeysDirPathUser))
                {
                    systemDirectory = AppDataManager.KeysDirPathUser;
                }

                string dialogTitle =
                    LocaleManager.Instance.UpdateAndGetDynamicValue(LocaleKeys.MenuBar_Actions_InstallKeysLabel);
                string dialogMessage =
                    LocaleManager.Instance.UpdateAndGetDynamicValue(LocaleKeys.Dialog_Keys_InstallerMainMessage);

                if (ContentManager.AreKeysAlreadyPresent(systemDirectory))
                {
                    dialogMessage +=
                        LocaleManager.Instance.UpdateAndGetDynamicValue(LocaleKeys
                            .Dialog_Keys_InstallerSubMessage);
                }

                dialogMessage += LocaleManager.Instance[LocaleKeys.Dialog_Keys_InstallerConfirmInstall];

                UserResult result = await ContentDialogHelper.CreateConfirmationDialog(
                    dialogTitle,
                    dialogMessage,
                    LocaleManager.Instance[LocaleKeys.InputDialogYes],
                    LocaleManager.Instance[LocaleKeys.InputDialogNo],
                    LocaleManager.Instance[LocaleKeys.RyujinxConfirm]);

                UpdateWaitWindow waitingDialog = new(dialogTitle,
                    LocaleManager.Instance[LocaleKeys.Dialog_Keys_InstallerWaitMessage]);

                if (result == UserResult.Yes)
                {
                    Logger.Info?.Print(LogClass.Application, $"Installing keys from {filename}");

                    Thread thread = new(() =>
                    {
                        Dispatcher.UIThread.InvokeAsync(delegate
                        {
                            waitingDialog.Show();
                        });

                        try
                        {
                            ContentManager.InstallKeys(filename, systemDirectory);

                            Dispatcher.UIThread.InvokeAsync(async delegate
                            {
                                waitingDialog.Close();

                                string message =
                                    LocaleManager.Instance.UpdateAndGetDynamicValue(LocaleKeys
                                        .Dialog_Keys_InstallerSuccessMessage);

                                await ContentDialogHelper.CreateInfoDialog(
                                    dialogTitle,
                                    message,
                                    LocaleManager.Instance[LocaleKeys.InputDialogOk],
                                    string.Empty,
                                    LocaleManager.Instance[LocaleKeys.RyujinxInfo]);

                                Logger.Info?.Print(LogClass.Application, message);
                            });
                        }
                        catch (Exception ex)
                        {
                            Dispatcher.UIThread.InvokeAsync(async () =>
                            {
                                waitingDialog.Close();

                                string message = ex.Message;
                                if (ex is FormatException)
                                {
                                    message = LocaleManager.Instance.UpdateAndGetDynamicValue(
                                        LocaleKeys.Dialog_Keys_InstallerInvalidKeysFoundMessage, filename);
                                }

                                await ContentDialogHelper.CreateErrorDialog(message);
                            });
                        }
                        finally
                        {
                            VirtualFileSystem.ReloadKeySet();
                        }
                    }) { Name = "GUI.KeysInstallerThread", };

                    thread.Start();
                }
            }
            catch (MissingKeyException ex)
            {
                if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime)
                {
                    Logger.Error?.Print(LogClass.Application, ex.ToString());

                    await UserErrorDialog.ShowUserErrorDialog(UserError.NoKeys);
                }
            }
            catch (Exception ex)
            {
                await ContentDialogHelper.CreateErrorDialog(ex.Message);
            }
        }

        private void ProgressHandler<T>(T state, int current, int total) where T : Enum
        {
            Dispatcher.UIThread.Post(() =>
            {
                ProgressMaximum = total;
                ProgressValue = current;

                switch (state)
                {
                    case LoadState ptcState:
                        CacheLoadStatus = $"{current} / {total}";
                        switch (ptcState)
                        {
                            case LoadState.Unloaded:
                            case LoadState.Loading:
                                LoadHeading = LocaleManager.Instance[LocaleKeys.CompilingPPTC];
                                IsLoadingIndeterminate = false;
                                break;
                            case LoadState.Loaded:
                                LoadHeading = LocaleManager.Instance.UpdateAndGetDynamicValue(LocaleKeys.LoadingHeading,
                                    _currentApplicationData.Name);
                                IsLoadingIndeterminate = true;
                                CacheLoadStatus = string.Empty;
                                break;
                        }

                        break;
                    case ShaderCacheLoadingState shaderCacheState:
                        CacheLoadStatus = $"{current} / {total}";
                        
                        string splash = SplashTextHelper.GetSplash();

                        if (!splash.IsNullOrEmpty())
                        {
                            Splash = $"\"{splash}\"";
                        }
                        switch (shaderCacheState)
                        {
                            case ShaderCacheLoadingState.Start:
                            case ShaderCacheLoadingState.Loading:
                                LoadHeading = LocaleManager.Instance[LocaleKeys.CompilingShaders];
                                IsLoadingIndeterminate = false;
                                break;
                            case ShaderCacheLoadingState.Packaging:
                                LoadHeading = LocaleManager.Instance[LocaleKeys.PackagingShaders];
                                IsLoadingIndeterminate = false;
                                break;
                            case ShaderCacheLoadingState.Loaded:
                                LoadHeading = LocaleManager.Instance.UpdateAndGetDynamicValue(LocaleKeys.LoadingHeading,
                                    _currentApplicationData.Name);
                                IsLoadingIndeterminate = true;
                                CacheLoadStatus = string.Empty;
                                break;
                        }

                        break;
                    default:
                        throw new ArgumentException($"Unknown Progress Handler type {typeof(T)}");
                }
            });
        }

        private void PrepareLoadScreen()
        {
            using MemoryStream stream = new(SelectedIcon);
            using SKBitmap gameIconBmp = SKBitmap.Decode(stream);

            SKColor dominantColor = IconColorPicker.GetFilteredColor(gameIconBmp);

            const float ColorMultiple = 0.5f;

            Color progressFgColor = Color.FromRgb(dominantColor.Red, dominantColor.Green, dominantColor.Blue);
            Color progressBgColor = Color.FromRgb(
                (byte)(dominantColor.Red * ColorMultiple),
                (byte)(dominantColor.Green * ColorMultiple),
                (byte)(dominantColor.Blue * ColorMultiple));

            ProgressBarForegroundColor = new SolidColorBrush(progressFgColor);
            ProgressBarBackgroundColor = new SolidColorBrush(progressBgColor);
        }

        private void InitializeGame()
        {
            RendererHostControl.WindowCreated += RendererHost_Created;

            AppHost.StatusUpdatedEvent += Update_StatusBar;
            AppHost.AppExit += AppHost_AppExit;

            _rendererWaitEvent.WaitOne();

            AppHost?.Start();

            AppHost?.DisposeContext();
        }

        private async Task HandleRelaunch()
        {
            if (UserChannelPersistence.PreviousIndex != -1 && UserChannelPersistence.ShouldRestart)
            {
                UserChannelPersistence.ShouldRestart = false;

                await LoadApplication(_currentApplicationData);
            }
            else if (_pendingRestart)
            {
                _pendingRestart = false;

                Logger.Info?.Print(LogClass.Application, $"Restarting emulation for '{_currentApplicationData.Name}'");

                await LoadApplication(_currentApplicationData);
            }
            else
            {
                // Otherwise, clear state.
                UserChannelPersistence = new UserChannelPersistence();
                _currentApplicationData = null;
            }
        }

        public void RestartEmulation()
        {
            if (AppHost is null || _currentApplicationData is null)
            {
                Logger.Warning?.Print(LogClass.Application, "RestartEmulation called but no application is running.");

                return;
            }

            Logger.Info?.Print(LogClass.Application, $"Restart requested for '{_currentApplicationData.Name}'");

            _pendingRestart = true;
            AppHost.Stop();
        }

        private void Update_StatusBar(object sender, StatusUpdatedEventArgs args)
        {
            if (ShowMenuAndStatusBar && !ShowLoadProgress)
            {
                Dispatcher.UIThread.InvokeAsync(() =>
                {
                    Application.Current!.Styles.TryGetResource(args.VSyncMode,
                        Application.Current.ActualThemeVariant,
                        out object color);

                    if (color is Color clr)
                    {
                        VSyncModeColor = new SolidColorBrush(clr);
                    }

                    VSyncModeText = args.VSyncMode == "Custom" ? "Custom" : "VSync";
                    DockedStatusText = args.DockedMode;
                    AspectRatioStatusText = args.AspectRatio;
                    GameStatusText = args.GameStatus;
                    VolumeStatusText = args.VolumeStatus;
                    FifoStatusText = args.FifoStatus;

                    ShaderCountText = (ShowShaderCompilationHint = args.ShaderCount > 0)
                        ? $"{LocaleManager.Instance[LocaleKeys.CompilingShaders]}: {args.ShaderCount}"
                        : string.Empty;

                    ShowStatusSeparator = true;
                });
            }
        }

        private void RendererHost_Created(object sender, EventArgs e)
        {
            ShowLoading(false);

            _rendererWaitEvent.Set();
        }

        private async Task LoadContentFromFolder(LocaleKeys localeMessageAddedKey, LocaleKeys localeMessageRemovedKey,
            LoadContentFromFolderDelegate onDirsSelected, LocaleKeys dirSelectDialogTitle)
        {
            Optional<IReadOnlyList<IStorageFolder>> result =
                await StorageProvider.OpenMultiFolderPickerAsync(
                    new FolderPickerOpenOptions { Title = LocaleManager.Instance[dirSelectDialogTitle] });

            if (result.TryGet(out IReadOnlyList<IStorageFolder> foldersToLoad))
            {
                List<string> dirs = foldersToLoad.Select(it => it.Path.LocalPath).ToList();
                int numAdded = onDirsSelected(dirs, out int numRemoved);

                string msg = string.Join("\n",
                    string.Format(LocaleManager.Instance[localeMessageRemovedKey], numRemoved),
                    string.Format(LocaleManager.Instance[localeMessageAddedKey], numAdded)
                );

                await Dispatcher.UIThread.InvokeAsync(async () =>
                {
                    await ContentDialogHelper.ShowTextDialog(
                        LocaleManager.Instance[
                            numAdded > 0 || numRemoved > 0 ? LocaleKeys.RyujinxConfirm : LocaleKeys.RyujinxInfo],
                        msg,
                        string.Empty,
                        string.Empty,
                        string.Empty,
                        LocaleManager.Instance[LocaleKeys.InputDialogOk],
                        (int)Symbol.Checkmark);
                });
            }
        }

        #endregion

        #region PublicMethods

        public void SetUiProgressHandlers(Switch emulationContext)
        {
            if (emulationContext.Processes.ActiveApplication.DiskCacheLoadState != null)
            {
                emulationContext.Processes.ActiveApplication.DiskCacheLoadState.StateChanged -= ProgressHandler;
                emulationContext.Processes.ActiveApplication.DiskCacheLoadState.StateChanged += ProgressHandler;
            }

            emulationContext.Gpu.ShaderCacheStateChanged -= ProgressHandler;
            emulationContext.Gpu.ShaderCacheStateChanged += ProgressHandler;
        }

        public void LoadConfigurableHotKeys()
        {
            if (AvaloniaKeyboardMappingHelper.TryGetAvaKey((Key)ConfigurationState.Instance.Hid.Hotkeys.Value.ShowUI,
                    out Avalonia.Input.Key showUiKey))
            {
                ShowUiKey = new KeyGesture(showUiKey);
            }

            if (AvaloniaKeyboardMappingHelper.TryGetAvaKey(
                    (Key)ConfigurationState.Instance.Hid.Hotkeys.Value.Screenshot,
                    out Avalonia.Input.Key screenshotKey))
            {
                ScreenshotKey = new KeyGesture(screenshotKey);
            }

            if (AvaloniaKeyboardMappingHelper.TryGetAvaKey((Key)ConfigurationState.Instance.Hid.Hotkeys.Value.Pause,
                    out Avalonia.Input.Key pauseKey))
            {
                PauseKey = new KeyGesture(pauseKey);
            }
        }

        public void TakeScreenshot()
        {
            AppHost.ScreenshotRequested = true;
        }

        public void HideUi() => ShowMenuAndStatusBar = false;

        public void ToggleShowConsole() => ShowConsole = !ShowConsole;

        private void NotifyViewModes()
        {
            OnPropertyChanged(nameof(IsCarousel));
            OnPropertyChanged(nameof(IsList));
            OnPropertyChanged(nameof(IsGrid));
        }

        public void SetListMode()
        {
            IsCarousel = false;
            Glyph = Glyph.List;
            NotifyViewModes();
        }

        public void SetGridMode()
        {
            IsCarousel = false;
            Glyph = Glyph.Grid;
            NotifyViewModes();
        }

        public void SetCarouselMode()
        {
            IsCarousel = true;
            NotifyViewModes();
        }

        public void SetAspectRatio(AspectRatio aspectRatio) =>
            ConfigurationState.Instance.Graphics.AspectRatio.Value = aspectRatio;

        public async Task InstallFirmwareFromFile()
        {
            Optional<IStorageFile> result = await StorageProvider.OpenSingleFilePickerAsync(new FilePickerOpenOptions
            {
                Title = LocaleManager.Instance[LocaleKeys.Dialog_Firmware_InstallFromFileFilePickerTitle],
                FileTypeFilter = new List<FilePickerFileType>
                {
                    new(LocaleManager.Instance[LocaleKeys.Common_FilePicker_AllSupportedFormats])
                    {
                        Patterns = ["*.xci", "*.zip"],
                        AppleUniformTypeIdentifiers = ["com.ryujinx.xci", "public.zip-archive"],
                        MimeTypes = ["application/x-nx-xci", "application/zip"],
                    },
                    new("XCI")
                    {
                        Patterns = ["*.xci"],
                        AppleUniformTypeIdentifiers = ["com.ryujinx.xci"],
                        MimeTypes = ["application/x-nx-xci"],
                    },
                    new("ZIP")
                    {
                        Patterns = ["*.zip"],
                        AppleUniformTypeIdentifiers = ["public.zip-archive"],
                        MimeTypes = ["application/zip"],
                    },
                },
            });

            if (result.HasValue)
            {
                await HandleFirmwareInstallation(result.Value.Path.LocalPath);
            }
        }

        public async Task InstallFirmwareFromFolder()
        {
            Optional<IStorageFolder> result = await StorageProvider.OpenSingleFolderPickerAsync(new FolderPickerOpenOptions
            {
                Title = LocaleManager.Instance[LocaleKeys.Dialog_Firmware_InstallFromFolderFilePickerTitle]
            });

            if (result.HasValue)
            {
                await HandleFirmwareInstallation(result.Value.Path.LocalPath);
            }
        }

        public async Task InstallKeysFromFile()
        {
            Optional<IStorageFile> result = await StorageProvider.OpenSingleFilePickerAsync(new FilePickerOpenOptions
            {
                Title = LocaleManager.Instance[LocaleKeys.Dialog_Keys_InstallFromFileFilePickerTitle],
                FileTypeFilter = new List<FilePickerFileType>
                {
                    new("KEYS")
                    {
                        Patterns = ["*.keys"],
                        AppleUniformTypeIdentifiers = ["com.ryujinx.xci"],
                        MimeTypes = ["application/keys"],
                    },
                },
            });

            if (result.HasValue)
            {
                await HandleKeysInstallation(result.Value.Path.LocalPath);
            }
        }

        public async Task InstallKeysFromFolder()
        {
            Optional<IStorageFolder> result = await StorageProvider.OpenSingleFolderPickerAsync(new FolderPickerOpenOptions
            {
                Title = LocaleManager.Instance[LocaleKeys.Dialog_Keys_InstallFromFolderFilePickerTitle]
            });

            if (result.HasValue)
            {
                await HandleKeysInstallation(result.Value.Path.LocalPath);
            }
        }

        public void OpenRyujinxFolder()
        {
            OpenHelper.OpenFolder(AppDataManager.BaseDirPath);
        }

        public void OpenScreenshotsFolder()
        {
            string screenshotsDir = Path.Combine(AppDataManager.BaseDirPath, "screenshots");

            try
            {
                if (!Directory.Exists(screenshotsDir))
                    Directory.CreateDirectory(screenshotsDir);
            }
            catch (Exception ex)
            {
                Logger.Error?.Print(LogClass.Application,
                    $"Failed to create directory at path {screenshotsDir}. Error : {ex.GetType().Name}", "Screenshot");

                return;
            }

            OpenHelper.OpenFolder(screenshotsDir);
        }

        public void OpenLogsFolder()
        {
            string logPath = AppDataManager.GetOrCreateLogsDir();
            if (!string.IsNullOrEmpty(logPath))
            {
                OpenHelper.OpenFolder(logPath);
            }
        }

        public void ToggleDockMode()
        {
            if (IsGameRunning)
            {
                ConfigurationState.Instance.System.EnableDockedMode.Toggle();
            }
        }

        public void ToggleVSyncMode()
        {
            AppHost.VSyncModeToggle();
            OnPropertyChanged(nameof(ShowCustomVSyncIntervalPicker));
        }

        public void VSyncModeSettingChanged()
        {
            if (_isGameRunning)
            {
                AppHost.Device.CustomVSyncInterval = ConfigurationState.Instance.Graphics.CustomVSyncInterval.Value;
                AppHost.Device.UpdateVSyncInterval();
            }

            CustomVSyncInterval = ConfigurationState.Instance.Graphics.CustomVSyncInterval.Value;
            OnPropertyChanged(nameof(ShowCustomVSyncIntervalPicker));
            OnPropertyChanged(nameof(CustomVSyncIntervalPercentageProxy));
            OnPropertyChanged(nameof(CustomVSyncIntervalPercentageText));
            OnPropertyChanged(nameof(CustomVSyncInterval));
        }

        public async Task ExitCurrentState()
        {
            if (WindowState is WindowState.FullScreen)
            {
                ToggleFullscreen();
            }
            else if (IsGameRunning)
            {
                await Task.Delay(100);

                AppHost?.ShowExitPrompt();
            }
        }

        public static void ChangeLanguage(object languageCode)
        {
            LocaleManager.Instance.LoadLanguage((string)languageCode);

            if (Program.PreviewerDetached)
            {
                ConfigurationState.Instance.UI.LanguageCode.Value = (string)languageCode;
                ConfigurationState.Instance.ToFileFormat().SaveConfig(Program.ConfigurationPath);
            }
        }

        public async Task ManageProfiles()
        {
            await NavigationDialogHost.Show(AccountManager, ContentManager, VirtualFileSystem,
                LibHacHorizonManager.RyujinxClient);
        }

        public void SimulateWakeUpMessage()
        {
            AppHost.Device.System.SimulateWakeUpMessage();
        }

        public KeyGesture LoadApplicationFromFileGesture => KeyGesture.Parse(OperatingSystem.IsMacOS() ? "Cmd+O" : "Ctrl+O");
        public KeyGesture LoadUnpackedGameFromFolderFileGesture => KeyGesture.Parse(OperatingSystem.IsMacOS() ? "Cmd+Shift+O" : "Ctrl+Shift+O");

        public async Task LoadApplicationFromFile()
        {
            Optional<IStorageFile> result = await StorageProvider.OpenSingleFilePickerAsync(new FilePickerOpenOptions
            {
                Title = LocaleManager.Instance[LocaleKeys.Dialog_FileMenu_LoadApplicationFromFileFilePickerTitle],
                FileTypeFilter = new List<FilePickerFileType>
                {
                    new(LocaleManager.Instance[LocaleKeys.Common_FilePicker_AllSupportedFormats])
                    {
                        Patterns = ["*.nsp", "*.xci", "*.nca", "*.nro", "*.nso"],
                        AppleUniformTypeIdentifiers =
                        [
                            "com.ryujinx.nsp",
                            "com.ryujinx.xci",
                            "com.ryujinx.nca",
                            "com.ryujinx.nro",
                            "com.ryujinx.nso"
                        ],
                        MimeTypes =
                        [
                            "application/x-nx-nsp",
                            "application/x-nx-xci",
                            "application/x-nx-nca",
                            "application/x-nx-nro",
                            "application/x-nx-nso"
                        ],
                    },
                    new("NSP")
                    {
                        Patterns = ["*.nsp"],
                        AppleUniformTypeIdentifiers = ["com.ryujinx.nsp"],
                        MimeTypes = ["application/x-nx-nsp"],
                    },
                    new("XCI")
                    {
                        Patterns = ["*.xci"],
                        AppleUniformTypeIdentifiers = ["com.ryujinx.xci"],
                        MimeTypes = ["application/x-nx-xci"],
                    },
                    new("NCA")
                    {
                        Patterns = ["*.nca"],
                        AppleUniformTypeIdentifiers = ["com.ryujinx.nca"],
                        MimeTypes = ["application/x-nx-nca"],
                    },
                    new("NRO")
                    {
                        Patterns = ["*.nro"],
                        AppleUniformTypeIdentifiers = ["com.ryujinx.nro"],
                        MimeTypes = ["application/x-nx-nro"],
                    },
                    new("NSO")
                    {
                        Patterns = ["*.nso"],
                        AppleUniformTypeIdentifiers = ["com.ryujinx.nso"],
                        MimeTypes = ["application/x-nx-nso"],
                    },
                },
            });

            if (result.HasValue)
            {
                if (ApplicationLibrary.TryGetApplicationsFromFile(result.Value.Path.LocalPath,
                        out List<ApplicationData> applications))
                {
                    await LoadApplication(applications[0]);
                }
                else
                {
                    await ContentDialogHelper.CreateErrorDialog(
                        LocaleManager.Instance[LocaleKeys.Error_NoApplicationFoundInFile]);
                }
            }
        }

        public async Task LoadUnpackedGameFromFolder()
        {
            Optional<IStorageFolder> result = await StorageProvider.OpenSingleFolderPickerAsync(
                new FolderPickerOpenOptions
                {
                    Title = LocaleManager.Instance[LocaleKeys.Dialog_FileMenu_LoadUnpackedApplicationFromFolderFilePickerTitle]
                });

            if (result.TryGet(out IStorageFolder value))
            {
                ApplicationData applicationData = new()
                {
                    Name = Path.GetFileNameWithoutExtension(value.Path.LocalPath), Path = value.Path.LocalPath,
                };

                await LoadApplication(applicationData);
            }
        }

        private async Task<IReadOnlyList<string>> PickFolders(LocaleKeys titleKey)
        {
            return (await StorageProvider.OpenMultiFolderPickerAsync(new FolderPickerOpenOptions 
            { 
                Title = LocaleManager.Instance[titleKey] 
            })).TryGet(out IReadOnlyList<IStorageFolder> folders)
                ? folders.Select(f => f.Path.LocalPath).ToList()
                : null;
        }

        public async Task LoadTitleUpdatesAndDLCFromFolder()
        {
            if (await PickFolders(LocaleKeys.Dialog_FileMenu_LoadUpdatesAndDLCFromFolderFilePickerTitle) is not { } dirs)
                return;

            int updAdded = ApplicationLibrary.AutoLoadTitleUpdates(dirs.ToList(), out _);
            int dlcAdded = ApplicationLibrary.AutoLoadDownloadableContents(dirs.ToList(), out _);

            await Dispatcher.UIThread.InvokeAsync(async () =>
            {
                await ContentDialogHelper.ShowTextDialog(
                    LocaleManager.Instance[LocaleKeys.RyujinxConfirm],
                    string.Format(LocaleManager.Instance[LocaleKeys.Dialog_ContentLoading_UpdatesAddedMessage], updAdded) + "\n\n" +
                    string.Format(LocaleManager.Instance[LocaleKeys.Dialog_ContentLoading_DLCAddedMessage], dlcAdded),
                    string.Empty, string.Empty, string.Empty,
                    LocaleManager.Instance[LocaleKeys.InputDialogOk],
                    (int)Symbol.Checkmark);
            });
        }

        public static bool InitializeUserConfig(ApplicationData application)
        {
            // Code where conditions will be met before loading the user configuration (Global Config)
            string backendThreadingInit = Program.BackendThreadingArg ??
                                          ConfigurationState.Instance.Graphics.BackendThreading.Value.ToString();

            // If a configuration is found in the "/games/xxxxxxxxxxxxxx" folder, the program will load the user setting. 
            string idGame = application.IdBaseString;
            if (ConfigurationFileFormat.TryLoad(Program.GetDirGameUserConfig(idGame),
                    out ConfigurationFileFormat configurationFileFormat))
            {
                // Loads the user configuration, having previously changed the global configuration to the user configuration              
                ConfigurationState.Instance.Load(configurationFileFormat, Program.GetDirGameUserConfig(idGame, true),
                    idGame);

                if (ConfigurationFileFormat.TryLoad(Program.GlobalConfigurationPath,
                        out ConfigurationFileFormat configurationFileFormatExtra))
                {
                    //This is where the global configuration will be stored.
                    //This allows you to change the global configuration settings during the game (for example, the global input setting)
                    ConfigurationState.InstanceExtra.Load(configurationFileFormatExtra,
                        Program.GlobalConfigurationPath);
                }
            }

            // Code where conditions will be executed after loading user configuration
            if (ConfigurationState.Instance.Graphics.BackendThreading.Value.ToString() != backendThreadingInit)
            {
                Rebooter.RebootAppWithGame(application.Path,
                [
                    "--bt",
                    ConfigurationState.Instance.Graphics.BackendThreading.Value.ToString()
                ]);

                return true;
            }

            return false;
        }

        public async Task LoadApplication(ApplicationData application, bool startFullscreen = false,
            BlitStruct<ApplicationControlProperty>? customNacpData = null)
        {
            if (InitializeUserConfig(application))
                return;

            if (AppHost != null)
            {
                await ContentDialogHelper.CreateInfoDialog(
                    LocaleManager.Instance[LocaleKeys.DialogLoadAppGameAlreadyLoadedMessage],
                    LocaleManager.Instance[LocaleKeys.DialogLoadAppGameAlreadyLoadedSubMessage],
                    LocaleManager.Instance[LocaleKeys.InputDialogOk],
                    string.Empty,
                    LocaleManager.Instance[LocaleKeys.RyujinxInfo]);

                return;
            }

            // [Nextendo] Splatoon 3 : prevenir AVANT le lancement que les mods installes seront
            // ignores.
            //
            // Sans ce message, un joueur qui a pose un mod le verrait simplement ne rien faire et
            // conclurait a un bug de l'emulateur. On le dit donc explicitement, on nomme les mods
            // concernes, et on precise que le jeu demarre quand meme — l'interdiction ne bloque pas
            // la partie, elle neutralise le mod. Voir ModLoader.ModsInterdits.
            if (Ryujinx.HLE.HOS.ModLoader.ModsInterdits(application.Id))
            {
                List<string> modsIgnores = Ryujinx.HLE.HOS.ModLoader.ModsInstallesPour(application.Id);

                if (modsIgnores.Count > 0)
                {
                    // On affiche le CHEMIN de chaque dossier, et on dit d'où il vient. Le nom seul
                    // valait « exefs » quand le mod est posé à plat sous le dossier du titre : un
                    // joueur qui n'a rien installé lisait une accusation sans savoir quoi regarder.
                    await ContentDialogHelper.CreateInfoDialog(
                        LocaleManager.Instance[LocaleKeys.Dialog_Nextendo_ModsBlockedTitle],
                        LocaleManager.GetFormatted(
                            LocaleKeys.Dialog_Nextendo_ModsBlockedMessage,
                            string.Join("\n", modsIgnores.Select(m => "• " + m))
                                + "\n\n" + LocaleManager.Instance[LocaleKeys.Dialog_Nextendo_ModsBlockedWhere]),
                        LocaleManager.Instance[LocaleKeys.InputDialogOk],
                        string.Empty,
                        "Nextendo Network");
                }
            }

            // [Nextendo] Pre-launch online gate. Online presents the anonymous NEX id
            // (NetworkServiceAccountId => 0xcafe, which the gated server refuses) — i.e. online
            // is BLOCKED, not just warned — when ANY of these hold:
            //   - the remote beta kill-switch is active / unreachable / this app version is too
            //     old (D5, FAIL CLOSED — no answer = blocked);
            //   - this title is on a version the server doesn't support;
            //   - no online profile exists yet (a guest profile counts as linked).
            // Surface the reason(s) in a single dialog; offline play is never affected.
            // [Nextendo] En mode « serveur personnalisé », toute cette porte saute : elle
            // interroge NOTRE serveur (interrupteur d'arrêt, version supportée) et exige un
            // compte chez NOUS. Quelqu'un qui joue sur son propre serveur n'a rien à voir avec
            // ces conditions, et les lui appliquer reviendrait à l'empêcher de jouer au nom
            // d'un service qu'il a justement désactivé.
            if (application.IsNextendoCompatible && !NextendoServerOverride.HorsNextendo)
            {
                // Evaluate the gate FRESH every launch — OnlineBlocked is runtime-only, so reset
                // it first or a stale "blocked" from a previous launch (or a since-created guest)
                // would wrongly keep online off.
                Ryujinx.Common.Configuration.NextendoAccount.OnlineBlocked = false;

                // First online launch: await one poll so we don't FAIL CLOSED against an
                // up-and-running server just because we hadn't asked it yet.
                if (!Ryujinx.Ava.Common.NextendoBeta.HasPolled)
                {
                    await Ryujinx.Ava.Common.NextendoBeta.RefreshAsync();
                }

                Ryujinx.Ava.Common.NextendoBeta.BlockReason remote = Ryujinx.Ava.Common.NextendoBeta.Evaluate();
                bool versionBad = !application.IsNextendoVersionOk;

                Ryujinx.Common.Configuration.NextendoAccount.OnlineBlocked =
                    remote != Ryujinx.Ava.Common.NextendoBeta.BlockReason.None || versionBad;

                // Refresh the remote config for the NEXT launch (a flipped kill-switch applies then).
                _ = Ryujinx.Ava.Common.NextendoBeta.RefreshAsync();

                if (remote != Ryujinx.Ava.Common.NextendoBeta.BlockReason.None)
                {
                    // Hard remote block (kill-switch / update required / servers unreachable):
                    // online can't work at all. State it plainly (Nintendo-style) and let the
                    // player continue offline.
                    await ContentDialogHelper.CreateInfoDialog(
                        LocaleManager.Instance[LocaleKeys.Dialog_Nextendo_OnlineUnavailableTitle],
                        Ryujinx.Ava.Common.NextendoBeta.Message(remote),
                        LocaleManager.Instance[LocaleKeys.InputDialogOk],
                        string.Empty,
                        "Nextendo Network");
                }
                else
                {
                    System.Collections.Generic.List<string> problems = [];

                    if (versionBad)
                    {
                        problems.Add("• " + LocaleManager.GetFormatted(
                            LocaleKeys.Dialog_Nextendo_OnlineProblemVersion,
                            application.Version, application.NextendoCompatibleVersion));
                    }

                    if (!Ryujinx.Common.Configuration.NextendoAccount.IsLinked)
                    {
                        problems.Add("• " + LocaleManager.Instance[LocaleKeys.Dialog_Nextendo_OnlineProblemNoProfile]);
                    }

                    if (problems.Count > 0)
                    {
                        UserResult onlineChoice = await ContentDialogHelper.CreateConfirmationDialog(
                            LocaleManager.Instance[LocaleKeys.Dialog_Nextendo_OnlineWontWorkTitle],
                            LocaleManager.Instance[LocaleKeys.Dialog_Nextendo_OnlineWontWorkBody] +
                            "\n\n" + string.Join("\n\n", problems),
                            LocaleManager.Instance[LocaleKeys.Dialog_Nextendo_OnlineLaunchAnyway],
                            LocaleManager.Instance[LocaleKeys.Dialog_Nextendo_OnlineCancel],
                            "Nextendo Network");

                        // Cancel -> abort so the user can create a profile / install the right
                        // version first. Launch anyway -> proceed offline.
                        if (onlineChoice != UserResult.Yes)
                        {
                            return;
                        }
                    }
                }
            }

            // [Nextendo] Force-sync the online schedule (BCAT byaml) with the server on every launch
            // of a title that needs it (Splatoon 2). The old flow only offered a download when NO
            // schedule existed locally and never re-checked, so players kept an outdated rotation and
            // hit "communication errors" against players on the current one. Now we compare the local
            // schedule's hash to the server's and force a silent refresh when they differ.
            if (application.RequiresNextendoByaml)
            {
                await Ryujinx.Ava.Common.NextendoByamlSync.EnsureUpToDateAsync(application);
            }

            // [Nextendo] Download this title's cloud save and apply it BEFORE the game
            // runs (compatible titles only; only meaningful because the user owns + is
            // launching this game).
            if (application.IsNextendoVersionOk && Ryujinx.Common.Configuration.NextendoAccount.IsLinked)
            {
                // [Nextendo] If the local save folder is EMPTY but the account has a cloud save,
                // ask the player (Switch-style) before starting: download it, or launch anyway with
                // a warning that a fresh save will overwrite the cloud copy on the next sync. When the
                // local save already has content we never touch it — the push keeps the cloud backed up.
                if (!Ryujinx.Ava.Common.ApplicationHelper.LocalSaveHasContent(application.Id))
                {
                    byte[] cloudSave = await Ryujinx.Ava.Common.NextendoSaveSync.FetchCloudSaveAsync(application);
                    if (cloudSave is { Length: > 0 })
                    {
                        // The overwrite warning is shown INSIDE the download prompt (not after), so the
                        // player knows up-front that launching without downloading will overwrite the cloud copy.
                        UserResult saveChoice = await ContentDialogHelper.CreateConfirmationDialog(
                            LocaleManager.Instance[LocaleKeys.Dialog_Nextendo_CloudSaveEmptyTitle],
                            LocaleManager.GetFormatted(LocaleKeys.Dialog_Nextendo_CloudSaveEmptyFormat, application.Name)
                                + "\n\n" + LocaleManager.Instance[LocaleKeys.Dialog_Nextendo_CloudSaveOverwriteWarn],
                            LocaleManager.Instance[LocaleKeys.Dialog_Nextendo_CloudSaveDownloadBtn],
                            LocaleManager.Instance[LocaleKeys.Dialog_Nextendo_CloudSaveLaunchWithoutBtn],
                            "Nextendo Network",
                            iconSymbol: 0xE753); // Segoe cloud glyph instead of the "?" icon

                        if (saveChoice == UserResult.Yes)
                        {
                            Ryujinx.Ava.Common.ApplicationHelper.ImportNextendoSave(application.Id, application.ControlHolder, cloudSave);
                        }
                    }
                }
            }

            // [Nextendo] Offer the shared community shader cache — only when the user runs the
            // right version AND is linked (same gate as above), and hasn't said "don't ask
            // again". On accept, a progress dialog downloads + installs it into the title's
            // local cache folder so the game stutters far less from the first match.
            if (application.IsNextendoVersionOk && Ryujinx.Common.Configuration.NextendoAccount.IsLinked
                && !Ryujinx.Ava.Common.NextendoCacheSync.IsSkipped(application.IdString))
            {
                long shaderSize = await Ryujinx.Ava.Common.NextendoCacheSync.ShaderAvailableAsync(application);
                if (shaderSize > 0)
                {
                    string sizeStr = $"{shaderSize / (1024.0 * 1024.0):0.#} Mo";
                    string installPath = Ryujinx.Ava.Common.NextendoCacheSync.ShaderInstallPath(application);

                    UserResult choice = await ContentDialogHelper.CreateDeniableConfirmationDialog(
                        LocaleManager.GetFormatted(LocaleKeys.Dialog_Nextendo_ShaderCacheAvailableFormat, application.Name, sizeStr),
                        LocaleManager.GetFormatted(LocaleKeys.Dialog_Nextendo_ShaderCacheDescription, installPath),
                        LocaleManager.Instance[LocaleKeys.Dialog_Nextendo_ByamlDownloadButtonYes],
                        LocaleManager.Instance[LocaleKeys.Dialog_Nextendo_ByamlDownloadButtonNo],
                        LocaleManager.Instance[LocaleKeys.Dialog_Nextendo_ByamlDownloadButtonDontAsk],
                        "Nextendo Network");

                    if (choice == UserResult.Yes)
                    {
                        string installed = await Ryujinx.Ava.Common.NextendoCacheSync.DownloadAndInstallShaderAsync(application);
                        if (installed != null)
                        {
                            await ContentDialogHelper.CreateInfoDialog(
                                LocaleManager.Instance[LocaleKeys.Dialog_Nextendo_ShaderCacheInstalledTitle],
                                LocaleManager.GetFormatted(LocaleKeys.Dialog_Nextendo_ShaderCacheInstalledMessage, installed),
                                LocaleManager.Instance[LocaleKeys.InputDialogOk],
                                string.Empty,
                                "Nextendo Network");
                        }
                    }
                    else if (choice == UserResult.Cancel) // "Ne plus demander"
                    {
                        Ryujinx.Ava.Common.NextendoCacheSync.MarkSkipped(application.IdString);
                    }
                }
            }

#if RELEASE
            await PerformanceCheck();
#endif
            PreLaunchNotification();

            Logger.RestartTime();

            RendererHostControl = new RendererHost();

            AppHost = new AppHost(
                RendererHostControl,
                InputManager,
                application.Path,
                application.Id,
                VirtualFileSystem,
                ContentManager,
                AccountManager,
                UserChannelPersistence,
                this,
                TopLevel);
            
            CancellationTokenSource cts = new CancellationTokenSource();

            try
            {
                await AppHost.LoadGuestApplication(cts, customNacpData);
            }
            catch (OperationCanceledException exception)
            {
                Logger.Info?.Print(LogClass.Application,
                    "LoadGuestApplication was interrupted !!! " + exception.Message);
                AppHost.DisposeContext();
                AppHost = null;

                // [Nextendo] Stop the periodic push + do a final save upload now the game
                // closed. Routed through FlushNextendoSaveAsync so it logs (start / skip-reason)
                // and shares logic with the periodic + window-close paths.
                _nextendoSaveTimer?.Dispose();
                _nextendoSaveTimer = null;
                await FlushNextendoSaveAsync();
                await Ryujinx.Ava.Common.NextendoHistorySync.PushAsync("load-cancel");
                return;
            }
            finally
            {
                cts.Dispose();
            }
            
            CanUpdate = false;

            application.Name ??= AppHost.Device.Processes.ActiveApplication.Name;
            
            SelectedIcon ??= ApplicationLibrary.GetApplicationIcon(application.Path,
                ConfigurationState.Instance.System.Language, application.Id);

            PrepareLoadScreen();

            LoadHeading = LocaleManager.Instance.UpdateAndGetDynamicValue(LocaleKeys.LoadingHeading, application.Name);

            SwitchToRenderer(startFullscreen);

            _currentApplicationData = application;

            // [Nextendo] Periodically upload this title's save WHILE it runs, so progress syncs
            // even when the game exits abnormally (online error, applet exit) — those paths skip
            // AppHost_AppExit and the window-close handler, so an on-stop-only push gets lost.
            // First push after 60s, then every 3 min. Disposed on exit.
            _nextendoSaveTimer?.Dispose();
            _nextendoSaveTimer = null;
            if (application.IsNextendoVersionOk && Ryujinx.Common.Configuration.NextendoAccount.IsLinked)
            {
                // Track the playing title so the crash handler can push its save best-effort.
                Ryujinx.Ava.Common.NextendoSaveSync.Playing = (application.Id, application.IdString);
                _nextendoSaveTimer = new System.Threading.Timer(
                    _ => { if (IsGameRunning) { _ = FlushNextendoSaveAsync(); } },
                    null, TimeSpan.FromSeconds(60), TimeSpan.FromMinutes(3));
            }

            Thread gameThread = new(InitializeGame) { Name = "GUI.WindowThread" };
            gameThread.Start();
        }

        public void SwitchToRenderer(bool startFullscreen) =>
            Dispatcher.UIThread.Post(() =>
            {
                SwitchToGameControl(startFullscreen);

                SetMainContent(RendererHostControl);

                RendererHostControl.Focus();
            });

        public static void UpdateGameMetadata(string titleId, TimeSpan playTime) 
            => ApplicationLibrary.LoadAndSaveMetaData(titleId, appMetadata => appMetadata.UpdatePostGame(playTime));
        
        public void RefreshFirmwareStatus()
        {
            SystemVersion version = null;
            try
            {
                version = ContentManager.GetCurrentFirmwareVersion();
            }
            catch (Exception)
            {
                // ignored
            }

            bool hasApplet = false;

            if (version != null)
            {
                LocaleManager.Instance.UpdateAndGetDynamicValue(LocaleKeys.StatusBar_FirmwareVersionLabel, version.VersionString);

                hasApplet = version.Major > 3;
            }
            else
            {
                LocaleManager.Instance.UpdateAndGetDynamicValue(LocaleKeys.StatusBar_FirmwareVersionLabel, "NaN");
            }

            IsAppletMenuActive = hasApplet;
        }

        // [Nextendo] Push the running title's save to the account. Used by the window-close
        // path (MainWindow.OnClosing), which swaps out AppHost_AppExit for a handler that
        // would otherwise close WITHOUT uploading — so closing the window lost SSBU/MK8 saves.
        // Awaited so the upload finishes before the app exits.
        public async Task FlushNextendoSaveAsync()
        {
            ApplicationData app = _currentApplicationData;
            if (app != null && app.IsNextendoVersionOk && Ryujinx.Common.Configuration.NextendoAccount.IsLinked)
            {
                await Ryujinx.Ava.Common.NextendoSaveSync.PushAsync(app.Id, app.IdString);
            }
            else
            {
                Logger.Info?.Print(LogClass.Application,
                    $"[Nextendo] close-flush skipped (app={app?.Name ?? "null"} versionOk={app?.IsNextendoVersionOk} linked={Ryujinx.Common.Configuration.NextendoAccount.IsLinked})");
            }
        }

        public void AppHost_AppExit(object sender, EventArgs e)
        {
            if (IsClosing)
            {
                return;
            }

            IsGameRunning = false;

            // [Nextendo] Push play history now the game has ended (the Stop action or a caught crash).
            // AppHost.Dispose already wrote this session's play time to the title's metadata, so the
            // push includes it. Fire-and-forget: the UI returns to the game list either way. The
            // window-close path (Alt+F4) is handled separately in MainWindow.OnClosing, which awaits it.
            _ = Ryujinx.Ava.Common.NextendoHistorySync.PushAsync("game-exit");

            Dispatcher.UIThread.InvokeAsync(async () =>
            {
                ShowMenuAndStatusBar = true;
                ShowContent = true;
                ShowLoadProgress = false;
                IsLoadingIndeterminate = false;
                CanUpdate = true;
                Cursor = Cursor.Default;

                SetMainContent(null);

                AppHost = null;

                // [Nextendo] Push the save on a normal Stop / Restart before the UI returns or the
                // game reloads. Without this, Stop/Restart relied only on the periodic 3-min timer,
                // so the last few minutes of progress could be lost. Alt+F4 is handled separately in
                // MainWindow.OnClosing; abnormal exits are still covered by the periodic push.
                _nextendoSaveTimer?.Dispose();
                _nextendoSaveTimer = null;
                await FlushNextendoSaveAsync();
                Ryujinx.Ava.Common.NextendoSaveSync.Playing = null; // normal exit: no crash-flush needed

                await HandleRelaunch();
            });

            RendererHostControl.WindowCreated -= RendererHost_Created;
            RendererHostControl = null;

            SelectedIcon = null;

            Dispatcher.UIThread.InvokeAsync(() =>
            {
                Title = RyujinxApp.FormatTitle();
            });
        }

        /// <summary>
        /// [Nextendo] Ctrl+F, and the Nextendo > Friends menu entry. Non-modal on purpose so it can
        /// be opened, read and left open while a game keeps running.
        /// </summary>
        public void OpenNextendoFriends() => NextendoFriendsWindow.Open();

        /// <summary>
        /// [Nextendo] Ctrl+F now opens the launcher profile dialog (the circular profile
        /// button), which floats above a running game the same way the friends window did.
        /// </summary>
        public void OpenNextendoProfile() => RyujinxApp.MainWindow.ApplicationCarousel?.OpenNextendoProfile();

        public async Task OpenAmiiboWindow()
        {
            if (AppHost.Device.System.SearchingForAmiibo(out int deviceId) && IsGameRunning)
            {
                string titleId = AppHost.Device.Processes.ActiveApplication.ProgramIdText.ToUpper();
                AmiiboWindow window = new(ShowAll, LastScannedAmiiboId ?? string.Empty, titleId);

                await StyleableAppWindow.ShowAsync(window);

                if (window.IsScanned)
                {
                    ShowAll = window.ViewModel.ShowAllAmiibo;
                    LastScannedAmiiboId = window.ScannedAmiibo.GetId();

                    AppHost.Device.System.ScanAmiibo(deviceId, LastScannedAmiiboId, window.ViewModel.UseRandomUuid);
                }
            }
        }

        public async Task ScanAmiiboFromBin()
        {
            bool shouldPause = ConfigurationState.Instance.UI.PauseEmulationWhileScanningAmiibo.Value && IsGameRunning;

            if (shouldPause && AppHost?.Device?.System != null)
            {
                AppHost?.Pause();
            }
            try
            {
                if (AppHost.Device.System.SearchingForAmiibo(out _) && IsGameRunning)
                {
                    Optional<IStorageFile> result = await StorageProvider.OpenSingleFilePickerAsync(
                        new FilePickerOpenOptions
                        {
                            Title = LocaleManager.Instance[LocaleKeys.Dialog_Amiibo_ScanAmiiboFromBinFilePickerTitle],
                            FileTypeFilter = new List<FilePickerFileType>
                            {
                                new(LocaleManager.Instance[LocaleKeys.Common_FilePicker_AllSupportedFormats])
                                {
                                    Patterns = ["*.bin"],
                                }
                            }
                        });

                    if (result.HasValue)
                    {
                        AppHost.Device.System.ScanAmiiboFromBin(result.Value.Path.LocalPath);
                    }
                }
            }
            finally
            {
                if (shouldPause && AppHost?.Device?.System != null)
                {
                    AppHost?.Resume();
                }
            }
        }

        [RelayCommand]
        public async Task ScanSkylander()
        {
            if (AppHost.Device.System.SearchingForSkylander(out int deviceId))
            {
                Optional<IStorageFile> result = await StorageProvider.OpenSingleFilePickerAsync(
                    new FilePickerOpenOptions
                {
                    Title = LocaleManager.Instance[LocaleKeys.Dialog_Skylanders_ScanSkylanderFilePickerTitle],
                    FileTypeFilter = new List<FilePickerFileType>
                {
                    new(LocaleManager.Instance[LocaleKeys.Common_FilePicker_AllSupportedFormats])
                    {
                        Patterns = ["*.sky", "*.bin", "*.dmp", "*.dump"],
                    },
                },
                });
                if (result.HasValue)
                {
                    // Open reading stream from the first file.
                    await using var stream = await result.Value.OpenReadAsync();
                    using var streamReader = new BinaryReader(stream);
                    // Reads all the content of file as a text.
                    byte[] data = new byte[1024];
                    var count = streamReader.Read(data, 0, 1024);
                    if (count < 1024)
                    {
                        return;
                    }
                    else
                    {
                        AppHost.Device.System.ScanSkylander(deviceId, data);
                    }
                }
                UpdateSkylanderButton();
            }
        }

        [RelayCommand]
        public async Task RemoveSkylander()
        {
            AppHost.Device.System.RemoveSkylander();
            UpdateSkylanderButton();
        }

        public void ReloadRenderDocApi()
        {
            RenderDoc.ReloadApi(ignoreAlreadyLoaded: true);

            OnPropertiesChanged(nameof(ShowStartCaptureButton), nameof(ShowEndCaptureButton), nameof(RenderDocIsAvailable));

            if (RenderDoc.IsAvailable)
                RenderDocIsCapturing = RenderDoc.IsFrameCapturing;

            NotificationHelper.ShowInformation(
                "RenderDoc API reloaded",
                RenderDoc.IsAvailable ? "RenderDoc is now available." : "RenderDoc is no longer available."
            );
        }

        public void ToggleCapture()
        {
            if (ShowLoadProgress) return;

            AppHost.RendererHost.EmbeddedWindow.ToggleRenderDocCapture(AppHost.Device);
            RenderDocIsCapturing = RenderDoc.IsFrameCapturing;
        }

        public void ToggleFullscreen()
        {
            if (Environment.TickCount64 - LastFullscreenToggle < HotKeyPressDelayMs)
            {
                return;
            }

            LastFullscreenToggle = Environment.TickCount64;

            if (WindowState is WindowState.FullScreen)
            {
                WindowState = WindowState.Normal;
                Window.TitleBar.ExtendsContentIntoTitleBar = !ConfigurationState.Instance.ShowOldUI;

                if (IsGameRunning)
                {
                    ShowMenuAndStatusBar = true;
                }

                if (OperatingSystem.IsWindows())
                {
                    RestoreWindowFromFullscreen();
                }
            }
            else
            {
                Window.TitleBar.ExtendsContentIntoTitleBar = true;

                if (IsGameRunning)
                {
                    ShowMenuAndStatusBar = false;
                }

                if (OperatingSystem.IsWindows())
                {
                    MakeWindowFullscreen();
                }
                else
                {
                    WindowState = WindowState.FullScreen;
                }
            }

            IsFullScreen = WindowState is WindowState.FullScreen;
        }

        private nint _savedWindowStyle;
        private WindowState _savedWindowState;
        private PixelPoint _savedWindowPosition;
        private double _savedWindowWidth;
        private double _savedWindowHeight;
        private Win32NativeInterop.NativeRect _savedWindowRect;
        private bool _savedWindowRectValid;

        [SupportedOSPlatform("windows")]
        private void MakeWindowFullscreen()
        {
            nint hwnd = Window.TryGetPlatformHandle()?.Handle ?? nint.Zero;
            if (hwnd == nint.Zero) return;

            PixelPoint windowCenter = new(
                Window.Position.X + (int)(Window.Bounds.Width / 2),
                Window.Position.Y + (int)(Window.Bounds.Height / 2));

            Avalonia.Platform.Screen screen =
                Window.Screens.ScreenFromVisual(Window) ??
                Window.Screens.ScreenFromPoint(windowCenter) ??
                Window.Screens.Primary;

            if (screen == null)
            {
                return; // Can't determine screen size, don't attempt fullscreen
            }

            // Save current style and placement
            _savedWindowStyle = Win32NativeInterop.GetWindowLongPtrW(hwnd, Win32NativeInterop.GWL_STYLE);
            _savedWindowState = WindowState;
            _savedWindowPosition = Window.Position;
            _savedWindowWidth = Window.Width;
            _savedWindowHeight = Window.Height;
            _savedWindowRectValid = Win32NativeInterop.GetWindowRect(hwnd, out _savedWindowRect);

            // Remove window chrome: WS_OVERLAPPEDWINDOW -> WS_POPUP | WS_VISIBLE
            Win32NativeInterop.SetWindowLongPtrW(hwnd, Win32NativeInterop.GWL_STYLE,
                unchecked((nint)(Win32NativeInterop.WS_POPUP | Win32NativeInterop.WS_VISIBLE)));

            int w = screen.Bounds.Width;
            int h = screen.Bounds.Height;

            Win32NativeInterop.SetWindowPos(hwnd, nint.Zero, screen.Bounds.X, screen.Bounds.Y, w, h,
                Win32NativeInterop.SWP_NOZORDER | Win32NativeInterop.SWP_NOACTIVATE | Win32NativeInterop.SWP_FRAMECHANGED);

            WindowState = WindowState.FullScreen;
        }

        [SupportedOSPlatform("windows")]
        private void RestoreWindowFromFullscreen()
        {
            nint hwnd = Window.TryGetPlatformHandle()?.Handle ?? nint.Zero;
            if (hwnd == nint.Zero) return;

            // Restore original window style
            Win32NativeInterop.SetWindowLongPtrW(hwnd, Win32NativeInterop.GWL_STYLE, _savedWindowStyle);

            if (_savedWindowState is WindowState.Maximized)
            {
                Win32NativeInterop.SetWindowPos(hwnd, nint.Zero, 0, 0, 0, 0,
                    Win32NativeInterop.SWP_NOZORDER | Win32NativeInterop.SWP_NOACTIVATE |
                    Win32NativeInterop.SWP_FRAMECHANGED | Win32NativeInterop.SWP_NOMOVE | Win32NativeInterop.SWP_NOSIZE);
            }
            else if (_savedWindowRectValid)
            {
                Dispatcher.UIThread.Post(() => RestoreSavedWindowRect(hwnd), DispatcherPriority.Background);
            }
            else
            {
                Win32NativeInterop.SetWindowPos(hwnd, nint.Zero, 0, 0, 0, 0,
                    Win32NativeInterop.SWP_NOZORDER | Win32NativeInterop.SWP_NOACTIVATE |
                    Win32NativeInterop.SWP_FRAMECHANGED | Win32NativeInterop.SWP_NOMOVE | Win32NativeInterop.SWP_NOSIZE);
            }
        }

        [SupportedOSPlatform("windows")]
        private void RestoreSavedWindowRect(nint hwnd)
        {
            Window.Position = _savedWindowPosition;
            Window.Width = _savedWindowWidth;
            Window.Height = _savedWindowHeight;

            Win32NativeInterop.SetWindowPos(hwnd, nint.Zero, _savedWindowRect.Left, _savedWindowRect.Top,
                _savedWindowRect.Width, _savedWindowRect.Height,
                Win32NativeInterop.SWP_NOZORDER | Win32NativeInterop.SWP_NOACTIVATE | Win32NativeInterop.SWP_FRAMECHANGED);
        }

        public static void SaveConfig()
        {
            ConfigurationState.Instance.ToFileFormat().SaveConfig(Program.ConfigurationPath);
        }

        public static async Task PerformanceCheck()
        {
            if (ConfigurationState.Instance.Logger.EnableTrace.Value)
            {
                string mainMessage = LocaleManager.Instance[LocaleKeys.DialogPerformanceCheckLoggingEnabledMessage];
                string secondaryMessage =
                    LocaleManager.Instance[LocaleKeys.DialogPerformanceCheckLoggingEnabledConfirmMessage];

                UserResult result =
                    await ContentDialogHelper.CreateLocalizedConfirmationDialog(mainMessage, secondaryMessage);

                if (result == UserResult.Yes)
                {
                    ConfigurationState.Instance.Logger.EnableTrace.Value = false;

                    SaveConfig();
                }
            }

            if (!string.IsNullOrWhiteSpace(ConfigurationState.Instance.Graphics.ShadersDumpPath.Value))
            {
                string mainMessage = LocaleManager.Instance[LocaleKeys.DialogPerformanceCheckShaderDumpEnabledMessage];
                string secondaryMessage =
                    LocaleManager.Instance[LocaleKeys.DialogPerformanceCheckShaderDumpEnabledConfirmMessage];

                UserResult result =
                    await ContentDialogHelper.CreateLocalizedConfirmationDialog(mainMessage, secondaryMessage);

                if (result == UserResult.Yes)
                {
                    ConfigurationState.Instance.Graphics.ShadersDumpPath.Value = string.Empty;

                    SaveConfig();
                }
            }
        }

        /// <summary>
        /// Show non-intrusive notifications for options that may cause side effects.
        /// </summary>
        public static void PreLaunchNotification()
        {
            if (ConfigurationState.Instance.Debug.DebuggerSuspendOnStart)
            {
                NotificationHelper.ShowInformation(
                    LocaleManager.Instance[LocaleKeys.NotificationLaunchCheckSuspendOnStartTitle],
                    LocaleManager.Instance[LocaleKeys.NotificationLaunchCheckSuspendOnStartMessage]);
            }

            if (ConfigurationState.Instance.Debug.EnableGdbStub)
            {
                NotificationHelper.ShowInformation(
                    LocaleManager.Instance.UpdateAndGetDynamicValue(LocaleKeys.NotificationLaunchCheckGdbStubTitle,
                        ConfigurationState.Instance.Debug.GdbStubPort.Value),
                    LocaleManager.Instance[LocaleKeys.NotificationLaunchCheckGdbStubMessage]);
            }

            if (ConfigurationState.Instance.System.DramSize.Value != MemoryConfiguration.MemoryConfiguration4GiB)
            {
                var memoryConfigurationLocaleKey = ConfigurationState.Instance.System.DramSize.Value switch
                {
                    MemoryConfiguration.MemoryConfiguration4GiB or
                        MemoryConfiguration.MemoryConfiguration4GiBAppletDev or
                        MemoryConfiguration.MemoryConfiguration4GiBSystemDev =>
                        LocaleKeys.SettingsTabSystemDramSize4GiB,
                    MemoryConfiguration.MemoryConfiguration6GiB or
                        MemoryConfiguration.MemoryConfiguration6GiBAppletDev =>
                        LocaleKeys.SettingsTabSystemDramSize6GiB,
                    MemoryConfiguration.MemoryConfiguration8GiB => LocaleKeys.SettingsTabSystemDramSize8GiB,
                    MemoryConfiguration.MemoryConfiguration12GiB => LocaleKeys.SettingsTabSystemDramSize12GiB,
                    _ => LocaleKeys.SettingsTabSystemDramSize4GiB,
                };

                NotificationHelper.ShowWarning(
                    LocaleManager.Instance.UpdateAndGetDynamicValue(
                        LocaleKeys.NotificationLaunchCheckDramSizeTitle,
                        LocaleManager.Instance[memoryConfigurationLocaleKey]
                    ),
                    LocaleManager.Instance[LocaleKeys.NotificationLaunchCheckDramSizeMessage]);
            }
        }

        public async void ProcessTrimResult(String filename, XCIFileTrimmer.OperationOutcome operationOutcome)
        {
            string notifyUser = operationOutcome.LocalizedText;

            if (notifyUser != null)
            {
                await ContentDialogHelper.CreateWarningDialog(
                    LocaleManager.Instance[LocaleKeys.Dialog_XCITrimmer_TrimFailedMessage],
                    notifyUser
                );
            }
            else
            {
                switch (operationOutcome)
                {
                    case XCIFileTrimmer.OperationOutcome.Successful:
                        RyujinxApp.MainWindow.LoadApplications();
                        break;
                }
            }
        }

        public async Task TrimXCIFile(string filename)
        {
            if (filename == null)
            {
                return;
            }

            XCIFileTrimmer trimmer = new(filename, new XCITrimmerLog.MainWindow(this));

            if (trimmer.CanBeTrimmed)
            {
                int savings = (int)Math.Round((double)trimmer.DiskSpaceSavingsB / 1024.0 / 1024.0);
                int currentFileSize = (int)Math.Round((double)trimmer.FileSizeB / 1024.0 / 1024.0);
                int cartDataSize = (int)Math.Round((double)trimmer.DataSizeB / 1024.0 / 1024.0);
                string secondaryText = LocaleManager.Instance.UpdateAndGetDynamicValue(
                    LocaleKeys.Dialog_XCITrimmer_SecondaryMessage, currentFileSize.ToString("0"), cartDataSize.ToString("0"), savings.ToString("0"));

                UserResult result = await ContentDialogHelper.CreateConfirmationDialog(
                    LocaleManager.Instance[LocaleKeys.Dialog_XCITrimmer_PrimaryMessage],
                    secondaryText,
                    LocaleManager.Instance[LocaleKeys.Continue],
                    LocaleManager.Instance[LocaleKeys.Cancel],
                    LocaleManager.Instance[LocaleKeys.GameListContextMenu_TrimXCIButton]
                );

                if (result == UserResult.Yes)
                {
                    Thread XCIFileTrimThread = new(() =>
                    {
                        Dispatcher.UIThread.Post(() =>
                        {
                            StatusBarProgressStatusText =
                                LocaleManager.Instance.UpdateAndGetDynamicValue(LocaleKeys.StatusBar_TrimmingXCILabel,
                                    Path.GetFileNameWithoutExtension(filename));
                            StatusBarProgressStatusVisible = true;
                            StatusBarProgressMaximum = 1;
                            StatusBarProgressValue = 0;
                            StatusBarVisible = true;
                        });

                        try
                        {
                            XCIFileTrimmer.OperationOutcome operationOutcome = trimmer.Trim();

                            Dispatcher.UIThread.Post(() =>
                            {
                                ProcessTrimResult(filename, operationOutcome);
                            });
                        }
                        finally
                        {
                            Dispatcher.UIThread.Post(() =>
                            {
                                StatusBarProgressStatusVisible = false;
                                StatusBarProgressStatusText = string.Empty;
                                StatusBarVisible = false;
                            });
                        }
                    }) { Name = "GUI.XCIFileTrimmerThread", IsBackground = true, };
                    XCIFileTrimThread.Start();
                }
            }
        }

        #endregion

        #region Context Menu commands

        public static AsyncRelayCommand<MainWindowViewModel> RunApplication { get; } =
            Commands.CreateConditional<MainWindowViewModel>(vm => vm?.SelectedApplication != null,
                viewModel => viewModel.LoadApplication(viewModel.SelectedApplication));

        public static RelayCommand<MainWindowViewModel> ToggleFavorite { get; } =
            Commands.CreateConditional<MainWindowViewModel>(vm => vm?.SelectedApplication != null,
                viewModel =>
                {
                    viewModel.SelectedApplication.Favorite = !viewModel.SelectedApplication.Favorite;

                    ApplicationLibrary.LoadAndSaveMetaData(viewModel.SelectedApplication.IdString, appMetadata =>
                    {
                        appMetadata.Favorite = viewModel.SelectedApplication.Favorite;
                    });

                    viewModel.RefreshView();
                }
            );

        public static RelayCommand<MainWindowViewModel> CreateApplicationShortcut { get; } =
            Commands.CreateConditional<MainWindowViewModel>(vm => vm?.SelectedApplication != null,
                viewModel => ShortcutHelper.CreateAppShortcut(
                    viewModel.SelectedApplication.Path,
                    viewModel.SelectedApplication.Name,
                    viewModel.SelectedApplication.IdString,
                    viewModel.SelectedApplication.Icon
                ));

        public static AsyncRelayCommand<MainWindowViewModel> EditGameConfiguration { get; } =
            Commands.CreateConditional<MainWindowViewModel>(vm => vm?.SelectedApplication != null,
                async viewModel =>
                {
                    await StyleableAppWindow.ShowAsync(new GameSpecificSettingsWindow(viewModel));

                    // just checking for file presence
                    viewModel.SelectedApplication.HasIndependentConfiguration = File.Exists(
                        Program.GetDirGameUserConfig(viewModel.SelectedApplication.IdString));

                    viewModel.RefreshView();
                });

        public static AsyncRelayCommand<MainWindowViewModel> OpenApplicationCompatibility { get; } =
            Commands.CreateConditional<MainWindowViewModel>(vm => vm?.SelectedApplication != null,
                viewModel => CompatibilityListWindow.Show(viewModel.SelectedApplication.IdString));

        public static AsyncRelayCommand<MainWindowViewModel> OpenApplicationData { get; } =
            Commands.CreateConditional<MainWindowViewModel>(vm => vm?.SelectedApplication != null,
                viewModel => ApplicationDataView.Show(viewModel.SelectedApplication));

        public static RelayCommand<MainWindowViewModel> OpenUserSaveDirectory { get; } =
            Commands.CreateConditional<MainWindowViewModel>(vm => vm?.SelectedApplication != null,
                viewModel =>
                    OpenSaveDirectory(viewModel, SaveDataType.Account,
                        viewModel.AccountManager.LastOpenedUser.UserId.ToLibHac())
            );

        public static RelayCommand<MainWindowViewModel> OpenDeviceSaveDirectory { get; } =
            Commands.CreateConditional<MainWindowViewModel>(vm => vm?.SelectedApplication != null,
                viewModel => OpenSaveDirectory(viewModel, SaveDataType.Device, default));

        public static RelayCommand<MainWindowViewModel> OpenBcatSaveDirectory { get; } =
            Commands.CreateConditional<MainWindowViewModel>(vm => vm?.SelectedApplication != null,
                viewModel => OpenSaveDirectory(viewModel, SaveDataType.Bcat, default));

        private static void OpenSaveDirectory(MainWindowViewModel viewModel, SaveDataType saveDataType,
            LibHac.Fs.UserId userId)
        {
            SaveDataFilter saveDataFilter = SaveDataFilter.Make(viewModel.SelectedApplication.Id, saveDataType, userId,
                saveDataId: default, index: default);

            ApplicationHelper.OpenSaveDir(in saveDataFilter, viewModel.SelectedApplication.Id,
                viewModel.SelectedApplication.ControlHolder, viewModel.SelectedApplication.Name);
        }

        public static AsyncRelayCommand<MainWindowViewModel> OpenTitleUpdateManager { get; } =
            Commands.CreateConditional<MainWindowViewModel>(vm => vm?.SelectedApplication != null,
                viewModel => TitleUpdateManagerView.Show(viewModel.ApplicationLibrary, viewModel.SelectedApplication)
            );

        public static AsyncRelayCommand<MainWindowViewModel> OpenDownloadableContentManager { get; } =
            Commands.CreateConditional<MainWindowViewModel>(vm => vm?.SelectedApplication != null,
                viewModel =>
                    DownloadableContentManagerView.Show(viewModel.ApplicationLibrary, viewModel.SelectedApplication)
            );

        public static AsyncRelayCommand<MainWindowViewModel> OpenCheatManager { get; } =
            Commands.CreateConditional<MainWindowViewModel>(vm => vm?.SelectedApplication != null,
                viewModel => StyleableAppWindow.ShowAsync(
                    new CheatWindow(
                        viewModel.VirtualFileSystem,
                        viewModel.SelectedApplication.IdString,
                        viewModel.SelectedApplication.Name,
                        viewModel.SelectedApplication.Path
                    )
                ));

        // [Nextendo] (Re)download the online schedule (BCAT byaml) for a compatible title.
        public static AsyncRelayCommand<MainWindowViewModel> DownloadNextendoByaml { get; } =
            Commands.CreateConditional<MainWindowViewModel>(
                vm => vm?.SelectedApplication != null && vm.SelectedApplication.IsNextendoCompatible,
                async viewModel =>
                {
                    ApplicationData app = viewModel.SelectedApplication;
                    bool ok = await Ryujinx.Ava.Common.NextendoByamlSync.DownloadAndInstallAsync(app);
                    await ContentDialogHelper.CreateInfoDialog(
                        LocaleManager.Instance[ok
                            ? LocaleKeys.Dialog_Nextendo_ByamlScheduleInstalledTitle
                            : LocaleKeys.Dialog_Nextendo_ByamlScheduleDownloadFailedTitle],
                        ok
                            ? LocaleManager.Instance.UpdateAndGetDynamicValue(
                                LocaleKeys.Dialog_Nextendo_ByamlScheduleReinstalledFormat, app.Name)
                            : LocaleManager.Instance[LocaleKeys.Dialog_Nextendo_ByamlScheduleDownloadFailedMessage],
                        LocaleManager.Instance[LocaleKeys.InputDialogOk], string.Empty, "Nextendo Network");
                });


        // [Nextendo] Open the GameBanana-backed Mod Store for the selected title (any game — the
        // store lists client-side cosmetic mods for every title, not only Nextendo-compatible ones).
        public static AsyncRelayCommand<MainWindowViewModel> OpenModStore { get; } =
            Commands.CreateConditional<MainWindowViewModel>(
                vm => vm?.SelectedApplication != null,
                viewModel => ModStoreView.Show(
                    viewModel.SelectedApplication.Id,
                    viewModel.SelectedApplication.Name));

        public static AsyncRelayCommand<MainWindowViewModel> OpenModManager { get; } =
            Commands.CreateConditional<MainWindowViewModel>(vm => vm?.SelectedApplication != null,
                viewModel => ModManagerView.Show(
                    viewModel.SelectedApplication.Id,
                    viewModel.SelectedApplication.IdBase,
                    viewModel.ApplicationLibrary,
                    viewModel.SelectedApplication.Name));

        public static RelayCommand<MainWindowViewModel> OpenModsDirectory { get; } =
            Commands.CreateConditional<MainWindowViewModel>(vm => vm?.SelectedApplication != null,
                viewModel =>
                {
                    string modsBasePath = ModLoader.GetModsBasePath();
                    string titleModsPath =
                        ModLoader.GetApplicationDir(modsBasePath, viewModel.SelectedApplication.IdString);

                    OpenHelper.OpenFolder(titleModsPath);
                });

        public static RelayCommand<MainWindowViewModel> OpenSdModsDirectory { get; } =
            Commands.CreateConditional<MainWindowViewModel>(vm => vm?.SelectedApplication != null,
                viewModel =>
                {
                    string sdModsBasePath = ModLoader.GetSdModsBasePath();
                    string titleModsPath =
                        ModLoader.GetApplicationDir(sdModsBasePath, viewModel.SelectedApplication.IdString);

                    OpenHelper.OpenFolder(titleModsPath);
                });

        public static AsyncRelayCommand<MainWindowViewModel> TrimXci { get; } =
            Commands.CreateConditional<MainWindowViewModel>(vm => vm?.SelectedApplication != null,
                viewModel => viewModel.TrimXCIFile(viewModel.SelectedApplication.Path));

        public static AsyncRelayCommand<MainWindowViewModel> PurgePtcCache { get; } =
            Commands.CreateConditional<MainWindowViewModel>(vm => vm?.SelectedApplication != null,
                async viewModel =>
                {
                    UserResult result = await ContentDialogHelper.CreateLocalizedConfirmationDialog(
                        LocaleManager.Instance[LocaleKeys.DialogWarning],
                        LocaleManager.Instance.UpdateAndGetDynamicValue(LocaleKeys.DialogPPTCDeletionMessage,
                            viewModel.SelectedApplication.Name)
                    );

                    if (result == UserResult.Yes)
                    {
                        DirectoryInfo mainDir = new(Path.Combine(AppDataManager.GamesDirPath,
                            viewModel.SelectedApplication.IdString, "cache", "cpu", "0"));
                        DirectoryInfo backupDir = new(Path.Combine(AppDataManager.GamesDirPath,
                            viewModel.SelectedApplication.IdString, "cache", "cpu", "1"));

                        List<FileInfo> cacheFiles = [];

                        if (mainDir.Exists)
                        {
                            cacheFiles.AddRange(mainDir.EnumerateFiles("*.cache"));
                        }

                        if (backupDir.Exists)
                        {
                            cacheFiles.AddRange(backupDir.EnumerateFiles("*.cache"));
                        }

                        if (cacheFiles.Count > 0)
                        {
                            foreach (FileInfo file in cacheFiles)
                            {
                                try
                                {
                                    file.Delete();
                                }
                                catch (Exception ex)
                                {
                                    await ContentDialogHelper.CreateErrorDialog(
                                        LocaleManager.Instance.UpdateAndGetDynamicValue(
                                            LocaleKeys.DialogPPTCDeletionErrorMessage, file.Name, ex));
                                }
                            }
                        }
                    }
                });

        public static AsyncRelayCommand<MainWindowViewModel> NukePtcCache { get; } =
            Commands.CreateConditional<MainWindowViewModel>(vm => vm?.SelectedApplication?.HasPtcCacheFiles ?? false,
                async viewModel =>
                {
                    UserResult result = await ContentDialogHelper.CreateLocalizedConfirmationDialog(
                        LocaleManager.Instance[LocaleKeys.DialogWarning],
                        LocaleManager.Instance.UpdateAndGetDynamicValue(LocaleKeys.DialogPPTCNukeMessage,
                            viewModel.SelectedApplication.Name)
                    );

                    if (result == UserResult.Yes)
                    {
                        DirectoryInfo mainDir = new(Path.Combine(AppDataManager.GamesDirPath,
                            viewModel.SelectedApplication.IdString, "cache", "cpu", "0"));
                        DirectoryInfo backupDir = new(Path.Combine(AppDataManager.GamesDirPath,
                            viewModel.SelectedApplication.IdString, "cache", "cpu", "1"));

                        List<FileInfo> cacheFiles = [];

                        if (mainDir.Exists)
                        {
                            cacheFiles.AddRange(mainDir.EnumerateFiles("*.cache"));
                            cacheFiles.AddRange(mainDir.EnumerateFiles("*.info"));
                        }

                        if (backupDir.Exists)
                        {
                            cacheFiles.AddRange(backupDir.EnumerateFiles("*.cache"));
                            cacheFiles.AddRange(backupDir.EnumerateFiles("*.info"));
                        }

                        if (cacheFiles.Count > 0)
                        {
                            foreach (FileInfo file in cacheFiles)
                            {
                                try
                                {
                                    file.Delete();
                                }
                                catch (Exception ex)
                                {
                                    await ContentDialogHelper.CreateErrorDialog(
                                        LocaleManager.Instance.UpdateAndGetDynamicValue(
                                            LocaleKeys.DialogPPTCDeletionErrorMessage, file.Name, ex));
                                }
                            }
                        }
                    }
                });

        public static AsyncRelayCommand<MainWindowViewModel> PurgeShaderCache { get; } =
            Commands.CreateConditional<MainWindowViewModel>(
                vm => vm?.SelectedApplication?.HasShaderCacheFiles ?? false,
                async viewModel =>
                {
                    UserResult result = await ContentDialogHelper.CreateLocalizedConfirmationDialog(
                        LocaleManager.Instance[LocaleKeys.DialogWarning],
                        LocaleManager.Instance.UpdateAndGetDynamicValue(LocaleKeys.DialogShaderDeletionMessage,
                            viewModel.SelectedApplication.Name)
                    );

                    if (result == UserResult.Yes)
                    {
                        DirectoryInfo shaderCacheDir = new(Path.Combine(AppDataManager.GamesDirPath,
                            viewModel.SelectedApplication.IdString, "cache", "shader"));

                        List<DirectoryInfo> oldCacheDirectories = [];
                        List<FileInfo> newCacheFiles = [];

                        if (shaderCacheDir.Exists)
                        {
                            oldCacheDirectories.AddRange(shaderCacheDir.EnumerateDirectories("*"));
                            newCacheFiles.AddRange(shaderCacheDir.GetFiles("*.toc"));
                            newCacheFiles.AddRange(shaderCacheDir.GetFiles("*.data"));
                        }

                        if ((oldCacheDirectories.Count > 0 || newCacheFiles.Count > 0))
                        {
                            foreach (DirectoryInfo directory in oldCacheDirectories)
                            {
                                try
                                {
                                    directory.Delete(true);
                                }
                                catch (Exception ex)
                                {
                                    await ContentDialogHelper.CreateErrorDialog(
                                        LocaleManager.Instance.UpdateAndGetDynamicValue(
                                            LocaleKeys.DialogPPTCDeletionErrorMessage, directory.Name, ex));
                                }
                            }

                            foreach (FileInfo file in newCacheFiles)
                            {
                                try
                                {
                                    file.Delete();
                                }
                                catch (Exception ex)
                                {
                                    await ContentDialogHelper.CreateErrorDialog(
                                        LocaleManager.Instance.UpdateAndGetDynamicValue(
                                            LocaleKeys.ShaderCachePurgeError, file.Name, ex));
                                }
                            }
                        }
                    }
                });

        public static RelayCommand<MainWindowViewModel> OpenPtcDirectory { get; } =
            Commands.CreateConditional<MainWindowViewModel>(vm => vm?.SelectedApplication != null,
                viewModel =>
                {
                    string ptcDir = Path.Combine(AppDataManager.GamesDirPath, viewModel.SelectedApplication.IdString,
                        "cache", "cpu");
                    string mainDir = Path.Combine(ptcDir, "0");
                    string backupDir = Path.Combine(ptcDir, "1");

                    if (!Directory.Exists(ptcDir))
                    {
                        Directory.CreateDirectory(ptcDir);
                        Directory.CreateDirectory(mainDir);
                        Directory.CreateDirectory(backupDir);
                    }

                    OpenHelper.OpenFolder(ptcDir);
                });

        public static RelayCommand<MainWindowViewModel> OpenShaderCacheDirectory { get; } =
            Commands.CreateConditional<MainWindowViewModel>(vm => vm?.SelectedApplication != null,
                viewModel =>
                {
                    string shaderCacheDir = Path.Combine(AppDataManager.GamesDirPath,
                        viewModel.SelectedApplication.IdString.ToLower(), "cache", "shader");

                    if (!Directory.Exists(shaderCacheDir))
                    {
                        Directory.CreateDirectory(shaderCacheDir);
                    }

                    OpenHelper.OpenFolder(shaderCacheDir);
                });

        public static AsyncRelayCommand<MainWindowViewModel> ExtractApplicationExeFs { get; } =
            Commands.CreateConditional<MainWindowViewModel>(vm => vm?.SelectedApplication != null,
                async viewModel =>
                {
                    await ApplicationHelper.ExtractSection(
                        viewModel.StorageProvider,
                        NcaSectionType.Code,
                        viewModel.SelectedApplication.Path,
                        viewModel.SelectedApplication.Name);
                });

        public static AsyncRelayCommand<MainWindowViewModel> ExtractApplicationRomFs { get; } =
            Commands.CreateConditional<MainWindowViewModel>(vm => vm?.SelectedApplication != null,
                async viewModel =>
                {
                    await ApplicationHelper.ExtractSection(
                        viewModel.StorageProvider,
                        NcaSectionType.Data,
                        viewModel.SelectedApplication.Path,
                        viewModel.SelectedApplication.Name);
                });

        public static AsyncRelayCommand<MainWindowViewModel> ExtractApplicationAocRomFs { get; } =
            Commands.CreateConditional<MainWindowViewModel>(vm => vm?.SelectedApplication != null,
                async viewModel =>
                {
                    DownloadableContentModel selectedDlc = await DlcSelectView.Show(viewModel.SelectedApplication.Id,
                        viewModel.ApplicationLibrary);

                    if (selectedDlc is not null)
                    {
                        await ApplicationHelper.ExtractAoc(
                            viewModel.StorageProvider,
                            selectedDlc.ContainerPath,
                            selectedDlc.FileName);
                    }
                });

        public static AsyncRelayCommand<MainWindowViewModel> ExtractApplicationLogo { get; } =
            Commands.CreateConditional<MainWindowViewModel>(vm => vm?.SelectedApplication != null,
                async viewModel =>
                {
                    Optional<IStorageFolder> resOpt = await viewModel.StorageProvider.OpenSingleFolderPickerAsync(
                        new FolderPickerOpenOptions
                        {
                            Title = LocaleManager.Instance[LocaleKeys.FolderDialogExtractTitle]
                        });

                    if (!resOpt.TryGet(out IStorageFolder result))
                        return;

                    ApplicationHelper.ExtractSection(
                        result.Path.LocalPath,
                        NcaSectionType.Logo,
                        viewModel.SelectedApplication.Path,
                        viewModel.SelectedApplication.Name);

                    IStorageFile iconFile =
                        await result.CreateFileAsync($"{viewModel.SelectedApplication.IdString}.png");
                    await using Stream fileStream = await iconFile.OpenWriteAsync();

                    using SKBitmap bitmap = SKBitmap.Decode(viewModel.SelectedApplication.Icon)
                        .Resize(new SKSizeI(512, 512), SKFilterQuality.High);

                    using SKData png = bitmap.Encode(SKEncodedImageFormat.Png, 100);

                    png.SaveTo(fileStream);
                });

        public bool ShowStartCaptureButton => !RenderDocIsCapturing && RenderDoc.IsAvailable;
        public bool ShowEndCaptureButton => RenderDocIsCapturing && RenderDoc.IsAvailable;
        public static bool RenderDocIsAvailable => RenderDoc.IsAvailable;

        public bool RenderDocIsCapturing
        {
            get;
            set
            {
                field = value;
                OnPropertyChanged();
                OnPropertiesChanged(nameof(ShowStartCaptureButton), nameof(ShowEndCaptureButton));
            }
        }

        public static RelayCommand<MainWindowViewModel> StartRenderDocCapture { get; } =
            Commands.CreateConditional<MainWindowViewModel>(vm => RenderDoc.IsAvailable && !vm.ShowLoadProgress,
                viewModel =>
                {
                    if (!RenderDoc.IsFrameCapturing)
                    {
                        if (viewModel.AppHost.RendererHost
                            .EmbeddedWindow.StartRenderDocCapture(viewModel.AppHost.Device))
                        {
                            Logger.Info?.Print(LogClass.Application, "Starting RenderDoc capture.");
                        }
                    }

                    viewModel.RenderDocIsCapturing = RenderDoc.IsFrameCapturing;
                });

        public static RelayCommand<MainWindowViewModel> EndRenderDocCapture { get; } =
            Commands.CreateConditional<MainWindowViewModel>(vm => RenderDoc.IsAvailable && !vm.ShowLoadProgress,
                viewModel =>
                {
                    if (RenderDoc.IsFrameCapturing)
                    {
                        if (viewModel.AppHost.RendererHost.EmbeddedWindow.EndRenderDocCapture())
                        {
                            Logger.Info?.Print(LogClass.Application, "Ended RenderDoc capture.");
                        }
                    }

                    viewModel.RenderDocIsCapturing = RenderDoc.IsFrameCapturing;
                });

        public static RelayCommand<MainWindowViewModel> DiscardRenderDocCapture { get; } =
            Commands.CreateConditional<MainWindowViewModel>(vm => RenderDoc.IsAvailable  && !vm.ShowLoadProgress,
                viewModel =>
                {
                    if (RenderDoc.IsFrameCapturing)
                    {
                        if (viewModel.AppHost.RendererHost.EmbeddedWindow.DiscardRenderDocCapture())
                        {
                            Logger.Info?.Print(LogClass.Application, "Discarded RenderDoc capture.");
                        }
                    }

                    viewModel.RenderDocIsCapturing = RenderDoc.IsFrameCapturing;
                });

        #endregion
    }
}
