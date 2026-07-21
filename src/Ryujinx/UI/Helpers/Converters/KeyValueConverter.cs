using Avalonia.Data.Converters;
using Ryujinx.Ava.Common.Locale;
using Ryujinx.Common.Configuration.Hid;
using Ryujinx.Common.Configuration.Hid.Controller;
using System;
using System.Collections.Generic;
using System.Globalization;
using Key = Ryujinx.Input.Key;
using HidKey = Ryujinx.Common.Configuration.Hid.Key;

namespace Ryujinx.Ava.UI.Helpers
{
    internal class KeyValueConverter : IValueConverter
    {
        public static readonly KeyValueConverter Instance = new();

        private static readonly Dictionary<GamepadInputId, LocaleKeys> _gamepadInputIdMap = new()
        {
            { GamepadInputId.LeftStick, LocaleKeys.GamepadLeftStick },
            { GamepadInputId.RightStick, LocaleKeys.GamepadRightStick },
            { GamepadInputId.LeftShoulder, LocaleKeys.GamepadLeftShoulder },
            { GamepadInputId.RightShoulder, LocaleKeys.GamepadRightShoulder },
            { GamepadInputId.LeftTrigger, LocaleKeys.GamepadLeftTrigger },
            { GamepadInputId.RightTrigger, LocaleKeys.GamepadRightTrigger },
            { GamepadInputId.DpadUp, LocaleKeys.GamepadDpadUp},
            { GamepadInputId.DpadDown, LocaleKeys.GamepadDpadDown},
            { GamepadInputId.DpadLeft, LocaleKeys.GamepadDpadLeft},
            { GamepadInputId.DpadRight, LocaleKeys.GamepadDpadRight},
            { GamepadInputId.Minus, LocaleKeys.GamepadMinus},
            { GamepadInputId.Plus, LocaleKeys.GamepadPlus},
            { GamepadInputId.Guide, LocaleKeys.GamepadGuide},
            { GamepadInputId.Misc1, LocaleKeys.GamepadMisc1},
            { GamepadInputId.Paddle1, LocaleKeys.GamepadPaddle1},
            { GamepadInputId.Paddle2, LocaleKeys.GamepadPaddle2},
            { GamepadInputId.Paddle3, LocaleKeys.GamepadPaddle3},
            { GamepadInputId.Paddle4, LocaleKeys.GamepadPaddle4},
            { GamepadInputId.Touchpad, LocaleKeys.GamepadTouchpad},
            { GamepadInputId.SingleLeftTrigger0, LocaleKeys.GamepadSingleLeftTrigger0},
            { GamepadInputId.SingleRightTrigger0, LocaleKeys.GamepadSingleRightTrigger0},
            { GamepadInputId.SingleLeftTrigger1, LocaleKeys.GamepadSingleLeftTrigger1},
            { GamepadInputId.SingleRightTrigger1, LocaleKeys.GamepadSingleRightTrigger1},
            { GamepadInputId.Unbound, LocaleKeys.KeyboardLayout_KeyUnbound},
        };

        private static readonly Dictionary<StickInputId, LocaleKeys> _stickInputIdMap = new()
        {
            { StickInputId.Left, LocaleKeys.StickLeft},
            { StickInputId.Right, LocaleKeys.StickRight},
            { StickInputId.Unbound, LocaleKeys.KeyboardLayout_KeyUnbound},
        };

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return value switch
            {
                Key key => KeyboardLayoutLocaleHelper.TryGetSemanticLabel(key, out string localizedKeyLabel)
                        ? localizedKeyLabel
                        : key.ToString(),
                HidKey key => KeyboardLayoutLocaleHelper.TryGetSemanticLabel((Key)(int)key, out string localizedHidKeyLabel)
                        ? localizedHidKeyLabel
                        : key.ToString(),
                PhysicalKey physicalKey => PhysicalKeyLabelHelper.GetDisplayString(physicalKey),
                GamepadInputId gamepadInputId => GetLocalizedMappedValue(gamepadInputId, _gamepadInputIdMap),
                StickInputId stickInputId => GetLocalizedMappedValue(stickInputId, _stickInputIdMap),
                _ => string.Empty,
            };
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }

        private static string GetLocalizedMappedValue<T>(T value, IReadOnlyDictionary<T, LocaleKeys> map) where T : notnull
        {
            return map.TryGetValue(value, out LocaleKeys localeKey)
                ? LocaleManager.Instance[localeKey]
                : value.ToString();
        }
    }
}
