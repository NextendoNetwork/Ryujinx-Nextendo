
# Ryubing Locales

Ryubing Locales uses a custom format, which uses a file for defining the supported languages and a folder of json files for the locales themselves.
Each json file holds the locales for a specific part of the emulator, e.g. the Setup Wizard locales are in `SetupWizard.json`, and each locale entry in the file includes all the supported languages in the same place.

## Languages
In the `/assets/` folder you will find the `Languages.json` file, which defines all the languages supported by the emulator. 
The file includes a table of the language codes and their language names.

    #Example of the format for Languages.json	
    {
      "Languages": {
        "ar_SA": "اَلْعَرَبِيَّةُ",
        "en_US": "English (US)",
        ...
        "zh_TW": "繁體中文 (台灣)"
      }
    }

## Locales
In the `/assets/Locales/` folder you will find the json files, which define all the locales supported by the emulator. 
Each json file holds locales for a specific part of the emulator in a large array of locale objects.
Each locale is made up an ID used for lookup and a list of the languages and their matching translations.
Any empty string or null value will automatically use the English translation instead in the emulator.

### Format
When adding a new locale, you just need to add the ID and the en_US language translation, then the validation system will add default values for the rest of languages automatically, when rebuilding the project.
If you want to signal that a translation is supposed to match the English translation, you just have to replace the empty string with `null`.
When you want to check what translations are missing for a language just search for `"<lang_code>": ""`, e.g: `"en_US": ""` (but with any other language, as English will never be missing translations).

### Legacy file (Root.json)
Currently all older locales are stored in `Root.json`, but they are slowly being moved into newer, more descriptive json files, to make the locale system more accessible.
Do **not** add new locales to `Root.json`.
If no json file exists for the specific part of the emulator you're working on, you should instead add a new json file for that part.

    #Example of the format for Root.json	
    {
      "Locales": [
        {
          "ID": "MenuBarActionsOpenMiiEditor",
          "Translations": {
            "ar_SA": "",
            "en_US": "Mii Editor",
            ...
            "zh_TW": "Mii 編輯器"
          }
        },
        {
          "ID": "KeyNumber9",
          "Translations": {
            "ar_SA": "٩",
            "en_US": "9",
            ...
            "zh_TW": null
          }
        }
      ]
    }
   