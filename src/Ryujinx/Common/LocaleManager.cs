using Gommon;
using Ryujinx.Ava.Systems;
using Ryujinx.Ava.Systems.Configuration;
using Ryujinx.Ava.UI.ViewModels;
using Ryujinx.Common;
using Ryujinx.Common.Utilities;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json.Serialization;

namespace Ryujinx.Ava.Common.Locale
{
    class LocaleManager : BaseModel
    {
        private const string DefaultLanguageCode = "en_US";

        private readonly Dictionary<LocaleKeys, string> _localeStrings;
        private readonly ConcurrentDictionary<LocaleKeys, object[]> _dynamicValues;
        private string _localeLanguageCode;
        public string CurrentLanguageCode => _localeLanguageCode;
        public static LocaleManager Instance { get; } = new();
        public event Action LocaleChanged;

        public LocaleManager()
        {
            _localeStrings = new Dictionary<LocaleKeys, string>();
            _dynamicValues = new ConcurrentDictionary<LocaleKeys, object[]>(new Dictionary<LocaleKeys, object[]>
            {
                { LocaleKeys.DialogConfirmationTitle, [RyujinxApp.FullAppName] },
                { LocaleKeys.DialogUpdaterTitle, [RyujinxApp.FullAppName] },
                { LocaleKeys.DialogErrorTitle, [RyujinxApp.FullAppName] },
                { LocaleKeys.DialogWarningTitle, [RyujinxApp.FullAppName] },
                { LocaleKeys.DialogExitTitle, [RyujinxApp.FullAppName] },
                { LocaleKeys.DialogStopEmulationTitle, [RyujinxApp.FullAppName] },
                { LocaleKeys.RyujinxInfo, [RyujinxApp.FullAppName] },
                { LocaleKeys.RyujinxConfirm, [RyujinxApp.FullAppName] },
                { LocaleKeys.RyujinxUpdater, [RyujinxApp.FullAppName] },
                { LocaleKeys.RyujinxRebooter, [RyujinxApp.FullAppName] },
                { LocaleKeys.CompatibilityListSearchBoxWatermarkWithCount, [CompatibilityDatabase.Entries.Length] },
                { LocaleKeys.CompatibilityListTitle, [CompatibilityDatabase.Entries.Length] }
            });

            Load();
        }

        private void Load()
        {
            string localeLanguageCode = CultureInfo.CurrentCulture.Name.Replace('-', '_');
            if (Program.PreviewerDetached && ConfigurationState.Instance.UI.LanguageCode.Value is { } lang)
            {
                if (!string.IsNullOrEmpty(lang))
                    localeLanguageCode = lang;
            }

            LoadLanguage(localeLanguageCode);

            // Save whatever we ended up with.
            if (Program.PreviewerDetached)
            {
                ConfigurationState.Instance.UI.LanguageCode.Value = _localeLanguageCode;

                ConfigurationState.Instance.ToFileFormat().SaveConfig(Program.ConfigurationPath);
            }
        }

        public static string GetUnformatted(LocaleKeys key) => Instance.Get(key);

        public static string GetFormatted(LocaleKeys key, params object[] values) 
            => GetUnformatted(key).Format(values);

        public static string FormatDynamicValue(LocaleKeys key, params object[] values)
            => Instance.UpdateAndGetDynamicValue(key, values);

        public static void Associate(LocaleKeys key, params object[] values)
            => Instance.SetDynamicValues(key, values);

        public string Get(LocaleKeys key) =>
            _localeStrings.TryGetValue(key, out string value)
                ? value
                : key.ToString();

        public string this[LocaleKeys key]
        {
            get
            {
                // Check if the locale contains the key.
                if (_localeStrings.TryGetValue(key, out string value))
                {
                    // Check if the localized string needs to be formatted.
                    if (_dynamicValues.TryGetValue(key, out object[] dynamicValue))
                        try
                        {
                            return string.Format(value, dynamicValue);
                        }
                        catch
                        {
                            // If formatting the text failed,
                            // continue to the below line & return the text without formatting.
                        }

                    return value;
                }

                return key.ToString(); // If the locale text doesn't exist return the key.
            }
            set
            {
                _localeStrings[key] = value;

                OnPropertyChanged();
            }
        }

        public bool IsRTL() =>
            _localeLanguageCode switch
            {
                "ar_SA" or "he_IL" => true,
                _ => false
            };

        public void SetDynamicValues(LocaleKeys key, params object[] values)
        {
            _dynamicValues[key] = values;

            OnPropertyChanged("Translation");
        }

        public string UpdateAndGetDynamicValue(LocaleKeys key, params object[] values)
        {
            SetDynamicValues(key, values);

            return this[key];
        }

        public void LoadLanguage(string languageCode)
        {
            Dictionary<LocaleKeys, string> locale = LoadJsonLanguage(languageCode);

            if (locale == null)
            {
                _localeLanguageCode = DefaultLanguageCode;
                locale = LoadJsonLanguage(_localeLanguageCode);
            }
            else
            {
                _localeLanguageCode = languageCode;
            }

            foreach ((LocaleKeys key, string val) in locale)
            {
                _localeStrings[key] = val;
            }

            OnPropertyChanged("Translation");

            LocaleChanged?.Invoke();
        }

        private static LocalesData? _localeData;

        private static Dictionary<LocaleKeys, string> LoadJsonLanguage(string languageCode)
        {
            Dictionary<LocaleKeys, string> localeStrings = new();

            if (_localeData is null)
            {
                Dictionary<string, LocalesJson> locales = [];

                foreach (string uri in EmbeddedResources.GetAllAvailableResources("Ryujinx/Assets/Locales", ".json"))
                {
                    string path = uri[..^".json".Length];
                    path = path.Replace('.', '/');
                    path = path.Append(".json");
                    
                    locales.TryAdd(Path.GetFileName(path), EmbeddedResources.ReadAllText(path)
                            .Into(it => JsonHelper.Deserialize(it, LocalesJsonContext.Default.LocalesJson)));
                }
                
                _localeData = new LocalesData
                {
                    Languages = EmbeddedResources.ReadAllText("Ryujinx/Assets/Languages.json")
                        .Into(it => JsonHelper.Deserialize(it, LanguagesJsonContext.Default.LanguagesJson)).Languages.Keys.ToList(),
                    LocalesFiles = locales
                };
                

            }

            foreach ((string fileName, LocalesJson file) in _localeData.Value.LocalesFiles)
            {
                foreach (LocalesEntry locale in file.Locales)
                {
                    if (locale.Translations.Count < _localeData.Value.Languages.Count)
                    {
                        throw new Exception(
                            $"Locale key {{{locale.ID}}} is missing languages! Has {locale.Translations.Count} translations, expected {_localeData.Value.Languages.Count}!");
                    }

                    if (locale.Translations.Count > _localeData.Value.Languages.Count)
                    {
                        throw new Exception(
                            $"Locale key {{{locale.ID}}} has too many languages! Has {locale.Translations.Count} translations, expected {_localeData.Value.Languages.Count}!");
                    }

                    if (!Enum.TryParse<LocaleKeys>(fileName == "Root.json" ? locale.ID : $"{fileName[..^".json".Length]}_{locale.ID}" , out LocaleKeys localeKey))
                        continue;

                    string str = locale.Translations.TryGetValue(languageCode, out string val) && !string.IsNullOrEmpty(val)
                        ? val
                        : locale.Translations[DefaultLanguageCode];

                    if (string.IsNullOrEmpty(str))
                    {
                        throw new Exception(
                            $"Locale key '{locale.ID}' has no valid translations for desired language {languageCode}! {DefaultLanguageCode} is an empty string or null");
                    }

                    localeStrings[localeKey] = str;
                }
            }

            return localeStrings;
        }
    }

    public struct LocalesData
    {
        public List<string> Languages { get; set; }
        public Dictionary<string, LocalesJson> LocalesFiles { get; set; }
    }

    public struct LanguagesJson
    {
        public Dictionary<string, string> Languages { get; set; }
    }

    public struct LocalesJson
    {
        public List<LocalesEntry> Locales { get; set; }
    }

    public struct LocalesEntry
    {
        public string ID { get; set; }
        public Dictionary<string, string> Translations { get; set; }
    }

    [JsonSourceGenerationOptions(WriteIndented = true)]
    [JsonSerializable(typeof(LocalesJson))]
    internal partial class LocalesJsonContext : JsonSerializerContext;
    
    [JsonSourceGenerationOptions(WriteIndented = true)]
    [JsonSerializable(typeof(LanguagesJson))]
    internal partial class LanguagesJsonContext : JsonSerializerContext;
}
