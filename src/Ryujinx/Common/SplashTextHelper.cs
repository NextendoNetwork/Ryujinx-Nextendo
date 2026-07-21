using System.Collections.Generic;
using Ryujinx.Common.Logging;
using Gommon;
using Ryujinx.Ava.Systems.Configuration;
using System.Text.Json;

namespace Ryujinx.Common
{
    public class SplashTextHelper
    {
        public static void PrintSplash()
        {
            string splash = GetSplash();
            
            Logger.Notice.Print(LogClass.Application,  "   ___                 __    _              ");
            Logger.Notice.Print(LogClass.Application, @"  / _ \  __ __ __ __  / /   (_)  ___   ___ _");
            Logger.Notice.Print(LogClass.Application, @" / , _/ / // // // / / _ \ / /  / _ \ / _ `/");
            Logger.Notice.Print(LogClass.Application, @"/_/|_|  \_, / \_,_/ /_.__//_/  /_//_/ \_, / ");
            Logger.Notice.Print(LogClass.Application,  "       /___/                         /___/  ");
            
            if (splash is null)
            {
                Logger.Error?.Print(LogClass.Application, "Failed to fetch Splash Text! Splash JSON is invalid!");
                return;
            }
            
            if (!splash.IsNullOrEmpty())
            {
                Logger.Notice.Print(LogClass.Application, "");
                Logger.Notice.Print(LogClass.Application, splash);
                Logger.Notice.Print(LogClass.Application, "");
            }
        }

        private static string _finalSplash;

        public static string GetSplash()
        {
            if (_finalSplash is null)
            {
                try
                {
                    string data;
                    data = EmbeddedResources.ReadAllText("Ryujinx/Assets/Splashes.json");
                    SplashLocales splashJson = JsonSerializer.Deserialize<SplashLocales>(data);

                    // Fall back to en_US rather than throwing: indexing a language the JSON
                    // doesn't carry raised KeyNotFoundException, the catch below swallowed it,
                    // and the player silently got NO splash at all. An empty list would do the
                    // same via GetRandomElement(), so guard both.
                    string lang = ConfigurationState.Instance.UI.LanguageCode.Value;
                    if (!splashJson.Locales.TryGetValue(lang, out List<string> splashes) || splashes.Count == 0)
                    {
                        splashJson.Locales.TryGetValue("en_US", out splashes);
                    }

                    _finalSplash = splashes?.GetRandomElement() ?? "";
                }
                catch
                {
                    return null;
                }
            }
            
            return _finalSplash;
        }

        private struct SplashLocales
        {
            public Dictionary<string, List<string>> Locales { get; set; }
        }

    }

}
