using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace Ryujinx.BuildValidationTasks
{
    public class LocalesValidationTask : IValidationTask
    {
        static readonly JsonSerializerOptions _jsonOptions = new()
        {
            WriteIndented = true, NewLine = "\n", Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };

        public LocalesValidationTask() { }

        public bool Execute(string projectPath, bool isGitRunner)
        {
            Console.WriteLine("Running Locale Validation Task...");

            bool encounteredIssue = false;
            string langPath = projectPath + "assets/Languages.json";
            string data;

            using (StreamReader sr = new(langPath))
            {
                data = sr.ReadToEnd();
            }

            if (isGitRunner && data.Contains("\r\n"))
                throw new FormatException("Languages.json is using CRLF line endings! It should be using LF line endings, rebuild locally to fix...");

            LanguagesJson langJson;

            try
            {
                langJson = JsonSerializer.Deserialize<LanguagesJson>(data);
            }
            catch (JsonException e)
            {
                throw new JsonException(e.Message); //shorter and easier stacktrace
            }

            foreach ((string code, string lang) in langJson.Languages)
            {
                if (string.IsNullOrEmpty(lang))
                {
                    throw new JsonException($"{code} language name missing!");
                }
            }

            string folderPath = projectPath + "assets/Locales/";

            string[] paths = Directory.GetFiles(folderPath, "*.json", SearchOption.AllDirectories);

            foreach (string path in paths)
            {
                using (StreamReader sr = new(path))
                {
                    data = sr.ReadToEnd();
                }

                if (isGitRunner && data.Contains("\r\n"))
                    throw new FormatException($"{Path.GetFileName(path)} is using CRLF line endings! It should be using LF line endings, rebuild locally to fix...");

                LocalesJson json;

                try
                {
                    json = JsonSerializer.Deserialize<LocalesJson>(data);
                }
                catch (JsonException e)
                {
                    throw new JsonException(e.Message); //shorter and easier stacktrace
                }


                for (int i = 0; i < json.Locales.Count; i++)
                {
                    LocalesEntry locale = json.Locales[i];

                    foreach (string langCode in
                             langJson.Languages.Keys.Where(lang => !locale.Translations.ContainsKey(lang)))
                    {
                        encounteredIssue = true;

                        if (!isGitRunner)
                        {
                            locale.Translations.Add(langCode, string.Empty);
                            Console.WriteLine($"Added '{langCode}' to Locale '{locale.ID}'");
                        }
                        else
                        {
                            Console.WriteLine($"Missing '{langCode}' in Locale '{locale.ID}'!");
                        }
                    }

                    foreach (string langCode in langJson.Languages.Keys.Where(lang =>
                                 locale.Translations.ContainsKey(lang) && lang != "en_US" &&
                                 locale.Translations[lang] == locale.Translations["en_US"]))
                    {
                        encounteredIssue = true;

                        if (!isGitRunner)
                        {
                            locale.Translations[langCode] = string.Empty;
                            Console.WriteLine(
                                $"Language '{langCode}' is a duplicate of en_US in Locale '{locale.ID}'! Resetting it...");
                        }
                        else
                        {
                            Console.WriteLine(
                                $"Language '{langCode}' is a duplicate of en_US in Locale '{locale.ID}'!");
                        }
                    }

                    locale.Translations = locale.Translations.OrderBy(pair => pair.Key)
                        .ToDictionary(pair => pair.Key, pair => pair.Value);
                    json.Locales[i] = locale;
                }

                if (isGitRunner && encounteredIssue)
                    throw new JsonException("1 or more locales are invalid! Rebuild locally to fix...");

                string jsonString = JsonSerializer.Serialize(json, _jsonOptions);

                using (StreamWriter sw = new(path))
                {
                    sw.Write($"{jsonString}\n");
                }
            }

            Console.WriteLine("Finished Locale Validation Task!");

            return true;
        }

        struct LanguagesJson
        {
            public Dictionary<string, string> Languages { get; set; }
        }

        struct LocalesJson
        {
            public List<LocalesEntry> Locales { get; set; }
        }

        struct LocalesEntry
        {
            public string ID { get; set; }
            public Dictionary<string, string> Translations { get; set; }
        }
    }
}
