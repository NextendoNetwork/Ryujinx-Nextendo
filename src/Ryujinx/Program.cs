using Avalonia;
using Avalonia.Threading;
using DiscordRPC;
using Gommon;
using Projektanker.Icons.Avalonia;
using Projektanker.Icons.Avalonia.FontAwesome;
using Projektanker.Icons.Avalonia.MaterialDesign;
using Ryujinx.Ava.Systems;
using Ryujinx.Ava.Systems.Configuration;
using Ryujinx.Ava.Systems.Configuration.System;
using Ryujinx.Ava.UI.Helpers;
using Ryujinx.Ava.UI.Windows;
using Ryujinx.Ava.Utilities;
using Ryujinx.Ava.Utilities.SystemInfo;
using Ryujinx.Common;
using Ryujinx.Common.Configuration;
using Ryujinx.Common.GraphicsDriver;
using Ryujinx.Common.Logging;
using Ryujinx.Common.SystemInterop;
using Ryujinx.Common.Utilities;
using Ryujinx.Graphics.RenderDocApi;
using Ryujinx.Graphics.Vulkan.MoltenVK;
using Ryujinx.Headless;
using Ryujinx.SDL3.Common;
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Threading.Tasks;

namespace Ryujinx.Ava
{
    internal static class Program
    {
        public static double WindowScaleFactor { get; set; }
        public static double DesktopScaleFactor { get; set; } = 1.0;
        public static string Version { get; private set; }
        public static string ConfigurationPath { get; private set; }
        public static string GlobalConfigurationPath { get; private set; }
        public static bool UseExtraConfig { get; set; }
        public static bool PreviewerDetached { get; private set; }
        public static bool UseHardwareAcceleration { get; private set; }
        public static string BackendThreadingArg { get; private set; }
        public static bool CoreDumpArg { get; private set; }

        private const uint MbIconwarning = 0x30;

        [STAThread]
        public static int Main(string[] args)
        {
            Version = ReleaseInformation.Version;

            if (OperatingSystem.IsWindows())
            {
                if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 19041))
                {
                    Logger.Error?.PrintMsg(LogClass.Application, "Ryujinx is not intended to be run on an outdated version of Windows. Exiting...");
                    _ = Win32NativeInterop.MessageBoxA(nint.Zero,
                        "You are running an outdated version of Windows.\n\nRyujinx supports Windows 10 version 20H1 and newer.\n",
                        $"Ryujinx {Version}", MbIconwarning);
                    return 0;
                }

                string onedriveFiles = Environment.GetEnvironmentVariable("Onedrive");
                string onedriveConsumerFiles = Environment.GetEnvironmentVariable("OnedriveConsumer");
                string onedriveCommercialFiles = Environment.GetEnvironmentVariable("OnedriveCommercial");

                // Apparently not everyone has OneDrive shoved onto their system.
                if ((onedriveFiles is not null && Environment.CurrentDirectory.StartsWithIgnoreCase(onedriveFiles))
                    || (onedriveConsumerFiles is not null && Environment.CurrentDirectory.StartsWithIgnoreCase(onedriveConsumerFiles))
                    || (onedriveCommercialFiles is not null && Environment.CurrentDirectory.StartsWithIgnoreCase(onedriveCommercialFiles)))
                {
                    Logger.Error?.PrintMsg(LogClass.Application, "Ryujinx is not intended to be run from a OneDrive folder. Exiting...");
                    _ = Win32NativeInterop.MessageBoxA(nint.Zero,
                        "Ryujinx is not intended to be run from a OneDrive folder. Please move it out and relaunch.",
                        $"Ryujinx {Version}", MbIconwarning);
                    return 0;
                }

                string programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
                string programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);

                if (Environment.CurrentDirectory.StartsWithIgnoreCase(programFiles) ||
                    Environment.CurrentDirectory.StartsWithIgnoreCase(programFilesX86))
                {
                    Logger.Error?.PrintMsg(LogClass.Application, "Ryujinx is not intended to be run from the Program Files folder. Exiting...");
                    _ = Win32NativeInterop.MessageBoxA(nint.Zero,
                        "Ryujinx is not intended to be run from the Program Files folder. Please move it out and relaunch.",
                        $"Ryujinx {Version}", MbIconwarning);
                    return 0;
                }

                // The names of everything here makes no sense for what this actually checks for. Thanks, Microsoft.
                // If you can't tell by the error string,
                // this actually checks if the current process was run with "Run as Administrator"
                // ...but this reads like it checks if the current is in/has the Windows admin role? lol
                if (new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator))
                {
                    Logger.Error?.PrintMsg(LogClass.Application, "Ryujinx is not intended to be run as administrator. Exiting...");
                    _ = Win32NativeInterop.MessageBoxA(nint.Zero, "Ryujinx is not intended to be run as administrator.",
                        $"Ryujinx {Version}", MbIconwarning);
                    return 0;
                }
            }
            else // Unix
            {
                // sudo check
                [DllImport("libc")]
                static extern uint geteuid();
                bool root = geteuid().Equals(0);

                if (OperatingSystem.IsMacOS())
                {
                    if (root)
                    {
                        Logger.Error?.PrintMsg(LogClass.Application, "Ryujinx is not intended to be run as administrator. Exiting...");
                        macOSNativeInterop.SimpleMessageBox($"Ryujinx {Version}",
                            "Ryujinx is not intended to be run as administrator.", "Ok");
                        return 0;
                    }
                    
                    string downloadFiles = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
                    
                    if (Environment.CurrentDirectory.StartsWithIgnoreCase(downloadFiles))
                    {
                        Logger.Error?.PrintMsg(LogClass.Application, "Ryujinx is not intended to be run from the Downloads folder. Exiting...");
                        macOSNativeInterop.SimpleMessageBox($"Ryujinx {Version}",
                            "Ryujinx is not intended to be run from the Downloads folder.", "Ok");
                        return 0;
                    }
                    
                    string icloudFiles = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library/Mobile Documents/com~apple~CloudDocs");
                    
                    if (Environment.CurrentDirectory.StartsWithIgnoreCase(icloudFiles))
                    {
                        Logger.Error?.PrintMsg(LogClass.Application, "Ryujinx is not intended to be run from the iCloud folder. Exiting...");
                        macOSNativeInterop.SimpleMessageBox($"Ryujinx {Version}",
                            "Ryujinx is not intended to be run from the iCloud folder.", "Ok");
                        return 0;
                    }
                }

                if (OperatingSystem.IsLinux())
                {
                    if (root)
                    {
                        Logger.Error?.PrintMsg(LogClass.Application, "Ryujinx is not intended to be run as administrator. Exiting...");
                        LinuxSDLInterop.SimpleMessageBox($"Ryujinx {Version}", "Ryujinx is not intended to be run as administrator.");
                        return 0;
                    }
                    
                    string container = Environment.GetEnvironmentVariable("container");
                    
                    if (container is not null && container.EqualsIgnoreCase("flatpak"))
                    {
                        Logger.Info?.PrintMsg(LogClass.Application, "=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=");
                        Logger.Warning?.PrintMsg(LogClass.Application, "This is very likely an unofficial build, Ryujinx does NOT have a flatpak!");
                        Logger.Info?.PrintMsg(LogClass.Application, "Please visit https://ryujinx.app/ for our official builds.");
                        Logger.Info?.PrintMsg(LogClass.Application, "=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=");
                    }
                }
            }

            bool noGuiArg = ConsumeCommandLineArgument(ref args, "--no-gui") || ConsumeCommandLineArgument(ref args, "nogui");
            bool coreDumpArg = ConsumeCommandLineArgument(ref args, "--core-dumps");

            CoreDumpArg = coreDumpArg;

            // TODO: Ryujinx causes core dumps on Linux when it exits "uncleanly", eg. through an unhandled exception.
            //       This is undesirable and causes very odd behavior during development (the process stops responding,
            //       the .NET debugger freezes or suddenly detaches, /tmp/ gets filled etc.), unless explicitly requested by the user.
            //       This needs to be investigated, but calling prctl() is better than modifying system-wide settings or leaving this be.
            if (!coreDumpArg)
            {
                OsUtils.SetCoreDumpable(false);
            }

            PreviewerDetached = true;

            if (noGuiArg)
            {
                HeadlessRyujinx.Entrypoint(args);
                return 0;
            }

            Initialize(args);

            LoggerAdapter.Register();

            IconProvider.Current
                .Register<FontAwesomeIconProvider>()
                .Register<MaterialDesignIconProvider>();

            return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }

        public static AppBuilder BuildAvaloniaApp() =>
            AppBuilder.Configure<RyujinxApp>()
                .UsePlatformDetect()
                
                // Vulkan UI rendering performs better, but its unpolished, and as such it lacks effective transparency.
                // https://github.com/AvaloniaUI/Avalonia/issues/19378
                // https://github.com/AvaloniaUI/Avalonia/issues/9610
                // X11RenderingMode.Glx && X11RenderingMode.Egl, Win32RenderingMode.Vulkan have these issues.
                
                .With(new X11PlatformOptions
                {
                    EnableMultiTouch = true,
                    EnableIme = true,
                    EnableInputFocusProxy = Environment.GetEnvironmentVariable("XDG_CURRENT_DESKTOP") == "gamescope",
                    RenderingMode = UseHardwareAcceleration
                        ? [X11RenderingMode.Glx, X11RenderingMode.Software]
                        : [X11RenderingMode.Software]
                })
                .With(new Win32PlatformOptions
                {
                    WinUICompositionBackdropCornerRadius = 8.0f,
                    RenderingMode = UseHardwareAcceleration
                        ? [Win32RenderingMode.AngleEgl, Win32RenderingMode.Software]
                        : [Win32RenderingMode.Software]
                });

        private static bool ConsumeCommandLineArgument(ref string[] args, string targetArgument)
        {
            List<string> argList = [.. args];
            bool found = argList.Remove(targetArgument);
            args = argList.ToArray();
            return found;
        }

        private static void Initialize(string[] args)
        {
            // Ensure Discord presence timestamp begins at the absolute start of when Ryujinx is launched
            DiscordIntegrationModule.EmulatorStartedAt = Timestamps.Now;

            // Parse arguments
            CommandLineState.ParseArguments(args);

            if (OperatingSystem.IsMacOS())
            {
                MVKInitialization.InitializeResolver();
            }

            // Delete backup files after updating.
            Task.Run(Updater.CleanupUpdate);

            // [Nextendo beta] WinExe means there may be no console window — setting the title
            // would throw. Guard it so a normal (windowed) launch is unaffected.
            try
            {
                Console.Title = $"{RyujinxApp.FullAppName} Console {Version}";
            }
            catch
            {
                // No console attached — nothing to title.
            }

            // Hook unhandled exception and process exit events.
            AppDomain.CurrentDomain.UnhandledException += (sender, e)
                => ProcessUnhandledException(sender, e.ExceptionObject as Exception, e.IsTerminating);
            TaskScheduler.UnobservedTaskException += (sender, e)
                => ProcessUnhandledException(sender, e.Exception, false);
            AppDomain.CurrentDomain.ProcessExit += (_, _) => Exit();

            // Setup base data directory.
            AppDataManager.Initialize(CommandLineState.BaseDirPathArg);

            // Initialize the configuration.
            ConfigurationState.Initialize();

            // Initialize the logger system.
            LoggerModule.Initialize();

            // Initialize Discord integration.
            DiscordIntegrationModule.Initialize();

            // [Nextendo] Remontée de présence du JEU COURANT (n'importe quel jeu) -> les amis
            // voient « joue à <jeu> » sur la Switch. S'abonne au changement d'application + TTL.
            Ryujinx.Common.Configuration.NextendoFriends.InitializePresence();

            // [Nextendo] Enrichit cette présence avec le play report du jeu (« Single Player »…),
            // même source que Discord mais indépendante de son activation.
            Ryujinx.Ava.Systems.NextendoRichPresence.Initialize();

            // Initialize SDL3 driver
            SDL3Driver.MainThreadDispatcher = action => Dispatcher.UIThread.InvokeAsync(action, DispatcherPriority.Input);

            ReloadConfig();

            WindowScaleFactor = ForceDpiAware.GetWindowScaleFactor();

            // Logging system information.
            PrintSystemInfo();

            // Enable OGL multithreading on the driver, and some other flags.
            DriverUtilities.InitDriverConfig(ConfigurationState.Instance.Graphics.BackendThreading == BackendThreading.Off);

            // Check if keys exists.
            if (!File.Exists(Path.Combine(AppDataManager.KeysDirPath, "prod.keys")))
            {
                if (!(AppDataManager.Mode == AppDataManager.LaunchMode.UserProfile && File.Exists(Path.Combine(AppDataManager.KeysDirPathUser, "prod.keys"))))
                {
                    MainWindow.ShowKeyErrorOnLoad = true;
                }
            }

            if (CommandLineState.LaunchPathArg != null)
            {
                MainWindow.DeferLoadApplication(CommandLineState.LaunchPathArg, CommandLineState.LaunchApplicationId, CommandLineState.StartFullscreenArg);
            }
        }

        public static string GetDirGameUserConfig(string gameId, bool changeFolderForGame = false)
        {
            if (string.IsNullOrEmpty(gameId))
            {
                return "";
            }

            string gameDir = Path.Combine(AppDataManager.GamesDirPath, gameId, ReleaseInformation.ConfigName);

            if (changeFolderForGame)
            {
                ConfigurationPath = gameDir;
                UseExtraConfig = true;
            }

            return gameDir;
        }

        public static void ReloadConfig(bool isRunGameWithCustomConfig = false)
        {

            string localConfigurationPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, ReleaseInformation.ConfigName);
            string appDataConfigurationPath = Path.Combine(AppDataManager.BaseDirPath, ReleaseInformation.ConfigName);


            if (!isRunGameWithCustomConfig) // To return settings from the game folder if the user configuration exists
            {
                // Now load the configuration as the other subsystems are now registered
                if (File.Exists(localConfigurationPath))
                {
                    ConfigurationPath = localConfigurationPath;
                }
                else if (File.Exists(appDataConfigurationPath))
                {
                    ConfigurationPath = appDataConfigurationPath;
                }
            }

            if (ConfigurationPath == null)
            {
                // No configuration, we load the default values and save it to disk
                ConfigurationPath = appDataConfigurationPath;
                Logger.Notice.Print(LogClass.Application, $"No configuration file found. Saving default configuration to: {ConfigurationPath}");

                ConfigurationState.Instance.LoadDefault();
                ConfigurationState.Instance.ToFileFormat().SaveConfig(ConfigurationPath);
            }
            else
            {
                Logger.Notice.Print(LogClass.Application, $"Loading configuration from: {ConfigurationPath}");

                if (ConfigurationFileFormat.TryLoad(ConfigurationPath, out ConfigurationFileFormat configurationFileFormat))
                {
                    ConfigurationState.Instance.Load(configurationFileFormat, ConfigurationPath);
                }
                else
                {
                    Logger.Warning?.PrintMsg(LogClass.Application, $"Failed to load config! Loading the default config instead.\nFailed config location: {ConfigurationPath}");

                    ConfigurationFileFormat.RenameInvalidConfigFile(ConfigurationPath);

                    ConfigurationState.Instance.LoadDefault();
                }
            }

            // When you first load the program, copy to remember the path for the global configuration
            GlobalConfigurationPath ??= ConfigurationPath;

            UseHardwareAcceleration = ConfigurationState.Instance.EnableHardwareAcceleration;

            // Check if graphics backend was overridden
            if (CommandLineState.OverrideGraphicsBackend is not null)
                ConfigurationState.Instance.Graphics.GraphicsBackend.Value = CommandLineState.OverrideGraphicsBackend.ToLower() switch
                {
                    "opengl" => GraphicsBackend.OpenGl,
                    "vulkan" => GraphicsBackend.Vulkan,
                    _ => ConfigurationState.Instance.Graphics.GraphicsBackend
                };

            // Check if backend threading was overridden
            if (CommandLineState.OverrideBackendThreading is not null)
                ConfigurationState.Instance.Graphics.BackendThreading.Value = CommandLineState.OverrideBackendThreading.ToLower() switch
                {
                    "auto" => BackendThreading.Auto,
                    "off" => BackendThreading.Off,
                    "on" => BackendThreading.On,
                    _ => ConfigurationState.Instance.Graphics.BackendThreading
                };

            if (CommandLineState.OverrideBackendThreadingAfterReboot is not null)
            {
                BackendThreadingArg = CommandLineState.OverrideBackendThreadingAfterReboot;
            }

            // Check if docked mode was overriden.
            if (CommandLineState.OverrideDockedMode.HasValue)
                ConfigurationState.Instance.System.EnableDockedMode.Value = CommandLineState.OverrideDockedMode.Value;

            // Check if HideCursor was overridden.
            if (CommandLineState.OverrideHideCursor is not null)
                ConfigurationState.Instance.HideCursor.Value = CommandLineState.OverrideHideCursor.ToLower() switch
                {
                    "never" => HideCursorMode.Never,
                    "onidle" => HideCursorMode.OnIdle,
                    "always" => HideCursorMode.Always,
                    _ => ConfigurationState.Instance.HideCursor
                };

            // Check if memoryManagerMode was overridden.
            if (CommandLineState.OverrideMemoryManagerMode is not null)
                if (Enum.TryParse(CommandLineState.OverrideMemoryManagerMode, true, out MemoryManagerMode result))
                {
                    ConfigurationState.Instance.System.MemoryManagerMode.Value = result;
                }

            // Check if PPTC was overridden.
            if (CommandLineState.OverridePPTC is not null)
                if (Enum.TryParse(CommandLineState.OverridePPTC, true, out bool result))
                {
                    ConfigurationState.Instance.System.EnablePtc.Value = result;
                }

            // Check if region was overridden.
            if (CommandLineState.OverrideSystemRegion is not null)
                if (Enum.TryParse(CommandLineState.OverrideSystemRegion, true, out Region result))
                {
                    ConfigurationState.Instance.System.Region.Value = result;
                }

            //Check if language was overridden.
            if (CommandLineState.OverrideSystemLanguage is not null)
                if (Enum.TryParse(CommandLineState.OverrideSystemLanguage, true, out Language result))
                {
                    ConfigurationState.Instance.System.Language.Value = result;
                }

            // Check if hardware-acceleration was overridden.
            if (CommandLineState.OverrideHardwareAcceleration != null)
                UseHardwareAcceleration = CommandLineState.OverrideHardwareAcceleration.Value;
        }

        internal static void PrintSystemInfo()
        {
            // Print the ryubing logo + joke splash
            SplashTextHelper.PrintSplash(); 
            
            Logger.Notice.Print(LogClass.Application, $"{RyujinxApp.FullAppName} Version: {Version}");
            Logger.Notice.Print(LogClass.Application, $".NET Runtime: {RuntimeInformation.FrameworkDescription}");
            SystemInfo.Gather().Print();

            Logger.Notice.Print(LogClass.Application, $"Logs Enabled: {Logger.GetEnabledLevels()
                    .FormatCollection(
                        x => x.ToString(),
                        separator: ", ",
                        emptyCollectionFallback: "<None>")}");

            Logger.Notice.Print(LogClass.Application,
                AppDataManager.Mode == AppDataManager.LaunchMode.Custom
                    ? $"Launch Mode: Custom Path {AppDataManager.BaseDirPath}"
                    : $"Launch Mode: {AppDataManager.Mode}");
        }

        internal static void ProcessUnhandledException(object sender, Exception initialException, bool isTerminating)
        {
            Logger.Log log = Logger.Error ?? Logger.Notice;

            List<Exception> exceptions = [];

            if (initialException is AggregateException ae)
            {
                exceptions.AddRange(ae.InnerExceptions);
            }
            else
            {
                exceptions.Add(initialException);
            }

            foreach (Exception e in exceptions)
            {
                string message = $"Unhandled exception caught: {e}";
                // ReSharper disable once ConstantConditionalAccessQualifier
                if (sender?.GetType()?.AsPrettyString() is { } senderName)
                    log.Print(LogClass.Application, message, senderName);
                else
                    log.PrintMsg(LogClass.Application, message);
            }


            if (isTerminating)
            {
                Logger.Flush();
                Exit();
            }
        }

        internal static void Exit()
        {
            DiscordIntegrationModule.Exit();

            Logger.Shutdown();
        }
    }
}
