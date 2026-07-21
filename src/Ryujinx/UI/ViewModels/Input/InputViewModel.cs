using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Svg.Skia;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using Gommon;
using Ryujinx.Ava.Common.Locale;
using Ryujinx.Ava.Input;
using Ryujinx.Ava.Systems.Configuration;
using Ryujinx.Ava.UI.Helpers;
using Ryujinx.Ava.UI.Models;
using Ryujinx.Ava.UI.Models.Input;
using Ryujinx.Ava.UI.Windows;
using Ryujinx.Common;
using Ryujinx.Common.Configuration;
using Ryujinx.Common.Configuration.Hid;
using Ryujinx.Common.Configuration.Hid.Controller;
using Ryujinx.Common.Configuration.Hid.Keyboard;
using Ryujinx.Common.Logging;
using Ryujinx.Common.Utilities;
using Ryujinx.Input;
using Ryujinx.Input.SDL3;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace Ryujinx.Ava.UI.ViewModels.Input
{
    public partial class InputViewModel : BaseModel, IDisposable
    {
        private const string Disabled = "disabled";
        private const string ProControllerResource = "Ryujinx/Assets/Icons/Controller_ProCon.svg";
        private const string JoyConPairResource = "Ryujinx/Assets/Icons/Controller_JoyConPair.svg";
        private const string JoyConLeftResource = "Ryujinx/Assets/Icons/Controller_JoyConLeft.svg";
        private const string JoyConRightResource = "Ryujinx/Assets/Icons/Controller_JoyConRight.svg";
        private const string KeyboardString = "keyboard";
        private const string ControllerString = "controller";
        private readonly MainWindow _mainWindow;
        private Control _keyboardDriverControl;

        private PlayerIndex _playerId;
        private PlayerIndex _playerIdChoose;
        private int _controller;
        private string _controllerImage;
        private int _device;
        private bool _isChangeTrackingActive;
        [ObservableProperty]
        public partial bool IsModified { get; set; }

        [ObservableProperty]
        public partial string ProfileName { get; set; }

        partial void OnProfileNameChanged(string value)
        {
            OnPropertyChanged(nameof(IsProfileLinked));
            OnPropertyChanged(nameof(CanDeleteOrSaveProfile));
        }

        [ObservableProperty]
        public partial bool NotificationIsVisible { get; set; } // Automatically call the NotificationView property with OnPropertyChanged()

        [ObservableProperty]
        public partial string NotificationText { get; set; } // Automatically call the NotificationText property with OnPropertyChanged()

        private bool _isLoaded;
        private bool _enableDynamicGamepadSwap;
        private bool _suppressProfileLoad;
        private bool _dynamicInputSwapFirstUseWarningShown;
        private bool? _allowDuplicateDeviceAssignment;
        private List<PlayerInputAssignment> _workingPlayerInputAssignments;

        private static readonly InputConfigJsonSerializerContext _serializerContext = new(JsonHelper.GetDefaultSerializerOptions());

        public IGamepadDriver AvaloniaKeyboardDriver { get; private set; }

        public IGamepad SelectedGamepad
        {
            get;
            private set
            {
                if (!ReferenceEquals(field, value))
                {
                    field?.Dispose();
                }

                Rainbow.Reset();

                field = value;

                OnPropertiesChanged(nameof(HasLed), nameof(CanClearLed));
            }
        }

        public StickVisualizer VisualStick { get; private set; }

        public ObservableCollection<PlayerModel> PlayerIndexes { get; set; }
        public ObservableCollection<(DeviceType Type, string Id, string Name)> Devices { get; set; }
        public ObservableCollection<PlayerInputDeviceAssignmentItem> PlayerInputDevices { get; set; }
        internal ObservableCollection<ControllerModel> Controllers { get; set; }
        public AvaloniaList<string> ProfilesList { get; set; }

        public bool UseGlobalConfig;

        // XAML Flags
        public bool ShowSettings => _device > 0;
        public bool IsController => CurrentDeviceType == DeviceType.Controller;
        public bool IsKeyboard => CurrentDeviceType == DeviceType.Keyboard;
        public bool CanOpenAssignedDevices => ShowSettings && EnableDynamicGamepadSwap;
        public bool CanDeleteOrSaveProfile => ShowSettings && !IsDefaultProfileName(ProfileName);
        public bool IsRight { get; set; }
        public bool IsLeft { get; set; }
        public bool HasLed => (SelectedGamepad?.Features & GamepadFeaturesFlag.Led) != 0;
        public bool CanClearLed => SelectedGamepad?.Name?.ContainsIgnoreCase("DualSense") == true;

        public event Action NotifyChangesEvent;

        public string ChosenProfile
        {
            get;
            set
            {
                // When you select a profile, the settings from the profile will be applied.
                // To save the settings, you still need to click the apply button
                field = value;
                if (!_suppressProfileLoad)
                {
                    LoadProfile();
                }
                OnPropertyChanged();
            }
        }

        public object ConfigViewModel
        {
            get;
            set
            {
                field = value;

                VisualStick.UpdateConfig(value);

                OnPropertyChanged();
            }
        }

        public bool EnableDynamicGamepadSwap
        {
            get => _enableDynamicGamepadSwap;
            set
            {
                if (_enableDynamicGamepadSwap == value)
                {
                    return;
                }

                bool isFirstDynamicInputSwapEnable = value && ShouldInitializePlayerOneDynamicInputSwap();
                bool shouldShowFirstUseWarning =
                    isFirstDynamicInputSwapEnable &&
                    !_dynamicInputSwapFirstUseWarningShown &&
                    _isChangeTrackingActive &&
                    _isLoaded &&
                    ConfigurationState.Instance.UI.ShowDynamicInputSwapWarning.Value;

                _enableDynamicGamepadSwap = value;

                if (_enableDynamicGamepadSwap)
                {
                    if (isFirstDynamicInputSwapEnable)
                    {
                        AssignAllConnectedInputDevices();
                    }
                    else
                    {
                        AssignCurrentDeviceIfNoInputDeviceIsAssigned();
                    }
                }

                RefreshProfileBindingState();
                RefreshModifiedState();
                OnPropertyChanged();
                OnPropertyChanged(nameof(CanOpenAssignedDevices));

                if (shouldShowFirstUseWarning)
                {
                    _dynamicInputSwapFirstUseWarningShown = true;
                    ShowDynamicInputSwapFirstUseWarning();
                }
            }
        }

        private bool ShouldInitializePlayerOneDynamicInputSwap()
        {
            if (_playerId != PlayerIndex.Player1)
            {
                return false;
            }

            PlayerInputAssignment persistedAssignment = GetPersistedPlayerInputAssignments()
                .FirstOrDefault(assignment => assignment.PlayerIndex == _playerId);

            return persistedAssignment == null || !persistedAssignment.EnableDynamicInputSwap;
        }

        private async void ShowDynamicInputSwapFirstUseWarning()
        {
            string message = LocaleManager.Instance.UpdateAndGetDynamicValue(
                LocaleKeys.DialogDynamicInputSwapDeviceAssignmentsHint,
                BuildDynamicInputSwapFirstUseAssignmentSummary());

            CheckBoxDialogResult result = await ContentDialogHelper.CreateCheckBoxDialog(
                LocaleManager.Instance[LocaleKeys.ControllerSettingsAssignedInputDevices],
                message,
                LocaleManager.Instance[LocaleKeys.DialogDontShowAgain],
                false);

            if (result.IsChecked)
            {
                ConfigurationState.Instance.UI.ShowDynamicInputSwapWarning.Value = false;
            }
        }

        private string BuildDynamicInputSwapFirstUseAssignmentSummary()
        {
            return string.Join(
                Environment.NewLine,
                PlayerInputDevices
                    .Where(device => device.HasAssignedToPlayers)
                    .Select(device => $"{device.Name} - {device.AssignedToPlayers}"));
        }

        public bool AllowDuplicateDeviceAssignment
        {
            get => _allowDuplicateDeviceAssignment ?? GetSavedAllowDuplicateDeviceAssignment();
            set
            {
                if (AllowDuplicateDeviceAssignment == value)
                {
                    return;
                }

                _allowDuplicateDeviceAssignment = value;

                if (!value)
                {
                    KeepCurrentPlayerAssignedDevicesExclusive();
                }

                RefreshPlayerInputDeviceAssignmentState();

                IsModified = true;
                OnPropertyChanged();
            }
        }

        private bool GetSavedAllowDuplicateDeviceAssignment()
        {
            return UseGlobalConfig && Program.UseExtraConfig
                ? ConfigurationState.InstanceExtra.Hid.AllowDuplicateDeviceAssignment.Value
                : ConfigurationState.Instance.Hid.AllowDuplicateDeviceAssignment.Value;
        }

        public PlayerIndex PlayerIdChoose
        {
            get => _playerIdChoose;
            set { }
        }

        public PlayerIndex PlayerId
        {
            get => _playerId;
            set
            {
                if (IsModified)
                {
                    _playerIdChoose = value;
                    return;
                }

                IsModified = false;
                _playerId = value;
                _isChangeTrackingActive = false;

                if (!Enum.IsDefined<PlayerIndex>(_playerId))
                {
                    _playerId = PlayerIndex.Player1;

                }

                _isLoaded = false;
                LoadConfiguration();
                LoadDevice();
                LoadProfiles();

                _isLoaded = true;
                _isChangeTrackingActive = true;
                OnPropertyChanged();
            }
        }

        public int Controller
        {
            get => _controller;
            set
            {
                int controllerIndex = value < 0 ? 0 : value;

                if (controllerIndex == _controller)
                {
                    return;
                }

                ApplyControllerSelection(controllerIndex);
                RefreshModifiedState();
            }
        }

        private void ApplyControllerSelection(int controllerIndex)
        {
            _controller = controllerIndex;

            if (Controllers.Count > 0 && _controller < Controllers.Count && _controller > -1)
            {
                ControllerType controller = Controllers[_controller].Type;

                IsLeft = true;
                IsRight = true;

                switch (controller)
                {
                    case ControllerType.Handheld:
                        ControllerImage = JoyConPairResource;
                        break;
                    case ControllerType.ProController:
                        ControllerImage = ProControllerResource;
                        break;
                    case ControllerType.JoyconPair:
                        ControllerImage = JoyConPairResource;
                        break;
                    case ControllerType.JoyconLeft:
                        ControllerImage = JoyConLeftResource;
                        IsRight = false;
                        break;
                    case ControllerType.JoyconRight:
                        ControllerImage = JoyConRightResource;
                        IsLeft = false;
                        break;
                }

                LoadInputDriver();
                LoadProfiles();
            }

            OnPropertyChanged(nameof(Controller));
            NotifyChanges();
        }

        public string ControllerImage
        {
            get => _controllerImage;
            set
            {
                _controllerImage = value;

                OnPropertyChanged();
                OnPropertyChanged(nameof(Image));
            }
        }

        public SvgImage Image
        {
            get
            {
                SvgImage image = new();

                if (!string.IsNullOrWhiteSpace(_controllerImage))
                {
                    SvgSource source = SvgSource.LoadFromStream(EmbeddedResources.GetStream(_controllerImage));

                    image.Source = source;
                }

                return image;
            }
        }

        public int Device
        {
            get => _device;
            set
            {
                if (value < 0 || value >= Devices.Count)
                {
                    return;
                }

                _device = value;

                DeviceType selected = Devices[_device].Type;

                if (selected != DeviceType.None)
                {
                    if (_isLoaded)
                    {
                        LoadSelectedDeviceDefaults();
                    }
                    else if (_device < Devices.Count)
                    {
                        LoadControllers();
                    }
                }

                RefreshModifiedState();
                FindPairedDeviceInConfigFile();
                OnPropertyChanged();
                OnPropertyChanged(nameof(SelectedDeviceItem));
                NotifyChanges();
            }
        }

        public bool NeedsResetCurrentDeviceToDefaultsConfirmation()
        {
            if (_device <= 0 || _device >= Devices.Count || Devices[_device].Type == DeviceType.None)
            {
                return false;
            }

            InputConfig defaultConfig = LoadDefaultConfiguration();
            InputConfig currentConfig = GetSelectedDeviceConfig();

            return !ConfigsMatch(currentConfig, defaultConfig);
        }

        public void ResetCurrentDeviceToDefaults()
        {
            RefreshAvailableDevices();

            if (_device <= 0 || _device >= Devices.Count || Devices[_device].Type == DeviceType.None)
            {
                return;
            }

            LoadCurrentDeviceDefaultProfile();
            RefreshModifiedState();
            FindPairedDeviceInConfigFile();
            NotifyChanges();
        }

        public void RefreshInputDevices()
        {
            RefreshAvailableDevices();
        }

        public object SelectedDeviceItem
        {
            get => _device >= 0 && _device < Devices.Count ? Devices[_device] : null;
            set
            {
                if (value is not ValueTuple<DeviceType, string, string> selectedDevice)
                {
                    return;
                }

                int deviceIndex = -1;
                for (int i = 0; i < Devices.Count; i++)
                {
                    (DeviceType Type, string Id, string Name) device = Devices[i];
                    if (device.Type == selectedDevice.Item1 && device.Id == selectedDevice.Item2)
                    {
                        deviceIndex = i;
                        break;
                    }
                }

                if (deviceIndex < 0)
                {
                    return;
                }

                if (deviceIndex == _device)
                {
                    return;
                }

                Device = deviceIndex;
            }
        }

        public InputConfig Config { get; set; }

        public InputViewModel(UserControl owner, bool useGlobal = false) : this()
        {
            if (Program.PreviewerDetached)
            {
                _mainWindow = RyujinxApp.MainWindow;

                ReplaceKeyboardDriver(owner);
                PhysicalKeyLabelHelper.LabelsChanged += OnPhysicalKeyLabelsChanged;

                _mainWindow.InputManager.GamepadDriver.OnGamepadConnected += HandleOnGamepadConnected;
                _mainWindow.InputManager.GamepadDriver.OnGamepadDisconnected += HandleOnGamepadDisconnected;

                UseGlobalConfig = useGlobal;

                _isLoaded = false;

                RefreshAvailableDevices();

                PlayerId = PlayerIndex.Player1;
            }

            _isChangeTrackingActive = true;
        }

        public void RetargetKeyboardDriver(Control owner)
        {
            if (!Program.PreviewerDetached)
            {
                return;
            }

            ReplaceKeyboardDriver(owner);
        }

        public InputViewModel()
        {
            PlayerIndexes = [];
            Controllers = [];
            Devices = [];
            PlayerInputDevices = [];
            ProfilesList = [];
            VisualStick = new StickVisualizer(this);

            ControllerImage = ProControllerResource;

            PlayerIndexes.Add(new(PlayerIndex.Player1, LocaleManager.Instance[LocaleKeys.ControllerSettingsPlayer1]));
            PlayerIndexes.Add(new(PlayerIndex.Player2, LocaleManager.Instance[LocaleKeys.ControllerSettingsPlayer2]));
            PlayerIndexes.Add(new(PlayerIndex.Player3, LocaleManager.Instance[LocaleKeys.ControllerSettingsPlayer3]));
            PlayerIndexes.Add(new(PlayerIndex.Player4, LocaleManager.Instance[LocaleKeys.ControllerSettingsPlayer4]));
            PlayerIndexes.Add(new(PlayerIndex.Player5, LocaleManager.Instance[LocaleKeys.ControllerSettingsPlayer5]));
            PlayerIndexes.Add(new(PlayerIndex.Player6, LocaleManager.Instance[LocaleKeys.ControllerSettingsPlayer6]));
            PlayerIndexes.Add(new(PlayerIndex.Player7, LocaleManager.Instance[LocaleKeys.ControllerSettingsPlayer7]));
            PlayerIndexes.Add(new(PlayerIndex.Player8, LocaleManager.Instance[LocaleKeys.ControllerSettingsPlayer8]));
            PlayerIndexes.Add(new(PlayerIndex.Handheld, LocaleManager.Instance[LocaleKeys.ControllerSettingsHandheld]));
        }



        private InputConfig GetPersistedInputConfig()
        {
            return GetPersistedInputConfig(_playerId);
        }

        private InputConfig GetPersistedInputConfig(PlayerIndex playerIndex)
        {
            if (UseGlobalConfig && Program.UseExtraConfig)
            {
                return ConfigurationState.InstanceExtra.Hid.InputConfig.Value.FirstOrDefault(inputConfig => inputConfig.PlayerIndex == playerIndex);
            }

            return ConfigurationState.Instance.Hid.InputConfig.Value.FirstOrDefault(inputConfig => inputConfig.PlayerIndex == playerIndex);
        }

        private List<InputConfig> GetPersistedInputConfigs()
        {
            if (UseGlobalConfig && Program.UseExtraConfig)
            {
                return ConfigurationState.InstanceExtra.Hid.InputConfig.Value ?? [];
            }

            return ConfigurationState.Instance.Hid.InputConfig.Value ?? [];
        }

        private List<PlayerInputAssignment> GetPersistedPlayerInputAssignments()
        {
            if (UseGlobalConfig && Program.UseExtraConfig)
            {
                return ConfigurationState.InstanceExtra.Hid.PlayerInputAssignments.Value ?? [];
            }

            return ConfigurationState.Instance.Hid.PlayerInputAssignments.Value ?? [];
        }

        private List<PlayerInputAssignment> GetWorkingPlayerInputAssignments()
        {
            if (_workingPlayerInputAssignments != null)
            {
                return _workingPlayerInputAssignments;
            }

            _workingPlayerInputAssignments = GetPersistedPlayerInputAssignments()
                .Where(assignment => assignment != null)
                .Select(ClonePlayerInputAssignment)
                .ToList();

            return _workingPlayerInputAssignments;
        }

        private static PlayerInputAssignment ClonePlayerInputAssignment(PlayerInputAssignment assignment)
        {
            if (assignment == null)
            {
                return null;
            }

            return new PlayerInputAssignment
            {
                PlayerIndex = assignment.PlayerIndex,
                EnableDynamicInputSwap = assignment.EnableDynamicInputSwap,
                Devices = assignment.Devices?
                    .Where(device => device != null)
                    .Select(device => new AssignedInputDevice
                    {
                        Type = device.Type,
                        Id = device.Id,
                        ProfileName = device.ProfileName,
                    })
                    .ToList() ?? [],
            };
        }

        private PlayerInputAssignment GetPersistedPlayerInputAssignment()
        {
            return GetPersistedPlayerInputAssignment(_playerId);
        }

        private PlayerInputAssignment GetPersistedPlayerInputAssignment(PlayerIndex playerIndex)
        {
            return GetPlayerInputAssignment(playerIndex, GetPersistedPlayerInputAssignments());
        }

        private PlayerInputAssignment GetWorkingPlayerInputAssignment(PlayerIndex playerIndex)
        {
            return GetPlayerInputAssignment(playerIndex, _workingPlayerInputAssignments ?? GetPersistedPlayerInputAssignments());
        }

        private PlayerInputAssignment GetPlayerInputAssignment(PlayerIndex playerIndex, List<PlayerInputAssignment> assignments)
        {
            InputConfig persistedConfig = GetPersistedInputConfig(playerIndex);
            PlayerInputAssignment persistedAssignment = assignments?.FirstOrDefault(assignment => assignment.PlayerIndex == playerIndex);

            if (persistedAssignment == null)
            {
                return BuildDefaultPlayerInputAssignment(playerIndex, persistedConfig);
            }

            PlayerInputAssignment normalizedAssignment = PlayerInputAssignmentHelper.Normalize(
                persistedAssignment,
                PlayerInputAssignmentHelper.CreatePrimaryDevice(persistedConfig));

            return normalizedAssignment;
        }

        private void LoadConfiguration(InputConfig inputConfig = null, bool reloadPlayerInputDevices = true)
        {
            Config = inputConfig ?? GetDisplayedInputConfig(GetPersistedInputConfig());

            if (reloadPlayerInputDevices)
            {
                PlayerInputAssignment persistedAssignment = GetPersistedPlayerInputAssignment();
                EnableDynamicGamepadSwap = persistedAssignment.EnableDynamicInputSwap;
            }

            ConfigViewModel = null;

            if (Config is StandardKeyboardInputConfig keyboardInputConfig)
            {
                ConfigViewModel = new KeyboardInputViewModel(this, new KeyboardInputConfig(keyboardInputConfig), VisualStick);
            }

            if (Config is StandardControllerInputConfig controllerInputConfig)
            {
                ConfigViewModel = new ControllerInputViewModel(this, new GamepadInputConfig(controllerInputConfig), VisualStick);
            }

            if (reloadPlayerInputDevices)
            {
                LoadPlayerInputDevices();
            }
            else
            {
                RefreshProfileBindingState();
            }
        }

        private InputConfig GetDisplayedInputConfig(InputConfig persistedConfig)
        {
            if (persistedConfig is not StandardControllerInputConfig controllerConfig)
            {
                return persistedConfig;
            }

            // If runtime has already fallen back to keyboard, reflect that active config in settings
            // instead of showing the stale persisted controller config.
            InputConfig activeConfig = _mainWindow?.ViewModel.AppHost?.NpadManager?.GetPlayerInputConfigByIndex((int)_playerId);
            if (activeConfig is StandardKeyboardInputConfig)
            {
                return activeConfig;
            }

            // When no game is running (NpadManager unavailable) and the persisted controller
            // device isn't currently connected, fall back to keyboard so the user isn't
            // stuck on "Disabled".
            if (activeConfig == null &&
                !Devices.Any(device => device.Type == DeviceType.Controller && device.Id == controllerConfig.Id) &&
                TryCreateKeyboardFallbackConfig(persistedConfig, out StandardKeyboardInputConfig fallbackConfig))
            {
                return fallbackConfig;
            }

            return persistedConfig;
        }

        // Note: player-level routing is stored separately from the selected keyboard/controller profile,
        // so changing the edited device does not silently clear dynamic input swap or assigned devices.
        private PlayerInputAssignment BuildDefaultPlayerInputAssignment(PlayerIndex playerIndex, InputConfig persistedConfig)
        {
            PlayerInputAssignment assignment = new()
            {
                PlayerIndex = playerIndex,
                EnableDynamicInputSwap = persistedConfig?.EnableDynamicGamepadSwap ?? false,
            };

            if (persistedConfig is StandardKeyboardInputConfig)
            {
                assignment.Devices.Add(new AssignedInputDevice
                {
                    Type = AssignedInputDeviceType.Keyboard,
                    Id = persistedConfig.Id,
                });

                if (assignment.EnableDynamicInputSwap)
                {
                    foreach ((DeviceType Type, string Id, string _) in Devices.Where(device => device.Type == DeviceType.Controller))
                    {
                        assignment.Devices.Add(new AssignedInputDevice
                        {
                            Type = AssignedInputDeviceType.Controller,
                            Id = Id,
                        });
                    }
                }
            }
            else if (persistedConfig is StandardControllerInputConfig)
            {
                assignment.Devices.Add(new AssignedInputDevice
                {
                    Type = AssignedInputDeviceType.Controller,
                    Id = persistedConfig.Id,
                });

                if (assignment.EnableDynamicInputSwap)
                {
                    (DeviceType Type, string Id, string Name) keyboardDevice = Devices.FirstOrDefault(device => device.Type == DeviceType.Keyboard);

                    if (keyboardDevice != default)
                    {
                        assignment.Devices.Add(new AssignedInputDevice
                        {
                            Type = AssignedInputDeviceType.Keyboard,
                            Id = keyboardDevice.Id,
                        });
                    }
                }
            }

            return assignment;
        }

        private string GetPlayerDisplayName(PlayerIndex playerIndex)
        {
            return PlayerIndexes.FirstOrDefault(player => player.Id == playerIndex)?.Name ?? playerIndex.ToString();
        }

        private void LoadPlayerInputDevices(bool preserveEdits = false)
        {
            PlayerInputAssignment assignment = GetPersistedPlayerInputAssignment();
            Dictionary<(DeviceType Type, string Id), PlayerInputDeviceAssignmentItem> editedItems = preserveEdits
                ? PlayerInputDevices.ToDictionary(item => (item.DeviceType, item.Id))
                : null;

            Dictionary<(AssignedInputDeviceType Type, string Id), List<string>> deviceToOtherAssignedPlayers = GetOtherPlayerDeviceAssignments();

            PlayerInputDevices.Clear();

            foreach ((DeviceType Type, string Id, string Name) device in Devices.Where(device => device.Type is DeviceType.Keyboard or DeviceType.Controller))
            {
                string deviceId = GetConfigDeviceId(device);
                AssignedInputDeviceType assignedType = device.Type == DeviceType.Keyboard
                    ? AssignedInputDeviceType.Keyboard
                    : AssignedInputDeviceType.Controller;
                PlayerInputDeviceAssignmentItem editedItem = null;
                editedItems?.TryGetValue((device.Type, deviceId), out editedItem);

                bool isAssigned = editedItem?.IsAssigned ?? assignment.Devices.Any(assignedDevice =>
                    assignedDevice.Type == assignedType &&
                    assignedDevice.Id == deviceId);

                string boundProfile = GetProfileNameOrDefault(editedItem?.BoundProfileName ?? assignment.Devices
                    .FirstOrDefault(assignedDevice =>
                        assignedDevice.Type == assignedType &&
                        assignedDevice.Id == deviceId)?.ProfileName);

                // Find other players using this device
                deviceToOtherAssignedPlayers.TryGetValue((assignedType, deviceId), out List<string> assignedOtherPlayers);

                PlayerInputDevices.Add(new PlayerInputDeviceAssignmentItem
                {
                    DeviceType = device.Type,
                    Id = deviceId,
                    Name = device.Name,
                    BoundProfileName = boundProfile,
                    IsAssigned = isAssigned,
                    AssignedToPlayers = FormatAssignedPlayerNames(isAssigned, assignedOtherPlayers),
                    IsDisabledByOtherPlayer = IsDisabledByOtherPlayer(isAssigned, assignedOtherPlayers),
                });
            }

            RefreshPlayerInputDeviceAssignmentState();
            RefreshProfileBindingState();
            OnPropertyChanged(nameof(PlayerInputDevices));
        }

        public void ToggleAssignedPlayerInputDevice(PlayerInputDeviceAssignmentItem item, bool isAssigned)
        {
            if (item == null)
            {
                return;
            }

            if (item.IsDisabledByOtherPlayer && isAssigned)
            {
                return;
            }

            if (item.IsDisabledByOtherPlayer && !isAssigned)
            {
                item.IsAssigned = false;
                RefreshPlayerInputDeviceAssignmentState();
                return;
            }

            item.IsAssigned = isAssigned;
            RefreshPlayerInputDeviceAssignmentState();
            RefreshProfileBindingState();
            RefreshModifiedState();
        }

        private Dictionary<(AssignedInputDeviceType Type, string Id), List<string>> GetOtherPlayerDeviceAssignments()
        {
            IEnumerable<PlayerIndex> otherPlayers = GetPersistedInputConfigs()
                .Where(config => config != null && config.PlayerIndex != _playerId)
                .Select(config => config.PlayerIndex)
                .Concat(((_workingPlayerInputAssignments ?? GetPersistedPlayerInputAssignments()) ?? [])
                    .Where(assignment => assignment != null && assignment.PlayerIndex != _playerId)
                    .Select(assignment => assignment.PlayerIndex))
                .Distinct();
            Dictionary<(AssignedInputDeviceType Type, string Id), List<string>> deviceToOtherAssignedPlayers = [];

            foreach (PlayerIndex otherPlayer in otherPlayers)
            {
                PlayerInputAssignment normalizedOtherAssignment = GetWorkingPlayerInputAssignment(otherPlayer);

                string playerName = GetPlayerDisplayName(otherPlayer);

                foreach (AssignedInputDevice device in normalizedOtherAssignment.Devices)
                {
                    (AssignedInputDeviceType Type, string Id) key = (device.Type, device.Id);
                    if (!deviceToOtherAssignedPlayers.TryGetValue(key, out List<string> players))
                    {
                        players = [];
                        deviceToOtherAssignedPlayers[key] = players;
                    }

                    if (!players.Contains(playerName))
                    {
                        players.Add(playerName);
                    }
                }
            }

            return deviceToOtherAssignedPlayers;
        }

        private void RefreshPlayerInputDeviceAssignmentState()
        {
            Dictionary<(AssignedInputDeviceType Type, string Id), List<string>> deviceToOtherAssignedPlayers = GetOtherPlayerDeviceAssignments();

            foreach (PlayerInputDeviceAssignmentItem item in PlayerInputDevices)
            {
                deviceToOtherAssignedPlayers.TryGetValue((item.AssignedType, item.Id), out List<string> assignedOtherPlayers);

                item.IsDisabledByOtherPlayer = IsDisabledByOtherPlayer(item.IsAssigned, assignedOtherPlayers);

                if (item.IsDisabledByOtherPlayer)
                {
                    item.IsAssigned = false;
                }

                item.AssignedToPlayers = FormatAssignedPlayerNames(item.IsAssigned, assignedOtherPlayers);
            }
        }

        private void KeepCurrentPlayerAssignedDevicesExclusive()
        {
            List<PlayerInputAssignment> assignments = GetWorkingPlayerInputAssignments();

            PlayerInputAssignment currentAssignment = GetEditedPlayerInputAssignment();
            int assignmentIndex = assignments.FindIndex(assignment => assignment.PlayerIndex == PlayerId);

            if (assignmentIndex == -1)
            {
                assignments.Add(currentAssignment);
            }
            else
            {
                assignments[assignmentIndex] = currentAssignment;
            }

            RemoveDuplicateDeviceAssignmentsForCurrentPlayer(assignments, currentAssignment);
        }

        private bool IsDisabledByOtherPlayer(bool isAssigned, List<string> assignedOtherPlayers)
        {
            return !AllowDuplicateDeviceAssignment &&
                !isAssigned &&
                assignedOtherPlayers != null &&
                assignedOtherPlayers.Count > 0;
        }

        private string FormatAssignedPlayerNames(bool isAssigned, List<string> assignedOtherPlayers)
        {
            if (!isAssigned)
            {
                return assignedOtherPlayers != null && assignedOtherPlayers.Count > 0
                    ? string.Join(", ", assignedOtherPlayers.OrderBy(name => ExtractPlayerNumber(name)))
                    : null;
            }

            string currentPlayerName = GetPlayerDisplayName(_playerId);

            return assignedOtherPlayers != null && assignedOtherPlayers.Count > 0
                ? $"{currentPlayerName}, {string.Join(", ", assignedOtherPlayers.OrderBy(name => ExtractPlayerNumber(name)))}"
                : currentPlayerName;
        }

        private int ExtractPlayerNumber(string playerName)
        {
            // Extract the numeric suffix from player names like "Player 1", "Player 2", etc.
            // If no number is found, return 0 to sort such names first.
            if (string.IsNullOrWhiteSpace(playerName))
            {
                return 0;
            }

            // Find the last space and try to parse the number after it
            int lastSpace = playerName.LastIndexOf(' ');
            if (lastSpace >= 0 && lastSpace < playerName.Length - 1)
            {
                string numberPart = playerName[(lastSpace + 1)..];
                if (int.TryParse(numberPart, out int number))
                {
                    return number;
                }
            }

            return 0;
        }

        private PlayerInputAssignment GetEditedPlayerInputAssignment()
        {
            PlayerInputAssignment assignment = new()
            {
                PlayerIndex = _playerId,
                EnableDynamicInputSwap = EnableDynamicGamepadSwap,
            };

            if (EnableDynamicGamepadSwap)
            {
                foreach (PlayerInputDeviceAssignmentItem item in PlayerInputDevices.Where(item => item.IsAssigned))
                {
                    assignment.Devices.Add(new AssignedInputDevice
                    {
                        Type = item.AssignedType,
                        Id = item.Id,
                        ProfileName = GetPersistedProfileName(item.BoundProfileName),
                    });
                }
            }

            // When dynamic swap is off, keep the legacy single selected-device route.
            if (!EnableDynamicGamepadSwap &&
                assignment.Devices.Count == 0 &&
                TryGetCurrentDevice(out (DeviceType Type, string Id, string Name) currentDevice) &&
                currentDevice.Type != DeviceType.None)
            {
                assignment.Devices.Add(new AssignedInputDevice
                {
                    Type = currentDevice.Type == DeviceType.Keyboard ? AssignedInputDeviceType.Keyboard : AssignedInputDeviceType.Controller,
                    Id = GetConfigDeviceId(currentDevice),
                    ProfileName = GetPersistedProfileName(FindInputDeviceAssignmentItem(currentDevice)?.BoundProfileName),
                });
            }

            return PlayerInputAssignmentHelper.Normalize(assignment, GetCurrentPrimaryAssignedInputDevice());
        }

        private bool PlayerAssignmentsMatch(PlayerInputAssignment currentAssignment, PlayerInputAssignment persistedAssignment)
        {
            return PlayerInputAssignmentHelper.AreEquivalent(
                currentAssignment,
                persistedAssignment,
                GetCurrentPrimaryAssignedInputDevice(),
                GetPersistedPrimaryAssignedInputDevice());
        }

        private void AssignCurrentDeviceIfNoInputDeviceIsAssigned()
        {
            if (PlayerInputDevices.Any(device => device.IsAssigned))
            {
                return;
            }

            if (TryGetCurrentDevice(out (DeviceType Type, string Id, string Name) currentDevice) && currentDevice.Type != DeviceType.None)
            {
                PlayerInputDeviceAssignmentItem currentItem = FindInputDeviceAssignmentItem(currentDevice);

                if (currentItem is { IsDisabledByOtherPlayer: false })
                {
                    currentItem.IsAssigned = true;
                    return;
                }
            }

            PlayerInputDeviceAssignmentItem firstAvailableItem = PlayerInputDevices.FirstOrDefault(device => !device.IsDisabledByOtherPlayer);

            if (firstAvailableItem != null)
            {
                firstAvailableItem.IsAssigned = true;
            }
        }

        private void AssignAllConnectedInputDevices()
        {
            foreach (PlayerInputDeviceAssignmentItem item in PlayerInputDevices.Where(device => !device.IsDisabledByOtherPlayer))
            {
                item.IsAssigned = true;
            }

            RefreshPlayerInputDeviceAssignmentState();
        }

        private AssignedInputDevice GetCurrentPrimaryAssignedInputDevice()
        {
            if (TryGetCurrentDevice(out (DeviceType Type, string Id, string Name) currentDevice) && currentDevice.Type != DeviceType.None)
            {
                return new AssignedInputDevice
                {
                    Type = currentDevice.Type == DeviceType.Keyboard ? AssignedInputDeviceType.Keyboard : AssignedInputDeviceType.Controller,
                    Id = GetConfigDeviceId(currentDevice),
                };
            }

            return PlayerInputAssignmentHelper.CreatePrimaryDevice(GetDisplayedInputConfig(GetPersistedInputConfig()));
        }

        private AssignedInputDevice GetPersistedPrimaryAssignedInputDevice()
        {
            return PlayerInputAssignmentHelper.CreatePrimaryDevice(GetPersistedInputConfig());
        }

        private PlayerInputDeviceAssignmentItem FindInputDeviceAssignmentItem((DeviceType Type, string Id, string Name) device)
        {
            string deviceId = GetConfigDeviceId(device);

            return PlayerInputDevices.FirstOrDefault(item =>
                item.DeviceType == device.Type &&
                item.Id == deviceId);
        }

        internal string GetCurrentProfileDefaultName()
        {
            return LocaleManager.Instance[LocaleKeys.ControllerSettingsProfileDefault];
        }

        private string GetProfileNameOrDefault(string profileName)
        {
            return string.IsNullOrWhiteSpace(profileName)
                ? GetCurrentProfileDefaultName()
                : profileName;
        }

        private bool IsDefaultProfileName(string profileName)
        {
            return string.Equals(profileName, GetCurrentProfileDefaultName(), StringComparison.Ordinal);
        }

        private string GetPersistedProfileName(string profileName)
        {
            return string.IsNullOrWhiteSpace(profileName) || IsDefaultProfileName(profileName)
                ? null
                : profileName;
        }

        private string GetBoundProfileNameForCurrentDevice()
        {
            if (!TryGetCurrentDevice(out (DeviceType Type, string Id, string Name) currentDevice))
            {
                return GetCurrentProfileDefaultName();
            }

            return GetProfileNameOrDefault(FindInputDeviceAssignmentItem(currentDevice)?.BoundProfileName);
        }

        private void ClearInvalidBindingForCurrentDevice()
        {
            if (!TryGetCurrentDevice(out (DeviceType Type, string Id, string Name) currentDevice))
            {
                return;
            }

            PlayerInputDeviceAssignmentItem item = FindInputDeviceAssignmentItem(currentDevice);
            if (item != null)
            {
                item.BoundProfileName = GetCurrentProfileDefaultName();
            }
        }

        public bool IsProfileLinked =>
            !string.IsNullOrWhiteSpace(ProfileName) &&
            string.Equals(ProfileName, GetBoundProfileNameForCurrentDevice(), StringComparison.Ordinal);

        public bool IsProfileNameLinked(string profileName)
        {
            return !string.IsNullOrWhiteSpace(profileName) &&
                   string.Equals(profileName, GetBoundProfileNameForCurrentDevice(), StringComparison.Ordinal);
        }

        private void ReplaceBoundProfileName(string previousProfileName, string nextProfileName)
        {
            if (string.IsNullOrWhiteSpace(previousProfileName) ||
                string.Equals(previousProfileName, nextProfileName, StringComparison.Ordinal))
            {
                return;
            }

            string replacementProfileName = GetProfileNameOrDefault(nextProfileName);

            foreach (PlayerInputDeviceAssignmentItem item in PlayerInputDevices)
            {
                if (string.Equals(item.BoundProfileName, previousProfileName, StringComparison.Ordinal))
                {
                    item.BoundProfileName = replacementProfileName;
                }
            }
        }

        private void SetSelectedProfileSilently(string profileName)
        {
            bool wasSuppressingProfileLoad = _suppressProfileLoad;
            _suppressProfileLoad = true;

            try
            {
                ProfileName = profileName;
                ChosenProfile = profileName;
            }
            finally
            {
                _suppressProfileLoad = wasSuppressingProfileLoad;
            }
        }

        private void RefreshProfileBindingState()
        {
            OnPropertyChanged(nameof(CanBindSelectedProfile));
            OnPropertyChanged(nameof(IsProfileLinked));
            OnPropertyChanged(nameof(BoundProfileNameForCurrentDevice));
        }

        public string BoundProfileNameForCurrentDevice => GetBoundProfileNameForCurrentDevice();

        public bool CanBindSelectedProfile =>
            ShowSettings &&
            !string.IsNullOrWhiteSpace(ProfileName) &&
            ProfilesList.Contains(ProfileName);

        public void LinkCurrentProfileToCurrentDevice()
        {
            if (!CanBindSelectedProfile)
            {
                return;
            }

            if (!TryGetCurrentDevice(out (DeviceType Type, string Id, string Name) currentDevice) || currentDevice.Type == DeviceType.None)
            {
                return;
            }

            PlayerInputDeviceAssignmentItem target = FindInputDeviceAssignmentItem(currentDevice);

            if (target == null)
            {
                return;
            }

            DeviceType selectedType = target.DeviceType;
            bool selectedDefaultProfile = IsDefaultProfileName(ProfileName);

            foreach (PlayerInputDeviceAssignmentItem item in PlayerInputDevices.Where(item => item.DeviceType == selectedType))
            {
                if ((!selectedDefaultProfile && string.Equals(item.BoundProfileName, ProfileName, StringComparison.Ordinal)) ||
                    item.Id == target.Id)
                {
                    item.BoundProfileName = GetCurrentProfileDefaultName();
                }
            }

            target.BoundProfileName = GetProfileNameOrDefault(ProfileName);

            RefreshProfileBindingState();
            RefreshModifiedState();
        }

        private void FindPairedDeviceInConfigFile()
        {
            // This function allows you to output a message about the device configuration found in the file
            // NOTE: if the configuration is found, we display the message "Waiting for controller connection",
            // but only if the id gamepad belongs to the selected player

            NotificationIsVisible =
                Config != null &&
                !Devices.Any(device => device.Id == Config.Id) &&
                Config.PlayerIndex == PlayerId;
            if (NotificationIsVisible)
            {
                if (string.IsNullOrEmpty(Config.Name))
                {
                    NotificationText = $"{LocaleManager.Instance[LocaleKeys.ControllerSettingsWaitingConnectDevice].Format("No information", Config.Id)}";
                }
                else
                {
                    NotificationText = $"{LocaleManager.Instance[LocaleKeys.ControllerSettingsWaitingConnectDevice].Format(Config.Name, Config.Id)}";
                }
            }
        }

        public void UnlinkDevice()
        {
            // "Disabled" mode is available after unbinding the device
            // NOTE: the IsModified flag to be able to apply the settings.
            NotificationIsVisible = false;
            IsModified = true;
        }

        public void LoadDevice()
        {
            int deviceIndex = 0;

            if (Config == null || Config.Backend == InputBackendType.Invalid)
            {
                ApplyLoadedDevice(deviceIndex);
                return;
            }

            DeviceType type = DeviceType.None;

            if (Config is StandardKeyboardInputConfig)
            {
                type = DeviceType.Keyboard;
            }

            if (Config is StandardControllerInputConfig)
            {
                type = DeviceType.Controller;
            }

            for (int i = 0; i < Devices.Count; i++)
            {
                if (Devices[i].Type == type && Devices[i].Id == Config.Id)
                {
                    deviceIndex = i;
                    break;
                }
            }

            ApplyLoadedDevice(deviceIndex);
        }

        private void ApplyLoadedDevice(int deviceIndex)
        {
            _device = deviceIndex is >= 0 and < int.MaxValue ? deviceIndex : 0;

            if (_device >= Devices.Count)
            {
                _device = 0;
            }

            if (_device > 0 && Devices[_device].Type != DeviceType.None)
            {
                LoadControllers();
            }

            LoadPlayerInputDevices(_isChangeTrackingActive);
            FindPairedDeviceInConfigFile();
            OnPropertyChanged(nameof(Device));
            OnPropertyChanged(nameof(SelectedDeviceItem));
            NotifyChanges();
        }

        private void LoadSelectedDeviceDefaults()
        {
            if (_device > 0 && _device < Devices.Count && Devices[_device].Type != DeviceType.None)
            {
                LoadControllers();
            }

            LoadConfiguration(LoadPreferredConfigurationForCurrentDevice(), false);
            SetSelectedProfileSilently(GetBoundProfileNameForCurrentDevice());
            RefreshProfileBindingState();
        }

        private void LoadCurrentDeviceDefaultProfile()
        {
            if (_device > 0 && _device < Devices.Count && Devices[_device].Type != DeviceType.None)
            {
                LoadControllers();
            }

            LoadConfiguration(LoadDefaultConfiguration(), false);
            SetSelectedProfileSilently(GetCurrentProfileDefaultName());
            RefreshProfileBindingState();
        }

        private string GetProfilePath(string profileName)
        {
            return Path.Combine(GetProfileBasePath(), profileName + ".json");
        }

        private InputConfig LoadPreferredConfigurationForCurrentDevice()
        {
            string boundProfileName = GetBoundProfileNameForCurrentDevice();

            if (!string.IsNullOrWhiteSpace(boundProfileName) &&
                TryLoadProfileConfiguration(boundProfileName, out InputConfig boundConfig))
            {
                return boundConfig;
            }

            return LoadDefaultConfiguration();
        }

        private bool TryLoadProfileConfiguration(string profileName, out InputConfig config)
        {
            config = null;

            if (string.IsNullOrWhiteSpace(profileName) ||
                string.Equals(profileName, GetCurrentProfileDefaultName(), StringComparison.Ordinal))
            {
                config = LoadDefaultConfiguration();
                return true;
            }

            string path = GetProfilePath(profileName);

            if (!File.Exists(path))
            {
                return false;
            }

            try
            {
                config = JsonHelper.DeserializeFromFile(path, _serializerContext.InputConfig);
            }
            catch (JsonException)
            {
                return false;
            }
            catch (InvalidOperationException)
            {
                return false;
            }

            if (config == null)
            {
                return false;
            }

            if (TryGetCurrentDevice(out (DeviceType Type, string Id, string Name) currentDevice))
            {
                config.Id = GetConfigDeviceId(currentDevice);
                config.Name = currentDevice.Name;
                config.PlayerIndex = _playerId;
            }

            return true;
        }

        public void RefreshModifiedState()
        {
            if (!_isChangeTrackingActive)
            {
                return;
            }

            IsModified = HasUnsavedChanges();
        }

        private bool HasUnsavedChanges()
        {
            bool duplicateDeviceAssignmentChanged = _allowDuplicateDeviceAssignment.HasValue &&
                _allowDuplicateDeviceAssignment.Value != GetSavedAllowDuplicateDeviceAssignment();
            bool configChanged = !ConfigsMatch(GetSelectedDeviceConfig(), GetDisplayedInputConfig(GetPersistedInputConfig()));
            bool playerAssignmentsChanged = !PlayerAssignmentsMatch(GetEditedPlayerInputAssignment(), GetPersistedPlayerInputAssignment());

            return duplicateDeviceAssignmentChanged || configChanged || playerAssignmentsChanged;
        }

        private static bool ConfigsMatch(InputConfig currentConfig, InputConfig otherConfig)
        {
            if (currentConfig == null || otherConfig == null)
            {
                return currentConfig == otherConfig;
            }

            return SerializeComparableConfig(currentConfig) ==
                   SerializeComparableConfig(otherConfig);
        }

        private static string SerializeComparableConfig(InputConfig config)
        {
            InputConfig comparableConfig =
                JsonHelper.Deserialize(
                    JsonHelper.Serialize(config, _serializerContext.InputConfig),
                    _serializerContext.InputConfig);

            comparableConfig.Name = string.Empty;

            if (comparableConfig is StandardControllerInputConfig controllerConfig &&
                controllerConfig.Led is { EnableLed: false, TurnOffLed: false, UseRainbow: false, LedColor: 0 })
            {
                controllerConfig.Led = null;
            }

            return JsonHelper.Serialize(comparableConfig, _serializerContext.InputConfig);
        }

        private InputConfig GetSelectedDeviceConfig()
        {
            if (!TryGetCurrentDevice(out (DeviceType Type, string Id, string Name) device) || device.Type == DeviceType.None)
            {
                return null;
            }

            InputConfig config = device.Type switch
            {
                DeviceType.Keyboard => (ConfigViewModel as KeyboardInputViewModel)?.Config.GetConfig(),
                DeviceType.Controller => (ConfigViewModel as ControllerInputViewModel)?.Config.GetConfig(),
                _ => null,
            };

            if (config == null)
            {
                return null;
            }

            config.Id = GetConfigDeviceId(device);
            config.Name = device.Name;
            config.PlayerIndex = _playerId;
            config.ControllerType = GetSelectedControllerType();
            config.EnableDynamicGamepadSwap = EnableDynamicGamepadSwap;

            return config;
        }

        private void LoadInputDriver()
        {
            if (!TryGetCurrentDevice(out (DeviceType Type, string Id, string Name) device))
            {
                SelectedGamepad = null;
                return;
            }

            string id = GetGamepadId(device);
            DeviceType type = device.Type;

            if (type == DeviceType.None)
            {
                SelectedGamepad = null;
                return;
            }

            if (type == DeviceType.Keyboard)
            {
                if (_mainWindow.InputManager.KeyboardDriver is AvaloniaKeyboardDriver)
                {
                    // NOTE: To get input in this window, we need to bind a custom keyboard driver instead of using the InputManager one as the main window isn't focused...
                    SelectedGamepad = AvaloniaKeyboardDriver.GetGamepad(id);
                }
                else
                {
                    SelectedGamepad = _mainWindow.InputManager.KeyboardDriver.GetGamepad(id);
                }
            }
            else
            {
                SelectedGamepad = _mainWindow.InputManager.GamepadDriver.GetGamepad(id);
            }
        }

        private void HandleOnGamepadDisconnected(string id)
        {
            bool selectedControllerDisconnected =
                TryGetCurrentDevice(out (DeviceType Type, string Id, string Name) currentDevice) &&
                currentDevice.Type == DeviceType.Controller &&
                string.Equals(GetGamepadId(currentDevice), id, StringComparison.Ordinal);

            if (!selectedControllerDisconnected)
            {
                RefreshAvailableDevices();
                RefreshModifiedState();
                FindPairedDeviceInConfigFile();
                NotifyChanges();
                return;
            }

            _isChangeTrackingActive = false; // Disable configuration change tracking

            try
            {
                RefreshAvailableDevices();

                InputConfig persistedConfig = GetPersistedInputConfig();
                InputConfig displayedConfig = GetDisplayedInputConfig(persistedConfig);
                bool shouldApplyKeyboardFallback =
                    selectedControllerDisconnected ||
                    displayedConfig is StandardKeyboardInputConfig;

                if (shouldApplyKeyboardFallback)
                {
                    if (selectedControllerDisconnected &&
                        displayedConfig is not StandardKeyboardInputConfig &&
                        TryCreateKeyboardFallbackConfig(persistedConfig, out StandardKeyboardInputConfig fallbackConfig))
                    {
                        displayedConfig = fallbackConfig;
                    }

                    LoadConfiguration(displayedConfig);
                    LoadDevice();
                    LoadProfiles();
                    FindPairedDeviceInConfigFile();
                    IsModified = false;
                    NotifyChanges();
                }
            }
            finally
            {
                _isChangeTrackingActive = true; // Enable configuration change tracking
            }
        }

        private async void HandleOnGamepadConnected(string id)
        {
            bool hasUnsavedChanges = HasUnsavedChanges();
            InputConfig persistedConfig = GetPersistedInputConfig();
            bool shouldRestoreControllerAfterFallback =
                !hasUnsavedChanges &&
                Config is StandardKeyboardInputConfig &&
                persistedConfig is StandardControllerInputConfig;

            if (shouldRestoreControllerAfterFallback)
            {
                _isChangeTrackingActive = false; // Disable configuration change tracking

                try
                {
                    const int reconnectRestoreAttempts = 20;
                    const int reconnectRestoreDelayMs = 250;

                    string controllerId = persistedConfig.Id;

                    for (int attempt = 0; attempt < reconnectRestoreAttempts; attempt++)
                    {
                        RefreshAvailableDevices();

                        if (Devices.Any(device => device.Type == DeviceType.Controller && device.Id == controllerId))
                        {
                            IsModified = true;
                            RevertChanges();
                            return;
                        }

                        await Task.Delay(reconnectRestoreDelayMs);
                    }
                }
                finally
                {
                    _isChangeTrackingActive = true; // Enable configuration change tracking
                }
            }

            RefreshAvailableDevices();
            RefreshModifiedState();
            FindPairedDeviceInConfigFile();
            NotifyChanges();
        }

        private bool TryGetCurrentDevice(out (DeviceType Type, string Id, string Name) device)
        {
            if (_device < 0 || _device >= Devices.Count)
            {
                device = default;
                return false;
            }

            device = Devices[_device];
            return true;
        }

        private DeviceType CurrentDeviceType =>
            _device >= 0 && _device < Devices.Count ? Devices[_device].Type : DeviceType.None;

        private ControllerType GetSelectedControllerType()
        {
            return _controller >= 0 && _controller < Controllers.Count
                ? Controllers[_controller].Type
                : ControllerType.ProController;
        }

        private static string GetGamepadId((DeviceType Type, string Id, string Name) device)
        {
            return device.Type == DeviceType.None ? null : device.Id.Split(" ")[0];
        }

        // Keyboard configs keep the full ID, while displayed controller entries include
        // the user-facing numbered suffix and need normalization before persistence/lookup.
        private static string GetConfigDeviceId((DeviceType Type, string Id, string Name) device)
        {
            return device.Type switch
            {
                DeviceType.Keyboard => device.Id,
                DeviceType.Controller => GetGamepadId(device),
                _ => null,
            };
        }

        public void LoadControllers()
        {
            Controllers.Clear();

            if (_playerId == PlayerIndex.Handheld)
            {
                Controllers.Add(new(ControllerType.Handheld, LocaleManager.Instance[LocaleKeys.ControllerSettingsControllerTypeHandheld]));

                ApplyControllerSelection(0);
            }
            else
            {
                Controllers.Add(new(ControllerType.ProController, LocaleManager.Instance[LocaleKeys.ControllerSettingsControllerTypeProController]));
                Controllers.Add(new(ControllerType.JoyconPair, LocaleManager.Instance[LocaleKeys.ControllerSettingsControllerTypeJoyConPair]));
                Controllers.Add(new(ControllerType.JoyconLeft, LocaleManager.Instance[LocaleKeys.ControllerSettingsControllerTypeJoyConLeft]));
                Controllers.Add(new(ControllerType.JoyconRight, LocaleManager.Instance[LocaleKeys.ControllerSettingsControllerTypeJoyConRight]));

                if (Config != null)
                {
                    int controllerIndex = -1;
                    for (int i = 0; i < Controllers.Count; i++)
                    {
                        if (Controllers[i].Type == Config.ControllerType)
                        {
                            controllerIndex = i;
                            break;
                        }
                    }

                    if (controllerIndex == -1)
                    {
                        controllerIndex = 0;
                    }

                    // Avalonia bug: setting a newly instanced ComboBox to 0
                    // causes the selected item to show up blank
                    // Workaround: set the box to 1 and then 0
                    // See: https://github.com/AvaloniaUI/Avalonia/issues/4610
                    //      https://github.com/AvaloniaUI/Avalonia/discussions/18834
                    if (controllerIndex == 0)
                    {
                        ApplyControllerSelection(1);
                    }

                    ApplyControllerSelection(controllerIndex);
                }
                else
                {
                    // Avalonia bug workaround: set to 1 then 0
                    ApplyControllerSelection(1);
                    ApplyControllerSelection(0);
                }
            }
        }

        private static string GetShortGamepadName(string str)
        {
            const string Ellipsis = "...";
            const int MaxSize = 50;

            if (str.Length > MaxSize)
            {
                return $"{str.AsSpan(0, MaxSize - Ellipsis.Length)}{Ellipsis}";
            }

            return str;
        }

        private void RefreshAvailableDevices()
        {
            int selectedDeviceIndex = 0;
            (DeviceType Type, string Id, string Name) selectedDevice = default;

            if (_device >= 0 && _device < Devices.Count)
            {
                selectedDevice = Devices[_device];
            }

            string GetGamepadName(IGamepad gamepad, int controllerNumber)
            {
                return $"{GetShortGamepadName(gamepad.Name)} ({controllerNumber})";
            }

            string GetUniqueGamepadName(IGamepad gamepad, ref int controllerNumber)
            {
                string name = GetGamepadName(gamepad, controllerNumber);
                if (Devices.Any(controller => controller.Name == name))
                {
                    controllerNumber++;
                    name = GetUniqueGamepadName(gamepad, ref controllerNumber);
                }

                return name;
            }

            lock (Devices)
            {
                Devices.Clear();
                Devices.Add((DeviceType.None, Disabled, LocaleManager.Instance[LocaleKeys.ControllerSettingsDeviceDisabled]));

                
                foreach (string id in _mainWindow.InputManager.KeyboardDriver.GamepadsIds)
                {
                    using IGamepad gamepad = _mainWindow.InputManager.KeyboardDriver.GetGamepad(id);

                    if (gamepad != null)
                    {
                        Devices.Add((DeviceType.Keyboard, id, $"{GetShortGamepadName(gamepad.Name)}"));
                    }
                }

                foreach (string id in _mainWindow.InputManager.GamepadDriver.GamepadsIds)
                {
                    using IGamepad gamepad = _mainWindow.InputManager.GamepadDriver.GetGamepad(id);

                    if (gamepad != null)
                    {
                        int controllerNumber = 0;
                        string name = GetUniqueGamepadName(gamepad, ref controllerNumber);
                        Devices.Add((DeviceType.Controller, id, name));
                    }
                }

                if (selectedDevice != default)
                {
                    selectedDeviceIndex = -1;
                    for (int i = 0; i < Devices.Count; i++)
                    {
                        (DeviceType Type, string Id, string Name) device = Devices[i];
                        if (device.Type == selectedDevice.Type && device.Id == selectedDevice.Id)
                        {
                            selectedDeviceIndex = i;
                            break;
                        }
                    }
                }

                if (selectedDeviceIndex < 0)
                {
                    selectedDeviceIndex = Math.Clamp(_device, 0, Devices.Count - 1);
                }
            }

            ApplyLoadedDevice(selectedDeviceIndex);
        }

        private string GetProfileBasePath()
        {
            string path = AppDataManager.ProfilesDirPath;
            DeviceType type = CurrentDeviceType;

            if (type == DeviceType.Keyboard)
            {
                path = Path.Combine(path, KeyboardString);
            }
            else if (type == DeviceType.Controller)
            {
                path = Path.Combine(path, ControllerString);
            }

            return path;
        }

        private void LoadProfiles()
        {
            bool wasSuppressingProfileLoad = _suppressProfileLoad;
            _suppressProfileLoad = true;

            try
            {
                ProfilesList.Clear();

                string basePath = GetProfileBasePath();

                if (!Directory.Exists(basePath))
                {
                    Directory.CreateDirectory(basePath);
                }

                ProfilesList.Add(GetCurrentProfileDefaultName());

                foreach (string profile in Directory.GetFiles(basePath, "*.json", SearchOption.AllDirectories))
                {
                    ProfilesList.Add(Path.GetFileNameWithoutExtension(profile));
                }

                string selectedProfile = GetBoundProfileNameForCurrentDevice();

                if (!ProfilesList.Contains(selectedProfile))
                {
                    ClearInvalidBindingForCurrentDevice();
                    selectedProfile = GetCurrentProfileDefaultName();
                }

                SetSelectedProfileSilently(selectedProfile);
            }
            finally
            {
                _suppressProfileLoad = wasSuppressingProfileLoad;
            }

            RefreshProfileBindingState();
        }

        public InputConfig LoadDefaultConfiguration()
        {
            (DeviceType Type, string Id, string Name) activeDevice = Devices.FirstOrDefault();

            if (Devices.Count > 0 && Device < Devices.Count && Device >= 0)
            {
                activeDevice = Devices[Device];
            }

            InputConfig config;
            if (activeDevice.Type == DeviceType.Keyboard)
            {
                config = InputConfigDefaults.CreateDefaultKeyboardConfiguration(
                    activeDevice.Id,
                    activeDevice.Name,
                    GetSelectedControllerType(),
                    _playerId);
            }
            else if (activeDevice.Type == DeviceType.Controller)
            {
                string id = activeDevice.Id.Split(" ")[0];
                string name = activeDevice.Name;

                bool isNintendoStyle = false;

                try
                {
                    IGamepad gp = _mainWindow?.InputManager?.GamepadDriver?.GetGamepad(id);

                    if (gp is SDL3Gamepad sdlGp)
                    {
                        // Nintendo vendor ID is 0x057E
                        isNintendoStyle = sdlGp.VendorId == 0x057E;
                    }
                    else
                    {
                        // Fallback to name-based detection
                        isNintendoStyle = name.Contains("Nintendo", StringComparison.OrdinalIgnoreCase);
                    }
                }
                catch (Exception ex)
                {
                    Logger.Debug?.Print(LogClass.UI, $"Controller vendor detection failed for '{name}': {ex.Message}");
                    isNintendoStyle = name.Contains("Nintendo", StringComparison.OrdinalIgnoreCase);
                }

                config = InputConfigDefaults.CreateDefaultControllerConfiguration(
                    id,
                    name,
                    GetSelectedControllerType(),
                    _playerId,
                    isNintendoStyle);
            }
            else
            {
                config = new InputConfig();
            }

            config.PlayerIndex = _playerId;

            return config;
        }

        private bool TryCreateKeyboardFallbackConfig(InputConfig sourceConfig, out StandardKeyboardInputConfig fallbackConfig)
        {
            fallbackConfig = null;

            (DeviceType Type, string Id, string Name) keyboardDevice =
                Devices.FirstOrDefault(device => device.Type == DeviceType.Keyboard);

            if (keyboardDevice == default)
            {
                return false;
            }

            ControllerType controllerType = sourceConfig?.ControllerType ?? GetSelectedControllerType();
            PlayerIndex playerIndex = sourceConfig?.PlayerIndex ?? _playerId;

            fallbackConfig = InputConfigDefaults.CreateDefaultKeyboardConfiguration(
                keyboardDevice.Id,
                keyboardDevice.Name,
                controllerType,
                playerIndex);
            fallbackConfig.EnableDynamicGamepadSwap = sourceConfig?.EnableDynamicGamepadSwap ?? false;
            return true;
        }

        public async void LoadProfile()
        {
            if (Device == 0)
            {
                return;
            }

            InputConfig config = null;

            if (string.IsNullOrWhiteSpace(ProfileName))
            {
                return;
            }

            if (ProfileName == GetCurrentProfileDefaultName())
            {
                config = LoadDefaultConfiguration();
            }
            else
            {
                string path = GetProfilePath(ProfileName);

                if (!File.Exists(path))
                {
                    int index = ProfilesList.IndexOf(ProfileName);
                    if (index != -1)
                    {
                        ProfilesList.RemoveAt(index);
                    }

                    return;
                }

                try
                {
                    config = JsonHelper.DeserializeFromFile(path, _serializerContext.InputConfig);
                }
                catch (JsonException) { }
                catch (InvalidOperationException)
                {
                    Logger.Error?.Print(LogClass.Configuration, $"Profile {ProfileName} is incompatible with the current input configuration system.");

                    await ContentDialogHelper.CreateErrorDialog(LocaleManager.Instance.UpdateAndGetDynamicValue(LocaleKeys.DialogProfileInvalidProfileErrorMessage, ProfileName));

                    return;
                }
            }

            if (config != null)
            {
                _isLoaded = false;

                string currentDeviceId = Config?.Id ??
                    (TryGetCurrentDevice(out (DeviceType Type, string Id, string Name) currentDevice)
                        ? GetConfigDeviceId(currentDevice)
                        : null);
                if (string.IsNullOrEmpty(currentDeviceId))
                {
                    Logger.Warning?.Print(LogClass.Configuration, $"Ignoring profile load for {ProfileName} because no active input device is selected.");
                    return;
                }

                config.Id = currentDeviceId; // Set current device id instead of changing device(independent profiles)

                LoadConfiguration(config, false);

                //LoadDevice();  This line of code hard-links profiles to controllers, the commented line allows profiles to be applied to all controllers 

                _isLoaded = true;

                RefreshProfileBindingState();
                RefreshModifiedState();
                NotifyChanges();
            }
        }

        public async void SaveProfile()
        {

            if (Device == 0)
            {
                return;
            }

            if (ConfigViewModel == null)
            {
                return;
            }

            if (ProfileName == GetCurrentProfileDefaultName())
            {
                await ContentDialogHelper.CreateErrorDialog(LocaleManager.Instance[LocaleKeys.DialogProfileDefaultProfileOverwriteErrorMessage]);

                return;
            }
            else
            {
                bool validFileName = ProfileName.IndexOfAny(Path.GetInvalidFileNameChars()) == -1;

                if (validFileName)
                {
                    string path = GetProfilePath(ProfileName);

                    InputConfig config = null;

                    if (IsKeyboard)
                    {
                        config = (ConfigViewModel as KeyboardInputViewModel).Config.GetConfig();
                    }
                    else if (IsController)
                    {
                        config = (ConfigViewModel as ControllerInputViewModel).Config.GetConfig();
                    }

                    if (config != null && _controller >= 0 && _controller < Controllers.Count)
                    {
                        config.ControllerType = Controllers[_controller].Type;
                    }

                    string jsonString = JsonHelper.Serialize(config, _serializerContext.InputConfig);

                    await File.WriteAllTextAsync(path, jsonString);

                    LoadProfiles();
                    SetSelectedProfileSilently(ProfileName);
                }
                else
                {
                    await ContentDialogHelper.CreateErrorDialog(LocaleManager.Instance[LocaleKeys.DialogProfileInvalidProfileNameErrorMessage]);
                }
            }
        }

        public async void RemoveProfile()
        {
            if (Device == 0 || ProfileName == GetCurrentProfileDefaultName() || ProfilesList.IndexOf(ProfileName) == -1)
            {
                return;
            }

            UserResult result = await ContentDialogHelper.CreateConfirmationDialog(
                LocaleManager.Instance[LocaleKeys.DialogProfileDeleteProfileTitle],
                LocaleManager.Instance[LocaleKeys.DialogProfileDeleteProfileMessage],
                LocaleManager.Instance[LocaleKeys.InputDialogYes],
                LocaleManager.Instance[LocaleKeys.InputDialogNo],
                LocaleManager.Instance[LocaleKeys.RyujinxConfirm]);

            if (result == UserResult.Yes)
            {
                string path = GetProfilePath(ProfileName);

                if (File.Exists(path))
                {
                    File.Delete(path);
                }

                ReplaceBoundProfileName(ProfileName, null);
                LoadProfiles();

                SetSelectedProfileSilently(ProfilesList[0].ToString());
                RefreshModifiedState();
            }
        }

        public void RevertChanges()
        {
            _allowDuplicateDeviceAssignment = null;
            _workingPlayerInputAssignments = null;
            _isLoaded = false;
            LoadConfiguration();
            LoadDevice();
            _isLoaded = true;

            OnPropertyChanged();
            IsModified = false;
        }

        public void Save()
        {

            if (!IsModified)
            {
                return; //If the input settings were not touched, then do nothing
            }

            IsModified = false;

            List<InputConfig> newConfig = [];
            List<PlayerInputAssignment> newAssignments = [];

            if (UseGlobalConfig && Program.UseExtraConfig)
            {
                newConfig.AddRange(ConfigurationState.InstanceExtra.Hid.InputConfig.Value ?? []);
                newAssignments.AddRange((_workingPlayerInputAssignments ?? ConfigurationState.InstanceExtra.Hid.PlayerInputAssignments.Value) ?? []);
            }
            else
            {
                newConfig.AddRange(ConfigurationState.Instance.Hid.InputConfig.Value ?? []);
                newAssignments.AddRange((_workingPlayerInputAssignments ?? ConfigurationState.Instance.Hid.PlayerInputAssignments.Value) ?? []);
            }

            newConfig.RemoveAll(static inputConfig => inputConfig == null);
            newAssignments.RemoveAll(static assignment => assignment == null);

            if (Device == 0)
            {
                newConfig.RemoveAll(inputConfig => inputConfig.PlayerIndex == PlayerId);
                newAssignments.RemoveAll(assignment => assignment.PlayerIndex == PlayerId);
            }
            else
            {
                InputConfig config = GetSelectedDeviceConfig();
                PlayerInputAssignment assignment = GetEditedPlayerInputAssignment();

                if (config == null)
                {
                    IsModified = true;
                    return;
                }

                int i = newConfig.FindIndex(x => x.PlayerIndex == PlayerId);
                if (i == -1)
                {
                    newConfig.Add(config);
                }
                else
                {
                    newConfig[i] = config;
                }

                int assignmentIndex = newAssignments.FindIndex(x => x.PlayerIndex == PlayerId);
                if (assignmentIndex == -1)
                {
                    newAssignments.Add(assignment);
                }
                else
                {
                    newAssignments[assignmentIndex] = assignment;
                }

                if (!AllowDuplicateDeviceAssignment)
                {
                    RemoveDuplicateDeviceAssignmentsForCurrentPlayer(newAssignments, assignment);
                }
            }

            // Atomically replace and signal input change.
            // NOTE: Do not modify InputConfig.Value directly as other code depends on the on-change event.
            _mainWindow.ViewModel.AppHost?.NpadManager.ReloadConfiguration(newConfig, newAssignments, ConfigurationState.Instance.Hid.EnableKeyboard, ConfigurationState.Instance.Hid.EnableMouse);

            if (UseGlobalConfig && Program.UseExtraConfig)
            {
                // In User Settings when "Use Global Input" is enabled, it saves global input to global setting
                ConfigurationState.InstanceExtra.Hid.InputConfig.Value = newConfig;
                ConfigurationState.InstanceExtra.Hid.PlayerInputAssignments.Value = newAssignments;
                ConfigurationState.InstanceExtra.Hid.AllowDuplicateDeviceAssignment.Value = AllowDuplicateDeviceAssignment;
                ConfigurationState.InstanceExtra.ToFileFormat().SaveConfig(Program.GlobalConfigurationPath);
            }
            else
            {
                ConfigurationState.Instance.Hid.InputConfig.Value = newConfig;
                ConfigurationState.Instance.Hid.PlayerInputAssignments.Value = newAssignments;
                ConfigurationState.Instance.Hid.AllowDuplicateDeviceAssignment.Value = AllowDuplicateDeviceAssignment;
                ConfigurationState.Instance.ToFileFormat().SaveConfig(Program.ConfigurationPath);
            }

            _allowDuplicateDeviceAssignment = null;
            _workingPlayerInputAssignments = null;
        }

        private void RemoveDuplicateDeviceAssignmentsForCurrentPlayer(List<PlayerInputAssignment> assignments, PlayerInputAssignment currentAssignment)
        {
            if (currentAssignment?.Devices == null || currentAssignment.Devices.Count == 0)
            {
                return;
            }

            foreach (InputConfig inputConfig in GetPersistedInputConfigs().Where(inputConfig =>
                inputConfig != null &&
                inputConfig.PlayerIndex != PlayerId &&
                inputConfig.EnableDynamicGamepadSwap &&
                CurrentAssignmentContainsDevice(currentAssignment, PlayerInputAssignmentHelper.CreatePrimaryDevice(inputConfig))))
            {
                if (assignments.All(assignment => assignment.PlayerIndex != inputConfig.PlayerIndex))
                {
                    assignments.Add(new PlayerInputAssignment
                    {
                        PlayerIndex = inputConfig.PlayerIndex,
                        EnableDynamicInputSwap = true,
                    });
                }
            }

            foreach (PlayerInputAssignment assignment in assignments.Where(assignment => assignment.PlayerIndex != PlayerId))
            {
                assignment.Devices.RemoveAll(device => CurrentAssignmentContainsDevice(currentAssignment, device));
            }
        }

        private static bool CurrentAssignmentContainsDevice(PlayerInputAssignment currentAssignment, AssignedInputDevice device)
        {
            return device != null &&
                currentAssignment.Devices.Any(currentDevice =>
                    currentDevice.Type == device.Type &&
                    string.Equals(currentDevice.Id, device.Id, StringComparison.Ordinal));
        }

        public void NotifyChanges()
        {
            OnPropertyChanged(nameof(ConfigViewModel));
            OnPropertyChanged(nameof(IsController));
            OnPropertyChanged(nameof(ShowSettings));
            OnPropertyChanged(nameof(CanOpenAssignedDevices));
            OnPropertyChanged(nameof(IsKeyboard));
            OnPropertyChanged(nameof(IsRight));
            OnPropertyChanged(nameof(IsLeft));
            OnPropertyChanged(nameof(CanBindSelectedProfile));
            OnPropertyChanged(nameof(IsProfileLinked));
            NotifyChangesEvent?.Invoke();
        }

        private void OnPhysicalKeyLabelsChanged()
        {
            if (ConfigViewModel is KeyboardInputViewModel keyboardInputViewModel)
            {
                Dispatcher.UIThread.Post(keyboardInputViewModel.Config.NotifyKeyLabelsChanged);
            }
        }

        private void ReplaceKeyboardDriver(Control owner)
        {
            Control target = TopLevel.GetTopLevel(owner) as Control ?? owner;

            if (ReferenceEquals(_keyboardDriverControl, target))
            {
                return;
            }

            if (AvaloniaKeyboardDriver is AvaloniaKeyboardDriver oldKeyboardDriver)
            {
                oldKeyboardDriver.KeyPressed -= PhysicalKeyLabelHelper.ObserveKeyPress;
                oldKeyboardDriver.Dispose();
            }

            _keyboardDriverControl = target;

            AvaloniaKeyboardDriver keyboardDriver = new(target, KeyboardInputMode.Physical);
            keyboardDriver.KeyPressed += PhysicalKeyLabelHelper.ObserveKeyPress;
            AvaloniaKeyboardDriver = keyboardDriver;

            if (_isLoaded && Device > 0 && Device < Devices.Count && Devices[Device].Type == DeviceType.Keyboard)
            {
                SelectedGamepad?.Dispose();
                LoadInputDriver();
            }
        }

        public void Dispose()
        {
            GC.SuppressFinalize(this);
            PhysicalKeyLabelHelper.LabelsChanged -= OnPhysicalKeyLabelsChanged;

            _mainWindow.InputManager.GamepadDriver.OnGamepadConnected -= HandleOnGamepadConnected;
            _mainWindow.InputManager.GamepadDriver.OnGamepadDisconnected -= HandleOnGamepadDisconnected;

            VisualStick.Dispose();

            SelectedGamepad?.Dispose();

            AvaloniaKeyboardDriver?.Dispose();
        }
    }
}
