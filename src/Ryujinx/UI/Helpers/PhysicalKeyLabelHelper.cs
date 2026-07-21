using Avalonia.Input;
using Ryujinx.Ava.Common.Locale;
using Ryujinx.Ava.Input;
using Ryujinx.Common.Configuration;
using Ryujinx.Common.Logging;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using AvaPhysicalKey = Avalonia.Input.PhysicalKey;
using ConfigPhysicalKey = Ryujinx.Common.Configuration.Hid.PhysicalKey;
using InputKey = Ryujinx.Input.Key;

namespace Ryujinx.Ava.UI.Helpers
{
    internal static class PhysicalKeyLabelHelper
    {
        private const string ObservedLabelsFileName = "keyboard_layout_labels.json";
        private static readonly ConcurrentDictionary<ConfigPhysicalKey, string> _observedLayoutLabels = new();
        private static readonly object _observedLayoutLabelsLock = new();
        private static readonly JsonSerializerOptions _serializerOptions = new()
        {
            WriteIndented = true,
            AllowTrailingCommas = true,
            ReadCommentHandling = JsonCommentHandling.Skip
        };
        private static bool _observedLayoutLabelsLoaded;
        public static event Action LabelsChanged;

        public static string GetDisplayString(ConfigPhysicalKey key)
        {
            EnsureObservedLayoutLabelsLoaded();

            if (KeyboardLayoutLocaleHelper.TryGetPhysicalLabel(key, out string localizedLabel))
            {
                return localizedLabel;
            }

            if (_observedLayoutLabels.TryGetValue(key, out string observedLabel))
            {
                return observedLabel;
            }

            if (TryGetFallbackPrintableKeyLabel(key, out string label))
            {
                return label;
            }

            return key.ToString();
        }

        public static void ObserveKeyPress(object sender, KeyEventArgs args)
        {
            EnsureObservedLayoutLabelsLoaded();

            if (args.KeyModifiers != KeyModifiers.None)
            {
                return;
            }

            InputKey inputKey = AvaloniaKeyboardMappingHelper.ToInputKey(args.PhysicalKey);
            if (!TryConvertToConfigPhysicalKey(inputKey, out ConfigPhysicalKey physicalKey) ||
                KeyboardLayoutLocaleHelper.TryGetPhysicalLocaleKey(physicalKey, out _))
            {
                return;
            }

            if (TryNormalizeObservedPrintableLabel(args.KeySymbol, out string label))
            {
                if (IsCapsLockOn() && !char.IsLetter(label[0]))
                {
                    return;
                }

                if (_observedLayoutLabels.TryGetValue(physicalKey, out string existingLabel) && existingLabel == label)
                {
                    return;
                }

                _observedLayoutLabels[physicalKey] = label;
                SaveObservedLayoutLabels();
                LabelsChanged?.Invoke();
            }
        }

        private static void EnsureObservedLayoutLabelsLoaded()
        {
            if (_observedLayoutLabelsLoaded)
            {
                return;
            }

            lock (_observedLayoutLabelsLock)
            {
                if (_observedLayoutLabelsLoaded)
                {
                    return;
                }

                string labelsPath = GetObservedLabelsPath();
                if (!File.Exists(labelsPath))
                {
                    _observedLayoutLabelsLoaded = true;
                    return;
                }

                try
                {
                    string labelsJson = File.ReadAllText(labelsPath);
                    Dictionary<string, string> labels = JsonSerializer.Deserialize<Dictionary<string, string>>(labelsJson, _serializerOptions);

                    if (labels != null)
                    {
                        foreach ((string key, string value) in labels)
                        {
                            if (Enum.TryParse(key, out ConfigPhysicalKey physicalKey) &&
                                !string.IsNullOrEmpty(value))
                            {
                                _observedLayoutLabels[physicalKey] = value;
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Logger.Warning?.Print(LogClass.UI, $"Unable to load observed keyboard layout labels from '{labelsPath}': {ex.Message}");
                }
                finally
                {
                    _observedLayoutLabelsLoaded = true;
                }
            }
        }

        private static void SaveObservedLayoutLabels()
        {
            lock (_observedLayoutLabelsLock)
            {
                try
                {
                    Dictionary<string, string> labels = new();

                    foreach ((ConfigPhysicalKey key, string value) in _observedLayoutLabels)
                    {
                        labels[key.ToString()] = value;
                    }

                    File.WriteAllText(GetObservedLabelsPath(), JsonSerializer.Serialize(labels, _serializerOptions));
                }
                catch (Exception ex)
                {
                    Logger.Warning?.Print(LogClass.UI, $"Unable to save observed keyboard layout labels: {ex.Message}");
                }
            }
        }

        private static string GetObservedLabelsPath()
        {
            return Path.Combine(AppDataManager.BaseDirPath, ObservedLabelsFileName);
        }

        private static bool TryGetFallbackPrintableKeyLabel(ConfigPhysicalKey key, out string label)
        {
            // The legacy enum name for the ISO extra key is misleading, so give it a distinct physical label.
            if (key == ConfigPhysicalKey.Grave)
            {
                label = "<>";
                return true;
            }

            if (!AvaloniaKeyboardMappingHelper.TryGetAvaPhysicalKey((InputKey)(int)key, out AvaPhysicalKey avaPhysicalKey))
            {
                label = string.Empty;
                return false;
            }

            label = PhysicalKeyExtensions.ToQwertyKeySymbol(avaPhysicalKey, false);

            if (string.IsNullOrEmpty(label) || label.Length != 1 || char.IsControl(label[0]))
            {
                label = string.Empty;
                return false;
            }

            if (char.IsLetter(label[0]))
            {
                label = char.ToUpperInvariant(label[0]).ToString();
            }

            return true;
        }

        private static bool IsCapsLockOn()
        {
            try
            {
                return OperatingSystem.IsWindows() && Console.CapsLock;
            }
            catch (Exception ex)
            {
                Logger.Debug?.Print(LogClass.UI, $"CapsLock state query failed: {ex.Message}");
                return false;
            }
        }

        private static bool TryNormalizeObservedPrintableLabel(string keySymbol, out string label)
        {
            if (string.IsNullOrEmpty(keySymbol) || keySymbol.Length != 1 || char.IsControl(keySymbol[0]))
            {
                label = string.Empty;
                return false;
            }

            label = char.IsLetter(keySymbol[0])
                ? char.ToUpperInvariant(keySymbol[0]).ToString()
                : keySymbol;

            return true;
        }

        private static bool TryConvertToConfigPhysicalKey(InputKey key, out ConfigPhysicalKey physicalKey)
        {
            if (key is >= InputKey.Unknown and < InputKey.Count)
            {
                physicalKey = (ConfigPhysicalKey)(int)key;
                return true;
            }

            physicalKey = ConfigPhysicalKey.Unknown;
            return false;
        }
    }
}
