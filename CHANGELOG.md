# Ryujinx Changelog

All updates to this Ryujinx branch will be documented in this file.

## [1.3.2](<https://git.ryujinx.app/ryubing/ryujinx/-/releases/1.3.2>) - 2025-06-09

## [1.3.1](<https://git.ryujinx.app/ryubing/ryujinx/-/releases/1.3.1>) - 2025-04-23

## [1.2.86](<https://github.com/Ryubing/Stable-Releases/releases/tag/1.2.86>) - 2025-03-13

## [1.2.82](<https://web.archive.org/web/20250312010534/https://github.com/Ryubing/Ryujinx/releases/tag/1.2.82>) - 2025-02-16

## [1.2.80-81](<https://web.archive.org/web/20250302064257/https://github.com/Ryubing/Ryujinx/releases/tag/1.2.81>) - 2025-01-22

## [1.2.78](<https://web.archive.org/web/20250301174537/https://github.com/Ryubing/Ryujinx/releases/tag/1.2.78>) - 2024-12-19

## [1.2.73-1.2.76](<https://web.archive.org/web/20250209202612/https://github.com/Ryubing/Ryujinx/releases/tag/1.2.76>) - 2024-11-19
A list of notable changes can be found on the release linked in the version number above.

Additionally, 1.2.74 & 75 were fixes for uploading Windows build artifacts.

1.2.76 fixes a rare crash on startup.

## [1.2.72](<https://git.ryujinx.app/ryubing/ryujinx/-/tags/1.2.72>) - 2024-11-03
PRs [#163](<https://web.archive.org/web/20241123015123/https://github.com/GreemDev/Ryujinx/pull/163>), [#164](<https://web.archive.org/web/20250307192526/https://github.com/Ryubing/Ryujinx/pull/164>), [#139](<https://web.archive.org/web/20250306123457/https://github.com/Ryubing/Ryujinx/pull/139>)
### HLE:
 - Add DebugMouse HID device.
   - Fixes "Clock Tower Rewind" crashing while loading.
### Audio:
 - Fix index bounds check in GetCoefficientAtIndex.
   - Fixes crashing in Super Mario Party Jamboree.
### misc:
 - Update macOS distribution .icns.

## [1.2.69](<https://git.ryujinx.app/ryubing/ryujinx/-/tags/1.2.69>) - 2024-11-01
### Infra:
  - Compile the native libraries into the Ryujinx executable.
  - Remove `libarmeilleure-jitsupport.dylib` from Windows & Linux releases (dylibs are macOS-only)
### Misc:
  - Remove custom themes in config.
    - This is a leftover from the GTK UI, as Avalonia does not have custom themes.
  - Replace "" with `string.Empty`.
  - Code cleanups & simplifications.

## [1.2.67](<https://git.ryujinx.app/ryubing/ryujinx/-/tags/1.2.67>) - 2024-11-01
PRs [#36](<https://web.archive.org/web/20250306215917/https://github.com/Ryubing/Ryujinx/pull/36>), [#135](<https://web.archive.org/web/20241122135125/https://github.com/GreemDev/Ryujinx/pull/135>)

### GUI:
  - Set UseFloatingWatermark to false when watermark is empty
    - Should prevent the text prompt box from having weird jumpy behavior.
### GPU:
  - Increase the amount of VRAM cache available for textures based on selected DRAM amount.
### Misc:
  - Fix homebrew loading.


## [1.2.64](https://git.ryujinx.app/ryubing/ryujinx/-/tags/1.2.64) - 2024-10-30
PRs [#92](https://web.archive.org/web/20241118052724/https://github.com/GreemDev/Ryujinx/pull/92), ~~[#96](https://github.com/GreemDev/Ryujinx/pull/96)~~, ~~[#97](https://github.com/GreemDev/Ryujinx/pull/97)~~,  [#101](https://web.archive.org/web/20250306223605/https://github.com/Ryubing/Ryujinx/pull/101), ~~[#103](https://github.com/GreemDev/Ryujinx/pull/103)~~
### GUI:
- Option to show classic-style title bar. Requires restart of emulator to take effect.
  - This is only relevant on Windows. Other Operating Systems default to this being on and not being changeable, because the custom (current) title bar only works on Windows in the first place.
### i18n:
- it_IT: 
  - Add missing Italian strings.
- pt_BR:
  - Add missing Brazilian Portuguese strings.
- fr_FR:
  - Fix some French strings.
### MISC:
- Higher-res logo.

## 1.2.59 - 2024-10-27

PRs ~~[#88](https://github.com/GreemDev/Ryujinx/pull/88), [#87](https://github.com/GreemDev/Ryujinx/pull/87)~~
### i18n:
- fr_FR:
  - Add missing translations for new features & fix a couple wrong ones.
  - Fix Ignore Missing Services / Ignore Applet tooltip.

## 1.2.57 - 2024-10-27
PRs ~~[#60](https://github.com/GreemDev/Ryujinx/pull/60)~~, [#42](https://web.archive.org/web/20241126203614/https://github.com/GreemDev/Ryujinx/pull/42)
### GUI:
- Automatically remove invalid DLC & updates as part of autoload.
- Added Thai translation for Ignore Applet hover tooltip.
### INPUT:
- When using multiple gamepads, when reconnecting they will no longer be mixed up between players.

## 1.2.50 - 2024-10-25
### GUI:
- Fix crash when using "delete all" button in mod manager.
### Updater:
- Remove Avalonia migration code.
### MISC:
- Replace references to IntPtr/UIntPtr to nint/nuint.

## 1.2.45 - 2024-10-25
### GUI:
- Added program icon to windows other than the main.
- Reference translations added in the last version.
- Shader compile counter is now translated.
### RPC:
- Added SONIC X SHADOW GENERATIONS asset image.
### MISC:
- Code cleanup.

## 1.2.44 - 2024-10-25
PR [#59](https://web.archive.org/web/20241125060420/https://github.com/GreemDev/Ryujinx/pull/59)
### GUI:
- Add descriptions for "ignoring applet" translated into other languages.

NOTE: The translation isn't referenced in the code yet, it will be in the next update. These are just the translations.

## Hotfix: 1.2.43 - 2024-10-24
### GUI:
- Do not enable Ignore Applet by default when upgrading config version.

## 1.2.42 - 2024-10-24
Sources:

Init function: [archive of github.com/MutantAura/Ryujinx/commit/9cef4ceba40d66492ff775af793ff70e6e7551a9](https://web.archive.org/web/20241122193401/https://github.com/MutantAura/Ryujinx/commit/9cef4ceba40d66492ff775af793ff70e6e7551a9)

Shader counter: ~~https://github.com/MutantAura/Ryujinx/commit/67b873645fd593e83d042a77bf7ab12e5ec97357~~ Original commit has been lost

Thanks MutantAura :D
### GUI:
- Implement shader compile counter (currently not translated, will change, need to pull changes.)
- Remove graphics backend / GPU name event logic in favor of a single init function.

## 1.2.41 - 2024-10-24
PR ~~[#54](https://github.com/GreemDev/Ryujinx/pull/54)~~

Thanks Whitescatz!
### i18n:
- th_TH (Thai): Added missing translations, reduce transliterated words, fix grammar.

## 1.2.40 - 2024-10-23
PR ~~[#40](https://github.com/GreemDev/Ryujinx/pull/40)~~

Thanks Вова С!
### GUI:
- Add option to ignore controller applet upon start.

*This option is under the hacks section for a reason; it ignores intended behavior. Use with caution.

## 1.2.39 - 2024-10-23
### MISC:
- Null-coalesce autoloaddirs on config load.
  - Should prevent crashing on config loads in some circumstances.

## 1.2.38 - 2024-10-23
PR [#51](https://web.archive.org/web/20241127022413/https://github.com/GreemDev/Ryujinx/pull/51)
### i18n:
- zh_CH (Simplified Chinese): Add some missing translations.

## 1.2.37 - 2024-10-23
PR [#37](https://web.archive.org/web/20241123010103/https://github.com/GreemDev/Ryujinx/pull/37)

Thanks Last Breath!
### GUI: 
- Set the default controller to the Pro Controller.

## 1.2.36 - 2024-10-21
PR ~~[#30](https://github.com/GreemDev/Ryujinx/pull/30)~~
### GUI:
- Fix repeated dialog popup notifying you of new updates when there aren't any, while having a bundled update inside an XCI and an external update file.

## 1.2.35 - 2024-10-21
PR [#32](https://web.archive.org/web/20241127010942/https://github.com/GreemDev/Ryujinx/pull/32)
### GUI:
- Replace "expand DRAM" option with a DRAM size dropdown.
  - Allows for using mods which require a ridiculous amount of memory to allocate from.

## 1.2.34 - 2024-10-21
PR [#29](https://web.archive.org/web/20241125093029/https://github.com/GreemDev/Ryujinx/pull/29)
### GUI:
- Fix duplicate controller names when 2 controllers of the same type are connected.
### INPUT:
- Fix invert x, y, and rotate when mapping physical left stick to logical right stick and vice versa.

## 1.2.32-1.2.33 - 2024-10-21
### i18n:   
- fr_FR: Added missing strings and general improvements. 
  - Improve French translation clarity & add missing translations by Nebroc351, helped by Fredy27 in the Discord.

## 1.2.31 - 2024-10-21
### GUI: 
- Revert maximized = fullscreen change.
  - Fixes fullscreen not hiding the Windows taskbar.

## 1.2.30 - 2024-10-19
### GUI: 
- Reload game list on locale change.
- Add keybinds to useful things (namely opening Amiibo scan window (Ctrl + A) and the scan button (Enter)).
- Reset RPC state when AppHost stops.

### MISC:
- XML & code cleanups.

## 1.2.29 - 2024-10-19
### GUI: 
- Remove references to ryujinx.org in the localization files.
- Switch from downloading amiibo.ryujinx.org to just referencing a file in the repo & images in the repo, under assets/amiibo.

This fork is now entirely independent of the existing Ryujinx infrastructure, and as such the Amiibo features will continue to work in my version when they break in the mainline version.

## 1.2.28 - 2024-10-17
### GUI: 
- Fix dialog popups doubling the window controls and laying text over the menu bar.

## 1.2.26 - 2024-10-17
### I18n: 
Added Low-power PPTC mode strings to the translation files.
### GUI:
- Remove OS-provided title bar and put the Ryujinx logo next to "File" in the menu bar.
  - What was in the title bar, Ryujinx version & current game information, is still visible by hovering the Ryujinx icon.
- Added icons to many actions in dropdown menus.
### RPC:
- Added Kirby and the Forgotten Land, Elder Scrolls V Skyrim, and Hyrule Warriors: Age of Calamity to the RPC assets.

## 1.2.25 - 2024-10-14
### CPU: 
- Add low-power PPTC mode.
  - Specifically, this setting causes the core count to get reduced by two-thirds, for lower-power but still fast loading if desired, and for unstable CPUs.

## 1.2.24 - 2024-10-14
### SDL: 
- Move Mouse & MouseDriver to Input project, instead of Headless.

## 1.2.22 - 2024-10-12
### GUI/RPC: 
- Added RDR, Luigi's Mansion 2 HD & 3 asset images.
### MISC:
- Minor code cleanups & improvements.
- Removed duplicate executable in the release bundle (leftovers from GTK & Avalonia dual releases).
- Removed Avalonia test release bundle, which was kept in Ryujinx for the OG Avalonia testers. That doesn't apply to this fork, so it's removed. 

## 1.2.21 - 2024-10-11
### GUI/RPC: 
- Add game version string when hovering large image asset.
- Add version information about this fork to the Ryujinx logo (big when in main menu, small when in game) when hovering.

## 1.2.20 - 2024-10-11
### MISC:
- Code cleanups & remove references to Ryujinx Patreon & Twitter.
### GUI:
- Add more Discord presence assets.

## 1.2.1-1.2.19 - 2024-10-08 - 2024-10-11
### GUI/INFRA/MISC:
- Remove GTK UI.
- Autoload DLC/Updates from dir ([#12](https://web.archive.org/web/20241127004005/https://github.com/GreemDev/Ryujinx/pull/12)).
- Changed executable icon to rainbow logo.
- Extract Data > Logo now also extracts the square thumbnail you see for the game in the UI. 
- The "use random UUID hack" checkbox in the Amiibo screen now remembers its last state when you reopen the window in a given session.
