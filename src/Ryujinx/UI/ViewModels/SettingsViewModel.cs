using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LibHac.Tools.FsSystem;
using Ryujinx.Audio.Backends.Apple;
using Ryujinx.Audio.Backends.OpenAL;
using Ryujinx.Audio.Backends.SDL3;
using Ryujinx.Audio.Backends.SoundIo;
using Ryujinx.Ava.Common.Locale;
using Ryujinx.Ava.Systems.Configuration;
using Ryujinx.Ava.Systems.Configuration.System;
using Ryujinx.Ava.Systems.Configuration.UI;
using Ryujinx.Ava.UI.Helpers;
using Ryujinx.Ava.UI.Models.Input;
using Ryujinx.Ava.UI.Windows;
using Ryujinx.Common.Configuration;
using Ryujinx.Common.Configuration.Multiplayer;
using Ryujinx.Common.GraphicsDriver;
using Ryujinx.Common.Helper;
using Ryujinx.Graphics.GAL;
using Ryujinx.Graphics.Vulkan;
using Ryujinx.HLE;
using Ryujinx.HLE.FileSystem;
using Ryujinx.HLE.HOS.Services.Time.TimeZone;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Net.NetworkInformation;
using System.Threading.Tasks;
using TimeZone = Ryujinx.Ava.UI.Models.TimeZone;

namespace Ryujinx.Ava.UI.ViewModels
{
    public partial class SettingsViewModel : BaseModel
    {
        private readonly VirtualFileSystem _virtualFileSystem;
        private readonly ContentManager _contentManager;
        private TimeZoneContentManager _timeZoneContentManager;

        private readonly List<string> _validTzRegions;

        private readonly Dictionary<string, string> _networkInterfaces;

        private int _resolutionScale;
        [ObservableProperty]
        public partial bool IsVulkanAvailable { get; set; } = true;

        [ObservableProperty]
        public partial bool GameListNeedsRefresh { get; set; }

        private readonly List<string> _gpuIds = [];
        private int _scalingFilter;
        private int _customVSyncInterval;
        private int _customVSyncIntervalPercentageProxy;
        private VSyncMode _vSyncMode;

        public event Action CloseWindow;
        public event Action SaveSettingsEvent;
        public event Action<bool> LocalGlobalInputSwitchEvent;
        public SettingsHacksViewModel DirtyHacks { get; }

        public bool IsGameRunning
        {
            get;
        }

        public Bitmap GameIcon
        {
            get;
        }

        public string GamePath
        {
            get;
        }

        public string GameTitle
        {
            get;
        }

        public string GameId
        {
            get;
        }

        public bool IsCustomConfig
        {
            get;
        }

        public bool IsGameTitleNotNull => !string.IsNullOrEmpty(GameTitle);
        public double PanelOpacity => IsGameTitleNotNull ? 0.5 : 1;


        public int ResolutionScale
        {
            get => _resolutionScale;
            set
            {
                _resolutionScale = value;

                OnPropertiesChanged(nameof(CustomResolutionScale), nameof(IsCustomResolutionScaleActive));
            }
        }

        public bool StartGamesInFullscreen
        {
            get => ConfigurationState.Instance.UI.StartFullscreen;
            set
            {
                if (ConfigurationState.Instance.UI.StartFullscreen.Value != value)
                {
                    ConfigurationState.Instance.UI.StartFullscreen.Value = value;
                    OnPropertyChanged();
                }
            }
        }
        public bool StartGamesWithoutUi
        {
            get => ConfigurationState.Instance.UI.StartNoUI;
            set
            {
                if (ConfigurationState.Instance.UI.StartNoUI.Value != value)
                {
                    ConfigurationState.Instance.UI.StartNoUI.Value = value;
                    OnPropertyChanged();
                }
            }
        }

        public int GraphicsBackendMultithreadingIndex
        {
            get;
            set
            {
                field = value;

                if (field != (int)ConfigurationState.Instance.Graphics.BackendThreading.Value)
                {
                    Dispatcher.UIThread.InvokeAsync(() =>
                        ContentDialogHelper.CreateInfoDialog(
                            LocaleManager.Instance[LocaleKeys.DialogSettingsBackendThreadingWarningMessage],
                            string.Empty,
                            string.Empty,
                            LocaleManager.Instance[LocaleKeys.InputDialogOk],
                            LocaleManager.Instance[LocaleKeys.DialogSettingsBackendThreadingWarningTitle])
                    );
                }

                OnPropertyChanged();
            }
        }

        public float CustomResolutionScale
        {
            get;
            set
            {
                field = MathF.Round(value, 1);

                OnPropertyChanged();
            }
        }

        public bool IsOpenGLAvailable => !OperatingSystem.IsMacOS();

        public bool EnableDiscordIntegration { get; set; }
        public bool ShowConfirmExit { get; set; }
        public bool IgnoreApplet { get; set; }
        public bool SkipUserProfiles { get; set; }
        public bool RememberWindowState { get; set; }
        public bool ShowOldUI { get; set; }
        public int HideCursor { get; set; }
        public int UpdateCheckerType { get; set; }
        public bool EnableDockedMode { get; set; }
        public bool EnableKeyboard { get; set; }
        public bool EnableMouse { get; set; }
        public bool DisableInputWhenOutOfFocus { get; set; }        
        public int FocusLostActionType { get; set; }

        public bool UseGlobalInputConfig
        {
            get;
            set
            {
                field = value;
                LocalGlobalInputSwitchEvent?.Invoke(field);
                OnPropertyChanged(nameof(InputPanelOpacity));
                OnPropertyChanged();
            }
        }

        public double InputPanelOpacity => UseGlobalInputConfig ? 0.5 : 1;

        public VSyncMode VSyncMode
        {
            get => _vSyncMode;  
            set
            {
                if (value is VSyncMode.Custom or VSyncMode.Switch or VSyncMode.Unbounded)
                {
                    _vSyncMode = value;
                    OnPropertyChanged();
                }
            }
        }

        public int CustomVSyncIntervalPercentageProxy
        {
            get => _customVSyncIntervalPercentageProxy;
            set
            {
                int newInterval = (int)((value / 100f) * 60);
                _customVSyncInterval = newInterval;
                _customVSyncIntervalPercentageProxy = value;
                OnPropertiesChanged(
                    nameof(CustomVSyncInterval),
                    nameof(CustomVSyncIntervalPercentageText));
            }
        }

        public string CustomVSyncIntervalPercentageText => CustomVSyncIntervalPercentageProxy + "%";

        public bool EnableCustomVSyncInterval
        {
            get;
            set
            {
                field = value;
                if (_vSyncMode == VSyncMode.Custom && !value)
                {
                    VSyncMode = VSyncMode.Switch;
                }
                else if (value)
                {
                    VSyncMode = VSyncMode.Custom;
                }

                OnPropertyChanged();
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
                OnPropertiesChanged(
                    nameof(CustomVSyncIntervalPercentageProxy),
                    nameof(CustomVSyncIntervalPercentageText));
                OnPropertyChanged();
            }
        }
        public bool EnablePptc { get; set; }
        public bool EnableLowPowerPptc { get; set; }


        public long TurboMultiplier
        {
            get;
            set
            {
                if (field != value)
                {
                    field = value;

                    OnPropertyChanged();
                    OnPropertyChanged((nameof(TurboMultiplierPercentageText)));
                }
            }
        }

        public string TurboMultiplierPercentageText => $"{TurboMultiplier}%";

        public bool EnableInternetAccess { get; set; }
        public bool EnableFsIntegrityChecks { get; set; }
        public bool IgnoreMissingServices { get; set; }
        public MemoryConfiguration DramSize { get; set; }
        public bool EnableShaderCache { get; set; }
        public bool EnableTextureRecompression { get; set; }
        public bool EnableMacroHLE { get; set; }
        public bool EnableColorSpacePassthrough { get; set; }
        public bool ColorSpacePassthroughAvailable => RunningPlatform.IsMacOS;
        public bool EnableFileLog { get; set; }
        public bool EnableStub { get; set; }
        public bool EnableInfo { get; set; }
        public bool EnableWarn { get; set; }
        public bool EnableError { get; set; }
        public bool EnableTrace { get; set; }
        public bool EnableGuest { get; set; }
        public bool EnableFsAccessLog { get; set; }
        public bool EnableNetLog { get; set; }
        public bool EnableAvaloniaLog { get; set; }
        public bool EnableDebug { get; set; }
        public bool IsOpenAlEnabled { get; set; }
        public bool IsSoundIoEnabled { get; set; }
        public bool IsSDL3Enabled { get; set; }
        public bool IsAudioToolboxEnabled { get; set; }
        public bool IsCustomResolutionScaleActive => _resolutionScale == 4;
        public bool IsScalingFilterActive => _scalingFilter == (int)Ryujinx.Common.Configuration.ScalingFilter.Fsr;

        public bool IsVulkanSelected =>
            GraphicsBackendIndex == 1 || (GraphicsBackendIndex == 0 && !OperatingSystem.IsMacOS());
        public bool UseHypervisor { get; set; }
        public bool GCLowLatency { get; set; }
        public bool DisableP2P { get; set; }

        public bool ShowDirtyHacks => ConfigurationState.Instance.Hacks.ShowDirtyHacks;

        public string TimeZone { get; set; }
        public string ShaderDumpPath { get; set; }

        public string LdnPassphrase
        {
            get;
            set
            {
                field = value;
                IsInvalidLdnPassphraseVisible = !ValidateLdnPassphrase(value);

                OnPropertyChanged();
                OnPropertyChanged(nameof(IsInvalidLdnPassphraseVisible));
            }
        }

        public int Language { get; set; }
        public int Region { get; set; }
        public int FsGlobalAccessLogMode { get; set; }
        public int AudioBackend { get; set; }
        public int MaxAnisotropy { get; set; }
        public int AspectRatio { get; set; }
        public int AntiAliasingEffect { get; set; }
        public string ScalingFilterLevelText => ScalingFilterLevel.ToString("0");

        public int ScalingFilterLevel
        {
            get;
            set
            {
                field = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(ScalingFilterLevelText));
            }
        }

        public int OpenglDebugLevel { get; set; }
        public int MemoryMode { get; set; }
        public int BaseStyleIndex { get; set; }

        public int GraphicsBackendIndex
        {
            get;
            set
            {
                field = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsVulkanSelected));
            }
        }

        public int ScalingFilter
        {
            get => _scalingFilter;
            set
            {
                _scalingFilter = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsScalingFilterActive));
            }
        }

        public int PreferredGpuIndex { get; set; }

        public float Volume
        {
            get;
            set
            {
                field = value;

                ConfigurationState.Instance.System.AudioVolume.Value = field / 100;

                OnPropertyChanged();
            }
        }

        [ObservableProperty]
        public partial bool MatchSystemTime { get; set; }
        
        public DateTimeOffset CurrentDate { get; set; }

        public TimeSpan CurrentTime { get; set; }

        internal AvaloniaList<TimeZone> TimeZones { get; set; }
        public AvaloniaList<string> GameDirectories { get; set; }
        public AvaloniaList<string> AutoloadDirectories { get; set; }
        public ObservableCollection<ComboBoxItem> AvailableGpus { get; set; }

        public AvaloniaList<string> NetworkInterfaceList
        {
            get => [.. _networkInterfaces.Keys];
        }

        public HotkeyConfig KeyboardHotkey { get; set; }

        public int NetworkInterfaceIndex
        {
            get;
            set
            {
                field = value != -1 ? value : 0;
            }
        }

        public int MultiplayerModeIndex
        {
            get;
            set
            {
                field = value;
            }
        }

        public bool IsInvalidLdnPassphraseVisible { get; set; }

        public bool EnableGdbStub
        {
            get;
            set
            {
                field = value;
                ConfigurationState.Instance.Debug.EnableGdbStub.Value = field;
            }
        }

        public ushort GDBStubPort
        {
            get;
            set
            {
                field = value;
                ConfigurationState.Instance.Debug.GdbStubPort.Value = field;
            }
        }

        public bool DebuggerSuspendOnStart
        {
            get;
            set
            {
                field = value;
                ConfigurationState.Instance.Debug.DebuggerSuspendOnStart.Value = field;
            }
        }

        public SettingsViewModel(VirtualFileSystem virtualFileSystem, ContentManager contentManager) : this()
        {
            _virtualFileSystem = virtualFileSystem;
            _contentManager = contentManager;

            if (Program.PreviewerDetached)
            {
                Task.Run(LoadTimeZones);

                DirtyHacks = new SettingsHacksViewModel(this);
            }
        }

        public SettingsViewModel(
            VirtualFileSystem virtualFileSystem,
            ContentManager contentManager,
            bool gameRunning,
            string gamePath,
            string gameName,
            string gameId,
            byte[] gameIconData,
            bool customConfig) : this()
        {
            _virtualFileSystem = virtualFileSystem;
            _contentManager = contentManager;

            if (gameIconData is { Length: > 0 })
            {
                using MemoryStream ms = new(gameIconData);
                GameIcon = new Bitmap(ms);
            }
            IsCustomConfig = customConfig;
            IsGameRunning = gameRunning;
            GamePath = gamePath;
            GameTitle = gameName;
            GameId = gameId;

            if (customConfig) // During the game. If there is no user config, then load the global config window
            {
                string gameDir = Program.GetDirGameUserConfig(gameId, true);

                Program.UseExtraConfig = true;

                if (ConfigurationFileFormat.TryLoad(Program.GlobalConfigurationPath, out ConfigurationFileFormat configurationFileFormatExtra))
                {
                    // Extra load global configuration for input setting and save global input setting with other global config
                    ConfigurationState.InstanceExtra.Load(configurationFileFormatExtra, Program.GlobalConfigurationPath);
                }

                if (ConfigurationFileFormat.TryLoad(gameDir, out ConfigurationFileFormat configurationFileFormat))
                {
                    ConfigurationState.Instance.Load(configurationFileFormat, gameDir, gameId);
                }

                LoadCurrentConfiguration(); // Needed to load custom configuration
            }

            if (Program.PreviewerDetached)
            {
                Task.Run(LoadTimeZones);
            }
        }

        public SettingsViewModel()
        {
            GameDirectories = [];
            AutoloadDirectories = [];
            TimeZones = [];
            AvailableGpus = [];
            _validTzRegions = [];
            _networkInterfaces = new Dictionary<string, string>();

            Task.Run(CheckSoundBackends);
            Task.Run(PopulateNetworkInterfaces);

            if (Program.PreviewerDetached)
            {
                Task.Run(LoadAvailableGpus);

                // if (!noLoadGlobalConfig)// Default is false, but loading custom config avoids double call
                LoadCurrentConfiguration();

                DirtyHacks = new SettingsHacksViewModel(this);
            }
        }

        public async Task CheckSoundBackends()
        {
            IsOpenAlEnabled = OpenALHardwareDeviceDriver.IsSupported;
            IsSoundIoEnabled = SoundIoHardwareDeviceDriver.IsSupported;
            IsSDL3Enabled = SDL3HardwareDeviceDriver.IsSupported;
            IsAudioToolboxEnabled = OperatingSystem.IsMacOS() && AppleHardwareDeviceDriver.IsSupported;

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                OnPropertyChanged(nameof(IsOpenAlEnabled));
                OnPropertyChanged(nameof(IsSoundIoEnabled));
                OnPropertyChanged(nameof(IsSDL3Enabled));
                OnPropertyChanged(nameof(IsAudioToolboxEnabled));
            });
        }

        private async Task LoadAvailableGpus()
        {
            AvailableGpus.Clear();

            DeviceInfo[] devices = VulkanRenderer.GetPhysicalDevices();

            if (devices.Length == 0)
            {
                IsVulkanAvailable = false;
                GraphicsBackendIndex = 2;
            }
            else
            {
                foreach (DeviceInfo device in devices)
                {
                    await Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        _gpuIds.Add(device.Id);

                        AvailableGpus.Add(new ComboBoxItem { Content = $"{device.Name} {(device.IsDiscrete ? "(dGPU)" : string.Empty)}" });
                    });
                }
            }

            // GPU configuration needs to be loaded during the async method or it will always return 0.
            PreferredGpuIndex = _gpuIds.Contains(ConfigurationState.Instance.Graphics.PreferredGpu) ?
                                _gpuIds.IndexOf(ConfigurationState.Instance.Graphics.PreferredGpu) : 0;

            Dispatcher.UIThread.Post(() => OnPropertyChanged(nameof(PreferredGpuIndex)));
        }

        public async Task LoadTimeZones()
        {
            _timeZoneContentManager = new TimeZoneContentManager();

            _timeZoneContentManager.InitializeInstance(_virtualFileSystem, _contentManager, IntegrityCheckLevel.None);

            foreach ((int offset, string location, string abbr) in _timeZoneContentManager.ParseTzOffsets())
            {
                int hours = Math.DivRem(offset, 3600, out int seconds);
                int minutes = Math.Abs(seconds) / 60;

                string abbr2 = abbr.StartsWith('+') || abbr.StartsWith('-') ? string.Empty : abbr;

                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    TimeZones.Add(new TimeZone($"UTC{hours:+0#;-0#;+00}:{minutes:D2}", location, abbr2));

                    _validTzRegions.Add(location);
                });
            }

            Dispatcher.UIThread.Post(() => OnPropertyChanged(nameof(TimeZone)));
        }

        private async Task PopulateNetworkInterfaces()
        {
            _networkInterfaces.Clear();
            _networkInterfaces.Add(LocaleManager.Instance[LocaleKeys.NetworkInterfaceDefault], "0");

            foreach (NetworkInterface networkInterface in NetworkInterface.GetAllNetworkInterfaces())
            {
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    _networkInterfaces.Add(networkInterface.Name, networkInterface.Id);
                });
            }

            // Network interface index  needs to be loaded during the async method or it will always return 0.
            NetworkInterfaceIndex = _networkInterfaces.Values.ToList().IndexOf(ConfigurationState.Instance.Multiplayer.LanInterfaceId.Value);

            Dispatcher.UIThread.Post(() => OnPropertyChanged(nameof(NetworkInterfaceIndex)));
        }

        private bool ValidateLdnPassphrase(string passphrase)
        {
            return string.IsNullOrEmpty(passphrase) || (passphrase.Length == 16 && Patterns.LdnPassphrase.IsMatch(passphrase));
        }

        public void ValidateAndSetTimeZone(string location)
        {
            if (_validTzRegions.Contains(location))
            {
                TimeZone = location;
            }
        }

        public void LoadCurrentConfiguration(bool global = false)
        {
            ConfigurationState config = global ? ConfigurationState.InstanceExtra: ConfigurationState.Instance;

            // User Interface
            EnableDiscordIntegration = config.EnableDiscordIntegration;
            ShowConfirmExit = config.ShowConfirmExit;
            RememberWindowState = config.RememberWindowState;
            ShowOldUI = config.ShowOldUI;
            HideCursor = (int)config.HideCursor.Value;
            UpdateCheckerType = (int)config.UpdateCheckerType.Value;
            FocusLostActionType = (int)config.FocusLostActionType.Value;
            StartGamesInFullscreen = config.UI.StartFullscreen;
            StartGamesWithoutUi = config.UI.StartNoUI;

            GameDirectories.Clear();
            GameDirectories.AddRange(config.UI.GameDirs.Value);

            AutoloadDirectories.Clear();
            AutoloadDirectories.AddRange(config.UI.AutoloadDirs.Value);

            BaseStyleIndex = config.UI.BaseStyle.Value switch
            {
                "Auto" => 0,
                "Light" => 1,
                "Dark" => 2,
                _ => 0
            };

            // Input
            UseGlobalInputConfig = config.System.UseInputGlobalConfig;
            EnableDockedMode = config.System.EnableDockedMode;
            EnableKeyboard = config.Hid.EnableKeyboard;
            EnableMouse = config.Hid.EnableMouse;
            DisableInputWhenOutOfFocus = config.Hid.DisableInputWhenOutOfFocus;

            // Keyboard Hotkeys
            KeyboardHotkey = new HotkeyConfig(config.Hid.Hotkeys.Value);

            // System
            Region = (int)config.System.Region.Value;
            Language = (int)config.System.Language.Value;
            TimeZone = config.System.TimeZone;

            DateTime currentHostDateTime = DateTime.Now;
            TimeSpan systemDateTimeOffset = TimeSpan.FromSeconds(config.System.SystemTimeOffset);
            DateTime currentDateTime = currentHostDateTime.Add(systemDateTimeOffset);
            CurrentDate = currentDateTime.Date;
            CurrentTime = currentDateTime.TimeOfDay;

            MatchSystemTime = config.System.MatchSystemTime;

            EnableCustomVSyncInterval = config.Graphics.EnableCustomVSyncInterval;
            CustomVSyncInterval = config.Graphics.CustomVSyncInterval;
            VSyncMode = config.Graphics.VSyncMode;
            EnableFsIntegrityChecks = config.System.EnableFsIntegrityChecks;
            DramSize = config.System.DramSize;
            IgnoreMissingServices = config.System.IgnoreMissingServices;
            IgnoreApplet = config.System.IgnoreControllerApplet;
            SkipUserProfiles = config.System.SkipUserProfilesManager;

            // CPU
            EnablePptc = config.System.EnablePtc;
            EnableLowPowerPptc = config.System.EnableLowPowerPtc;
            MemoryMode = (int)config.System.MemoryManagerMode.Value;
            UseHypervisor = config.System.UseHypervisor;
            GCLowLatency = config.System.GCLowLatency;
            TurboMultiplier = config.System.TickScalar;

            // Graphics
            GraphicsBackendIndex = (int)config.Graphics.GraphicsBackend.Value;
            // Physical devices are queried asynchronously hence the preferred index config value is loaded in LoadAvailableGpus().
            EnableShaderCache = config.Graphics.EnableShaderCache;
            EnableTextureRecompression = config.Graphics.EnableTextureRecompression;
            EnableMacroHLE = config.Graphics.EnableMacroHLE;
            EnableColorSpacePassthrough = config.Graphics.EnableColorSpacePassthrough;
            ResolutionScale = config.Graphics.ResScale == -1 ? 4 : config.Graphics.ResScale - 1;
            CustomResolutionScale = config.Graphics.ResScaleCustom;
            MaxAnisotropy = config.Graphics.MaxAnisotropy == -1 ? 0 : (int)MathF.Log2(config.Graphics.MaxAnisotropy);
            AspectRatio = (int)config.Graphics.AspectRatio.Value;
            GraphicsBackendMultithreadingIndex = (int)config.Graphics.BackendThreading.Value;
            ShaderDumpPath = config.Graphics.ShadersDumpPath;
            AntiAliasingEffect = (int)config.Graphics.AntiAliasing.Value;
            ScalingFilter = (int)config.Graphics.ScalingFilter.Value;
            ScalingFilterLevel = config.Graphics.ScalingFilterLevel.Value;

            // Audio
            AudioBackend = (int)config.System.AudioBackend.Value;
            Volume = config.System.AudioVolume * 100;

            // Network
            EnableInternetAccess = config.System.EnableInternetAccess;
            // LAN interface index is loaded asynchronously in PopulateNetworkInterfaces()

            // Logging
            EnableFileLog = config.Logger.EnableFileLog;
            EnableStub = config.Logger.EnableStub;
            EnableInfo = config.Logger.EnableInfo;
            EnableWarn = config.Logger.EnableWarn;
            EnableError = config.Logger.EnableError;
            EnableTrace = config.Logger.EnableTrace;
            EnableGuest = config.Logger.EnableGuest;
            EnableDebug = config.Logger.EnableDebug;
            EnableFsAccessLog = config.Logger.EnableFsAccessLog;
            EnableNetLog = config.Logger.EnableNetLog;
            EnableAvaloniaLog = config.Logger.EnableAvaloniaLog;
            FsGlobalAccessLogMode = config.System.FsGlobalAccessLogMode;
            OpenglDebugLevel = (int)config.Logger.GraphicsDebugLevel.Value;

            // Multiplayer
            MultiplayerModeIndex = (int)config.Multiplayer.Mode.Value;
            DisableP2P = config.Multiplayer.DisableP2p;
            LdnPassphrase = config.Multiplayer.LdnPassphrase;

            // Debug
            EnableGdbStub = config.Debug.EnableGdbStub.Value;
            GDBStubPort = config.Debug.GdbStubPort.Value;
            DebuggerSuspendOnStart = config.Debug.DebuggerSuspendOnStart.Value;
        }

        public void SaveSettings(bool global = false)
        {
            ConfigurationState config = global ? ConfigurationState.InstanceExtra: ConfigurationState.Instance;

            // User Interface
            config.EnableDiscordIntegration.Value = EnableDiscordIntegration;
            config.ShowConfirmExit.Value = ShowConfirmExit;
            config.RememberWindowState.Value = RememberWindowState;
            config.ShowOldUI.Value = ShowOldUI;
            config.HideCursor.Value = (HideCursorMode)HideCursor;
            config.UpdateCheckerType.Value = (UpdaterType)UpdateCheckerType;
            config.FocusLostActionType.Value = (FocusLostType)FocusLostActionType;
            config.UI.GameDirs.Value = [.. GameDirectories];
            config.UI.AutoloadDirs.Value = [.. AutoloadDirectories];
            config.UI.StartFullscreen.Value = StartGamesInFullscreen;
            config.UI.StartNoUI.Value = StartGamesWithoutUi;

            config.UI.BaseStyle.Value = BaseStyleIndex switch
            {
                0 => "Auto",
                1 => "Light",
                2 => "Dark",
                _ => "Auto"
            };

            // Input
            config.System.UseInputGlobalConfig.Value = UseGlobalInputConfig;
            config.System.EnableDockedMode.Value = EnableDockedMode;
            config.Hid.EnableKeyboard.Value = EnableKeyboard;
            config.Hid.EnableMouse.Value = EnableMouse;
            config.Hid.DisableInputWhenOutOfFocus.Value = DisableInputWhenOutOfFocus;

            // Keyboard Hotkeys
            config.Hid.Hotkeys.Value = KeyboardHotkey.GetConfig();

            // System
            config.System.Region.Value = (Region)Region;

            if (config.System.Language.Value != (Language)Language)
                GameListNeedsRefresh = true;

            config.System.Language.Value = (Language)Language;
            if (_validTzRegions.Contains(TimeZone))
            {
                config.System.TimeZone.Value = TimeZone;
            }

            config.System.MatchSystemTime.Value = MatchSystemTime;
            config.System.SystemTimeOffset.Value = Convert.ToInt64((CurrentDate.ToUnixTimeSeconds() + CurrentTime.TotalSeconds) - DateTimeOffset.Now.ToUnixTimeSeconds());
            config.System.EnableFsIntegrityChecks.Value = EnableFsIntegrityChecks;
            config.System.DramSize.Value = DramSize;
            config.System.IgnoreMissingServices.Value = IgnoreMissingServices;
            config.System.IgnoreControllerApplet.Value = IgnoreApplet;
            config.System.SkipUserProfilesManager.Value = SkipUserProfiles;

            // CPU
            config.System.EnablePtc.Value = EnablePptc;
            config.System.EnableLowPowerPtc.Value = EnableLowPowerPptc;
            config.System.MemoryManagerMode.Value = (MemoryManagerMode)MemoryMode;
            config.System.UseHypervisor.Value = UseHypervisor;
            config.System.GCLowLatency.Value = GCLowLatency;
            config.System.TickScalar.Value = TurboMultiplier;

            // Graphics
            config.Graphics.VSyncMode.Value = VSyncMode;
            config.Graphics.EnableCustomVSyncInterval.Value = EnableCustomVSyncInterval;
            config.Graphics.CustomVSyncInterval.Value = CustomVSyncInterval;
            config.Graphics.GraphicsBackend.Value = (GraphicsBackend)GraphicsBackendIndex;
            config.Graphics.PreferredGpu.Value = _gpuIds.ElementAtOrDefault(PreferredGpuIndex);
            config.Graphics.EnableShaderCache.Value = EnableShaderCache;
            config.Graphics.EnableTextureRecompression.Value = EnableTextureRecompression;
            config.Graphics.EnableMacroHLE.Value = EnableMacroHLE;
            config.Graphics.EnableColorSpacePassthrough.Value = EnableColorSpacePassthrough;
            config.Graphics.ResScale.Value = ResolutionScale == 4 ? -1 : ResolutionScale + 1;
            config.Graphics.ResScaleCustom.Value = CustomResolutionScale;
            config.Graphics.MaxAnisotropy.Value = MaxAnisotropy == 0 ? -1 : MathF.Pow(2, MaxAnisotropy);
            config.Graphics.AspectRatio.Value = (AspectRatio)AspectRatio;
            config.Graphics.AntiAliasing.Value = (AntiAliasing)AntiAliasingEffect;
            config.Graphics.ScalingFilter.Value = (ScalingFilter)ScalingFilter;
            config.Graphics.ScalingFilterLevel.Value = ScalingFilterLevel;

            if (ConfigurationState.Instance.Graphics.BackendThreading != (BackendThreading)GraphicsBackendMultithreadingIndex)
            {
                DriverUtilities.ToggleOGLThreading(GraphicsBackendMultithreadingIndex == (int)BackendThreading.Off);
            }

            config.Graphics.BackendThreading.Value = (BackendThreading)GraphicsBackendMultithreadingIndex;
            config.Graphics.ShadersDumpPath.Value = ShaderDumpPath;

            // Audio
            AudioBackend audioBackend = (AudioBackend)AudioBackend;
            if (audioBackend != config.System.AudioBackend.Value)
            {
                config.System.AudioBackend.Value = audioBackend;
            }

            config.System.AudioVolume.Value = Volume / 100;

            // Network
            config.System.EnableInternetAccess.Value = EnableInternetAccess;

            // Logging
            config.Logger.EnableFileLog.Value = EnableFileLog;
            config.Logger.EnableStub.Value = EnableStub;
            config.Logger.EnableInfo.Value = EnableInfo;
            config.Logger.EnableWarn.Value = EnableWarn;
            config.Logger.EnableError.Value = EnableError;
            config.Logger.EnableTrace.Value = EnableTrace;
            config.Logger.EnableGuest.Value = EnableGuest;
            config.Logger.EnableDebug.Value = EnableDebug;
            config.Logger.EnableFsAccessLog.Value = EnableFsAccessLog;
            config.Logger.EnableNetLog.Value = EnableNetLog;
            config.Logger.EnableAvaloniaLog.Value = EnableAvaloniaLog;
            config.System.FsGlobalAccessLogMode.Value = FsGlobalAccessLogMode;
            config.Logger.GraphicsDebugLevel.Value = (GraphicsDebugLevel)OpenglDebugLevel;

            // Multiplayer
            config.Multiplayer.LanInterfaceId.Value = _networkInterfaces[NetworkInterfaceList[NetworkInterfaceIndex]];
            config.Multiplayer.Mode.Value = (MultiplayerMode)MultiplayerModeIndex;
            config.Multiplayer.DisableP2p.Value = DisableP2P;
            config.Multiplayer.LdnPassphrase.Value = LdnPassphrase;

            // Debug
            config.Debug.EnableGdbStub.Value = EnableGdbStub;
            config.Debug.GdbStubPort.Value = GDBStubPort;
            config.Debug.DebuggerSuspendOnStart.Value = DebuggerSuspendOnStart;

            // Dirty Hacks
            config.Hacks.Xc2MenuSoftlockFix.Value = DirtyHacks.Xc2MenuSoftlockFix;
            config.Hacks.DisableNifmIsAnyInternetRequestAccepted.Value =
                DirtyHacks.NifmDisableIsAnyInternetRequestAccepted;

            config.ToFileFormat().SaveConfig(Program.ConfigurationPath);

            MainWindow.UpdateGraphicsConfig();
            RyujinxApp.MainWindow.ViewModel.VSyncModeSettingChanged();

            SaveSettingsEvent?.Invoke();

            GameListNeedsRefresh = false;
        }

        private static void RevertIfNotSaved(bool isCustomConfig = false, bool isGameRunning = false)
        {
            // Restores settings for a custom configuration during a game, if the condition is met.
            // If the condition is not met (parameter is false), restores global (default) configuration instead.
            Program.ReloadConfig(isCustomConfig && isGameRunning); 
        }

        public void ApplyButton()
        {
            SaveSettings();
        }

        public void DeleteConfigGame()
        {
            string gameDir = Program.GetDirGameUserConfig(GameId);

            if (File.Exists(gameDir))
            {
                File.Delete(gameDir);
            }

            RevertIfNotSaved(IsCustomConfig, IsGameRunning);
            CloseWindow?.Invoke();
        }

        public void SaveUserConfig()
        {
            SaveSettings();
            RevertIfNotSaved(IsCustomConfig, IsGameRunning); // Revert global or custom configuration after saving user configuration
            CloseWindow?.Invoke();
        }

        public void OkButton()
        {
            SaveSettings();
            CloseWindow?.Invoke();
        }

        [ObservableProperty] private bool _wantsToReset;

        public AsyncRelayCommand ResetButton => Commands.Create(async () =>
        {
            if (!WantsToReset)
                return;

            CloseWindow?.Invoke();
            ConfigurationState.Instance.LoadDefault();
            ConfigurationState.Instance.ToFileFormat().SaveConfig(Program.ConfigurationPath);
            RyujinxApp.MainWindow.LoadApplications();

            await ContentDialogHelper.CreateInfoDialog(
                $"Your {RyujinxApp.FullAppName} configuration has been reset.",
                "",
                string.Empty,
                LocaleManager.Instance[LocaleKeys.SettingsButtonClose],
                "Configuration Reset");
        });

        public void CancelButton()
        {
            RevertIfNotSaved(IsCustomConfig, IsGameRunning);
            CloseWindow?.Invoke();
        }
    }
}
