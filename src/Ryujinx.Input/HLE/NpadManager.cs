using Ryujinx.Common;
using Ryujinx.Common.Configuration.Hid;
using Ryujinx.Common.Configuration.Hid.Controller;
using Ryujinx.Common.Configuration.Hid.Keyboard;
using Ryujinx.HLE.HOS.Services.Hid;
using System;
using System.Buffers;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Threading;
using CemuHookClient = Ryujinx.Input.Motion.CemuHook.Client;
using ControllerType = Ryujinx.Common.Configuration.Hid.ControllerType;
using PlayerIndex = Ryujinx.HLE.HOS.Services.Hid.PlayerIndex;
using Switch = Ryujinx.HLE.Switch;

namespace Ryujinx.Input.HLE
{
    public class NpadManager : IDisposable
    {
        private readonly CemuHookClient _cemuHookClient;

        private readonly Lock _lock = new();

        private int _inputUpdateBlockCount;

        private const int MaxControllers = 9;

        private readonly NpadController[] _controllers;

        private readonly IGamepadDriver _keyboardDriver;
        private readonly IGamepadDriver _gamepadDriver;
        private readonly IGamepadDriver _mouseDriver;
        private bool _isDisposed;

        private List<InputConfig> _inputConfig;
        private List<InputConfig> _requestedInputConfig;
        private List<PlayerInputAssignment> _playerInputAssignments;
        private bool _enableKeyboard;
        private bool _enableMouse;
        private Switch _device;
        
        private readonly List<GamepadInput> _hleInputStates = [];
        private readonly List<SixAxisInput> _hleMotionStates = new(NpadDevices.MaxControllers);

        public NpadManager(IGamepadDriver keyboardDriver, IGamepadDriver gamepadDriver, IGamepadDriver mouseDriver)
        {
            _controllers = new NpadController[MaxControllers];
            _cemuHookClient = new CemuHookClient(this);

            _keyboardDriver = keyboardDriver;
            _gamepadDriver = gamepadDriver;
            _mouseDriver = mouseDriver;
            _inputConfig = [];
            _requestedInputConfig = [];
            _playerInputAssignments = [];

            _gamepadDriver.OnGamepadConnected += HandleOnGamepadConnected;
            _gamepadDriver.OnGamepadDisconnected += HandleOnGamepadDisconnected;
        }

        private void RefreshInputConfigForHLE()
        {
            lock (_lock)
            {
                List<InputConfig> validInputs = [];
                foreach (InputConfig inputConfigEntry in _inputConfig)
                {
                    if (_controllers[(int)inputConfigEntry.PlayerIndex] != null)
                    {
                        validInputs.Add(inputConfigEntry);
                    }
                }

                _device.Hid.RefreshInputConfig(validInputs);
            }
        }

        private void HandleOnGamepadDisconnected(string obj)
        {
            List<InputConfig> requestedInputConfig;
            List<PlayerInputAssignment> playerInputAssignments;
            bool enableKeyboard;
            bool enableMouse;

            lock (_lock)
            {
                // Forcibly disconnect any controllers with this ID.
                for (int i = 0; i < _controllers.Length; i++)
                {
                    if (_controllers[i]?.HasAssignedControllerId(obj) == true)
                    {
                        _controllers[i]?.Dispose();
                        _controllers[i] = null;
                    }
                }

                requestedInputConfig = _requestedInputConfig;
                playerInputAssignments = _playerInputAssignments;
                enableKeyboard = _enableKeyboard;
                enableMouse = _enableMouse;
            }

            // Force input reload.
            ReloadConfiguration(requestedInputConfig, playerInputAssignments, enableKeyboard, enableMouse);
        }

        private void HandleOnGamepadConnected(string id)
        {
            List<InputConfig> requestedInputConfig;
            List<PlayerInputAssignment> playerInputAssignments;
            bool enableKeyboard;
            bool enableMouse;

            lock (_lock)
            {
                for (int i = 0; i < _controllers.Length; i++)
                {
                    if (_controllers[i] != null && PlayerHasAssignedControllerId((PlayerIndex)i, id))
                    {
                        _controllers[i]?.Dispose();
                        _controllers[i] = null;
                    }
                }

                requestedInputConfig = _requestedInputConfig;
                playerInputAssignments = _playerInputAssignments;
                enableKeyboard = _enableKeyboard;
                enableMouse = _enableMouse;
            }

            // Force input reload
            ReloadConfiguration(requestedInputConfig, playerInputAssignments, enableKeyboard, enableMouse);
        }

        private bool PlayerHasAssignedControllerId(PlayerIndex playerIndex, string id)
        {
            if (string.IsNullOrEmpty(id))
            {
                return false;
            }

            InputConfig inputConfig = _requestedInputConfig.FirstOrDefault(config => (int)config.PlayerIndex == (int)playerIndex);

            if (inputConfig == null)
            {
                return false;
            }

            PlayerInputAssignment playerInputAssignment = GetPlayerInputAssignment(inputConfig);

            return playerInputAssignment.EnableDynamicInputSwap &&
                playerInputAssignment.Devices.Any(device =>
                    device.Type == AssignedInputDeviceType.Controller &&
                    string.Equals(device.Id, id, StringComparison.Ordinal));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool DriverConfigurationUpdate(ref NpadController controller, InputConfig config, PlayerInputAssignment playerInputAssignment)
        {
            Debug.Assert(_keyboardDriver != null, "Keyboard driver is not initialized!");
            Debug.Assert(_gamepadDriver != null, "Gamepad driver is not initialized!");

            if (!controller.MatchesDriverConfiguration(config, playerInputAssignment))
            {
                return controller.UpdateDriverConfiguration(_keyboardDriver, _gamepadDriver, config, playerInputAssignment);
            }

            return controller.IsAvailable;
        }

        public void ReloadConfiguration(List<InputConfig> inputConfig, bool enableKeyboard, bool enableMouse)
        {
            ReloadConfiguration(inputConfig, [], enableKeyboard, enableMouse);
        }

        public void ReloadConfiguration(List<InputConfig> inputConfig, List<PlayerInputAssignment> playerInputAssignments, bool enableKeyboard, bool enableMouse)
        {
            lock (_lock)
            {
                _requestedInputConfig = inputConfig?.ToList() ?? [];
                _playerInputAssignments = playerInputAssignments?.ToList() ?? [];

                NpadController[] oldControllers = _controllers.ToArray();

                List<InputConfig> validInputs = [];

                foreach (InputConfig inputConfigEntry in _requestedInputConfig)
                {
                    NpadController controller;
                    int index = (int)inputConfigEntry.PlayerIndex;

                    if (oldControllers[index] != null)
                    {
                        // Try reuse the existing controller.
                        controller = oldControllers[index];
                        oldControllers[index] = null;
                    }
                    else
                    {
                        controller = new(_cemuHookClient);
                    }

                    InputConfig activeConfig = inputConfigEntry;
                    PlayerInputAssignment playerInputAssignment = GetPlayerInputAssignment(inputConfigEntry);

                    bool isValid = DriverConfigurationUpdate(ref controller, activeConfig, playerInputAssignment);

                    if (!isValid &&
                        !playerInputAssignment.EnableDynamicInputSwap &&
                        inputConfigEntry is StandardControllerInputConfig &&
                        TryGetKeyboardFallback(inputConfigEntry, out StandardKeyboardInputConfig fallbackConfig))
                    {
                        activeConfig = fallbackConfig;
                        isValid = DriverConfigurationUpdate(ref controller, activeConfig, playerInputAssignment);
                    }

                    if (!isValid)
                    {
                        _controllers[index] = null;
                        controller.Dispose();
                    }
                    else
                    {
                        _controllers[index] = controller;
                        validInputs.Add(activeConfig);
                    }
                }

                for (int i = 0; i < oldControllers.Length; i++)
                {
                    // Disconnect any controllers that weren't reused by the new configuration.

                    oldControllers[i]?.Dispose();
                    oldControllers[i] = null;
                }

                _inputConfig = validInputs;
                _enableKeyboard = enableKeyboard;
                _enableMouse = enableMouse;

                _device.Hid.RefreshInputConfig(validInputs);
            }
        }

        private PlayerInputAssignment GetPlayerInputAssignment(InputConfig inputConfig)
        {
            PlayerInputAssignment playerInputAssignment = _playerInputAssignments.FirstOrDefault(assignment => assignment.PlayerIndex == inputConfig.PlayerIndex);

            if (playerInputAssignment != null)
            {
                PlayerInputAssignment normalizedAssignment = PlayerInputAssignmentHelper.Normalize(
                    playerInputAssignment,
                    PlayerInputAssignmentHelper.CreatePrimaryDevice(inputConfig));

                if (normalizedAssignment.EnableDynamicInputSwap || normalizedAssignment.Devices.Count > 0)
                {
                    return normalizedAssignment;
                }
            }

            // Note: older configs only know about a single saved device per player,
            // so we synthesize a routing entry here until the user saves explicit assignments.
            playerInputAssignment = new PlayerInputAssignment
            {
                PlayerIndex = inputConfig.PlayerIndex,
                EnableDynamicInputSwap = inputConfig.EnableDynamicGamepadSwap,
            };

            AssignedInputDevice primaryDevice = PlayerInputAssignmentHelper.CreatePrimaryDevice(inputConfig);

            if (primaryDevice != null)
            {
                playerInputAssignment.Devices.Add(primaryDevice);
            }

            if (playerInputAssignment.EnableDynamicInputSwap && inputConfig is StandardControllerInputConfig)
            {
                string keyboardId = _keyboardDriver.GamepadsIds.IsEmpty ? null : _keyboardDriver.GamepadsIds[0];

                if (!string.IsNullOrWhiteSpace(keyboardId))
                {
                    playerInputAssignment.Devices.Add(new AssignedInputDevice
                    {
                        Type = AssignedInputDeviceType.Keyboard,
                        Id = keyboardId,
                    });
                }
            }

            return playerInputAssignment;
        }

        private bool TryGetKeyboardFallback(InputConfig inputConfig, out StandardKeyboardInputConfig fallbackConfig)
        {
            fallbackConfig = null;

            ReadOnlySpan<string> keyboardIds = _keyboardDriver.GamepadsIds;

            if (keyboardIds.IsEmpty)
            {
                return false;
            }

            string keyboardId = keyboardIds[0];

            using IGamepad keyboard = _keyboardDriver.GetGamepad(keyboardId);

            if (keyboard == null)
            {
                return false;
            }

            fallbackConfig = InputConfigDefaults.CreateDefaultKeyboardConfiguration(
                keyboardId,
                keyboard.Name,
                inputConfig.ControllerType,
                inputConfig.PlayerIndex);

            fallbackConfig.EnableDynamicGamepadSwap = inputConfig.EnableDynamicGamepadSwap;

            return true;
        }

        private void ClearInputDriverStates()
        {
            foreach (InputConfig inputConfig in _inputConfig)
            {
                _controllers[(int)inputConfig.PlayerIndex]?.GamepadDriver?.Clear();
            }
        }

        public void UnblockInputUpdates()
        {
            lock (_lock)
            {
                if (_inputUpdateBlockCount == 0)
                {
                    return;
                }

                _inputUpdateBlockCount--;

                if (_inputUpdateBlockCount == 0)
                {
                    ClearInputDriverStates();
                }
            }
        }

        public bool InputUpdatesBlocked
        {
            get
            {
                lock (_lock)
                    return _inputUpdateBlockCount > 0;
            }
        }

        public void BlockInputUpdates()
        {
            lock (_lock)
            {
                _inputUpdateBlockCount++;
            }
        }

        public void Initialize(Switch device, List<InputConfig> inputConfig, bool enableKeyboard, bool enableMouse)
        {
            Initialize(device, inputConfig, [], enableKeyboard, enableMouse);
        }

        public void Initialize(Switch device, List<InputConfig> inputConfig, List<PlayerInputAssignment> playerInputAssignments, bool enableKeyboard, bool enableMouse)
        {
            _device = device;
            _device.Configuration.RefreshInputConfig = RefreshInputConfigForHLE;

            ReloadConfiguration(inputConfig, playerInputAssignments, enableKeyboard, enableMouse);
        }

        public void Update(float aspectRatio = 1)
        {
            lock (_lock)
            {
                _hleInputStates.Clear();
                _hleMotionStates.Clear();

                KeyboardInput? hleKeyboardInput = null;

                foreach (InputConfig inputConfig in _inputConfig)
                {
                    GamepadInput inputState = default;
                    (SixAxisInput, SixAxisInput) motionState = default;

                    NpadController controller = _controllers[(int)inputConfig.PlayerIndex];
                    PlayerIndex playerIndex = (PlayerIndex)inputConfig.PlayerIndex;

                    bool isJoyconPair = false;

                    // Do we allow input updates and is a controller connected?
                    if (_inputUpdateBlockCount == 0 && controller != null)
                    {
                        DriverConfigurationUpdate(ref controller, inputConfig, GetPlayerInputAssignment(inputConfig));

                        controller.UpdateUserConfiguration(inputConfig);
                        controller.Update();
                        controller.UpdateRumble(_device.Hid.Npads.GetRumbleQueue(playerIndex));

                        inputState = controller.GetHLEInputState();

                        inputState.Buttons |= _device.Hid.UpdateStickButtons(inputState.LStick, inputState.RStick);

                        isJoyconPair = inputConfig.ControllerType == ControllerType.JoyconPair;

                        SixAxisInput altMotionState = isJoyconPair ? controller.GetHLEMotionState(true) : default;

                        motionState = (controller.GetHLEMotionState(), altMotionState);
                    }
                    else
                    {
                        // Ensure that orientation isn't null
                        motionState.Item1.Orientation = new float[9];
                    }

                    inputState.PlayerId = playerIndex;
                    motionState.Item1.PlayerId = playerIndex;

                    _hleInputStates.Add(inputState);
                    _hleMotionStates.Add(motionState.Item1);

                    if (isJoyconPair && !motionState.Item2.Equals(default))
                    {
                        motionState.Item2.PlayerId = playerIndex;

                        _hleMotionStates.Add(motionState.Item2);
                    }
                }

                if (_inputUpdateBlockCount == 0 && _enableKeyboard)
                {
                    hleKeyboardInput = NpadController.GetHLEKeyboardInput(_keyboardDriver);
                }

                _device.Hid.Npads.Update(_hleInputStates);
                _device.Hid.Npads.UpdateSixAxis(_hleMotionStates);

                if (hleKeyboardInput.HasValue)
                {
                    _device.Hid.Keyboard.Update(hleKeyboardInput.Value);
                }

                if (_enableMouse)
                {
                    IMouse mouse = _mouseDriver.GetGamepad("0") as IMouse;

                    MouseStateSnapshot mouseInput = IMouse.GetMouseStateSnapshot(mouse);

                    uint buttons = 0;

                    if (mouseInput.IsPressed(MouseButton.Button1))
                    {
                        buttons |= 1 << 0;
                    }

                    if (mouseInput.IsPressed(MouseButton.Button2))
                    {
                        buttons |= 1 << 1;
                    }

                    if (mouseInput.IsPressed(MouseButton.Button3))
                    {
                        buttons |= 1 << 2;
                    }

                    if (mouseInput.IsPressed(MouseButton.Button4))
                    {
                        buttons |= 1 << 3;
                    }

                    if (mouseInput.IsPressed(MouseButton.Button5))
                    {
                        buttons |= 1 << 4;
                    }

                    Vector2 position = IMouse.GetScreenPosition(mouseInput.Position, mouse.ClientSize, aspectRatio);

                    _device.Hid.Mouse.Update((int)position.X, (int)position.Y, buttons, (int)mouseInput.Scroll.X, (int)mouseInput.Scroll.Y, true);
                    
                    ArrayPool<bool>.Shared.Return(mouseInput.ButtonState);
                }
                else
                {
                    _device.Hid.Mouse.Update(0, 0);
                }

                _device.TamperMachine.UpdateInput(_hleInputStates);
            }
        }

        public InputConfig GetPlayerInputConfigByIndex(int index)
        {
            lock (_lock)
            {
                NpadController controller = _controllers[index];

                return controller?.ActiveConfig ?? _inputConfig.FirstOrDefault(x => x.PlayerIndex == (Common.Configuration.Hid.PlayerIndex)index);
            }
        }

        protected virtual void Dispose(bool disposing)
        {
            if (disposing)
            {
                lock (_lock)
                {
                    if (!_isDisposed)
                    {
                        _cemuHookClient.Dispose();

                        _gamepadDriver.OnGamepadConnected -= HandleOnGamepadConnected;
                        _gamepadDriver.OnGamepadDisconnected -= HandleOnGamepadDisconnected;

                        for (int i = 0; i < _controllers.Length; i++)
                        {
                            _controllers[i]?.Dispose();
                        }

                        _isDisposed = true;
                    }
                }
            }
        }

        public void Dispose()
        {
            GC.SuppressFinalize(this);
            Dispose(true);
        }
    }
}
