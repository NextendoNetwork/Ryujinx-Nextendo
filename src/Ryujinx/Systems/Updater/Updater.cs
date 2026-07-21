using Avalonia.Threading;
using FluentAvalonia.UI.Controls;
using Gommon;
using Ryujinx.Ava.Common.Locale;
using Ryujinx.Ava.UI.Helpers;
using Ryujinx.Ava.Utilities;
using Ryujinx.Common;
using Ryujinx.Common.Helper;
using Ryujinx.Common.Logging;
using SharpCompress.Archives;
using SharpCompress.Compressors.Xz;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Formats.Tar;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Threading;
using System.Threading.Tasks;

namespace Ryujinx.Ava.Systems
{
    internal static partial class Updater
    {
        private static readonly string _homeDir = AppDomain.CurrentDomain.BaseDirectory;
        private static readonly string _updateDir = Path.Combine(Path.GetTempPath(), "Ryujinx", "update");
        private static readonly string _updatePublishDir = Path.Combine(_updateDir, "publish");
        
        private static int _connectionCount = 1;
        private static long _buildSize;
        private static bool _updateSuccessful;
        private static bool _running;

        private static readonly string[] _windowsDependencyDirs = [];
        
        public static async Task BeginUpdateAsync(bool showVersionUpToDate = false)
        {
            if (_running)
            {
                return;
            }

            _running = true;

            Optional<(Version, Version)> versionTuple = await CheckVersionAsync(showVersionUpToDate);

            if (_running is false || !versionTuple.HasValue)
                return;

            (Version currentVersion, Version newVersion) = versionTuple.Value;

            if (newVersion <= currentVersion)
            {
                if (showVersionUpToDate)
                {
                    await ContentDialogHelper.CreateUpdaterUpToDateInfoDialog(
                        LocaleManager.Instance[LocaleKeys.DialogUpdaterAlreadyOnLatestVersionMessage],
                        string.Empty, 
                        changelogUrl: _versionResponse.ReleaseUrlFormat.Format(currentVersion));
                }

                Logger.Info?.Print(LogClass.Application, "Up to date.");

                _running = false;

                return;
            }

            await Dispatcher.UIThread.InvokeAsync(async () =>
            {
                string newVersionString = ReleaseInformation.IsCanaryBuild
                    ? $"Canary {currentVersion} → Canary {newVersion}"
                    : $"{currentVersion} → {newVersion}";

                Logger.Info?.Print(LogClass.Application, $"Version found: {newVersionString.Replace("→", "->")}");

                // Show a message asking the user if they want to update
                UserResult shouldUpdate = await ContentDialogHelper.CreateUpdaterChoiceDialog(
                    LocaleManager.Instance[LocaleKeys.RyujinxUpdater],
                    LocaleManager.Instance[LocaleKeys.RyujinxUpdaterMessage],
                    newVersionString,
                    ReleaseInformation.GetChangelogUrl(currentVersion, newVersion));

                switch (shouldUpdate)
                {
                    case UserResult.Yes:
                        await UpdateRyujinx(_versionResponse.ArtifactUrl);
                        break;
                    default:
                        _running = false;
                        break;
                }
            });
        }

        private static HttpClient ConstructHttpClient()
        {
            HttpClient result = new();

            // Required by GitHub to interact with APIs.
            result.DefaultRequestHeaders.Add("User-Agent", $"Ryujinx-Updater/{ReleaseInformation.Version}");

            return result;
        }

        private static async Task UpdateRyujinx(string downloadUrl)
        {
            _updateSuccessful = false;

            // Empty update dir, although it shouldn't ever have anything inside it
            if (Directory.Exists(_updateDir))
            {
                Directory.Delete(_updateDir, true);
            }

            Directory.CreateDirectory(_updateDir);

            // [Nextendo] These rewrites are specific to the Ryujinx update server, which publishes
            // both formats and serves the legacy url by default. Nextendo releases come straight
            // from GitHub and ship exactly what the release page lists, so rewriting the extension
            // there points at an asset that does not exist (404) — hence the guard.
            if (!NextendoIsOwnReleaseUrl(downloadUrl))
            {
                // If we get a .zip url switch it to the preferred .7z file instead
                // The update server still returns the .zip url by default for legacy support
                downloadUrl = downloadUrl.Replace(".zip", ".7z");

                // If we get a .tar.gz url switch it to the preferred .tar.xz file instead
                // The update server still returns the .tar.gz url by default for legacy support
                downloadUrl = downloadUrl.Replace(".tar.gz", ".tar.xz");
            }

            // [Nextendo] The download is always saved as update.bin, so the extension is lost by
            // the time InstallUpdate runs. Remember the real format now, from the url we actually
            // fetch, instead of guessing it back from the OS.
            _archiveKind = NextendoArchiveKindFromUrl(downloadUrl);

            string updateFile = Path.Combine(_updateDir, "update.bin");

            TaskDialog taskDialog = new()
            {
                Header = LocaleManager.Instance[LocaleKeys.RyujinxUpdater],
                SubHeader = LocaleManager.Instance[LocaleKeys.UpdaterDownloading],
                IconSource = new SymbolIconSource { Symbol = Symbol.Download },
                ShowProgressBar = true,
                XamlRoot = RyujinxApp.MainWindow,
            };
            
            // Fetch build size information to learn chunk sizes.
            using HttpClient buildSizeClient = ConstructHttpClient();
            try
            {
                buildSizeClient.DefaultRequestHeaders.Add("Range", "bytes=0-0");
                
                // Forgejo instance is located in Ukraine. Connection times will vary across the world.
                buildSizeClient.Timeout = TimeSpan.FromSeconds(10);

                // [Nextendo] Fetch the size of the url we are ACTUALLY downloading (downloadUrl),
                // not _versionResponse.ArtifactUrl — the latter is null on the Nextendo update path
                // (we bypass the Ryujinx update server), which threw here, left _buildSize = -1, and
                // forced the single-threaded worker. That worker calls InstallUpdate off the UI
                // thread and crashes on the first TaskDialog property set ("Call from invalid
                // thread"), so the update never finished. GitHub asset urls support Range requests,
                // so this determines the size and lets the UI-safe multi-threaded path run.
                HttpResponseMessage message = await buildSizeClient.GetAsync(new Uri(downloadUrl), HttpCompletionOption.ResponseHeadersRead);

                _buildSize = message.Content.Headers.ContentRange.Length.Value;
            }
            catch (Exception ex)
            {
                Logger.Warning?.Print(LogClass.Application, ex.Message);
                Logger.Warning?.Print(LogClass.Application, "Couldn't determine build size for update, using single-threaded updater");

                _buildSize = -1;
            }

            taskDialog.Opened += (s, e) =>
            {
                if (_buildSize >= 0)
                {
                    DoUpdateWithMultipleThreads(taskDialog, downloadUrl, updateFile);
                }
                else
                {
                    DoUpdateWithSingleThread(taskDialog, downloadUrl, updateFile);
                }
            };

            await taskDialog.ShowAsync(true);

            if (_updateSuccessful)
            {
                bool shouldRestart = true;

                if (!OperatingSystem.IsMacOS())
                {
                    shouldRestart = await ContentDialogHelper.CreateChoiceDialog(LocaleManager.Instance[LocaleKeys.RyujinxUpdater],
                        LocaleManager.Instance[LocaleKeys.DialogUpdaterCompleteMessage],
                        LocaleManager.Instance[LocaleKeys.DialogUpdaterRestartMessage]);
                }

                if (shouldRestart)
                {
                    List<string> arguments = CommandLineState.Arguments.ToList();
                    string executableDirectory = AppDomain.CurrentDomain.BaseDirectory;

                    // On macOS we perform the update at relaunch.
                    if (OperatingSystem.IsMacOS())
                    {
                        string baseBundlePath = Path.GetFullPath(Path.Combine(executableDirectory, "..", ".."));
                        string newBundlePath = Path.Combine(_updateDir, "Ryujinx.app");
                        string updaterScriptPath = Path.Combine(newBundlePath, "Contents", "Resources", "updater.sh");
                        string currentPid = Environment.ProcessId.ToString();

                        arguments.InsertRange(0, new List<string> { updaterScriptPath, baseBundlePath, newBundlePath, currentPid });
                        Process.Start("/bin/bash", arguments);
                    }
                    else
                    {
                        // Find the process name.
                        string ryuName = Path.GetFileName(Environment.ProcessPath) ?? string.Empty;

                        // Some operating systems can see the renamed executable, so strip off the .ryuold if found.
                        if (ryuName.EndsWith(".ryuold"))
                        {
                            ryuName = ryuName[..^7];
                        }

                        // Fallback if the executable could not be found.
                        if (ryuName.Length == 0 || !Path.Exists(Path.Combine(executableDirectory, ryuName)))
                        {
                            ryuName = OperatingSystem.IsWindows() ? "Ryujinx.exe" : "Ryujinx";
                        }

                        ProcessStartInfo processStart = new(ryuName)
                        {
                            UseShellExecute = true,
                            WorkingDirectory = executableDirectory,
                        };

                        foreach (string argument in CommandLineState.Arguments)
                        {
                            processStart.ArgumentList.Add(argument);
                        }

                        Process.Start(processStart);
                    }

                    Environment.Exit(0);
                }
            }
        }

        private static void DoUpdateWithMultipleThreads(TaskDialog taskDialog, string downloadUrl, string updateFile)
        {
            // Multi-Threaded Updater
            long chunkSize = _buildSize / _connectionCount;
            long remainderChunk = _buildSize % _connectionCount;

            int completedRequests = 0;
            int totalProgressPercentage = 0;
            int[] progressPercentage = new int[_connectionCount];

            List<byte[]> list = new(_connectionCount);
            List<WebClient> webClients = new(_connectionCount);

            for (int i = 0; i < _connectionCount; i++)
            {
                list.Add([]);
            }

            for (int i = 0; i < _connectionCount; i++)
            {
#pragma warning disable SYSLIB0014
                // TODO: WebClient is obsolete and need to be replaced with a more complex logic using HttpClient.
                using WebClient client = new();
#pragma warning restore SYSLIB0014

                webClients.Add(client);

                if (i == _connectionCount - 1)
                {
                    client.Headers.Add("Range", $"bytes={chunkSize * i}-{(chunkSize * (i + 1) - 1) + remainderChunk}");
                }
                else
                {
                    client.Headers.Add("Range", $"bytes={chunkSize * i}-{chunkSize * (i + 1) - 1}");
                }

                client.DownloadProgressChanged += (_, args) =>
                {
                    int index = (int)args.UserState;

                    Interlocked.Add(ref totalProgressPercentage, -1 * progressPercentage[index]);
                    Interlocked.Exchange(ref progressPercentage[index], args.ProgressPercentage);
                    Interlocked.Add(ref totalProgressPercentage, args.ProgressPercentage);

                    taskDialog.SetProgressBarState(totalProgressPercentage / _connectionCount, TaskDialogProgressState.Normal);
                };

                client.DownloadDataCompleted += (_, args) =>
                {
                    int index = (int)args.UserState;

                    if (args.Cancelled)
                    {
                        webClients[index].Dispose();

                        taskDialog.Hide();

                        return;
                    }

                    list[index] = args.Result;
                    Interlocked.Increment(ref completedRequests);

                    if (Equals(completedRequests, _connectionCount))
                    {
                        byte[] mergedFileBytes = new byte[_buildSize];
                        for (int connectionIndex = 0, destinationOffset = 0; connectionIndex < _connectionCount; connectionIndex++)
                        {
                            Array.Copy(list[connectionIndex], 0, mergedFileBytes, destinationOffset, list[connectionIndex].Length);
                            destinationOffset += list[connectionIndex].Length;
                        }

                        File.WriteAllBytes(updateFile, mergedFileBytes);

                        // On macOS, ensure that we remove the quarantine bit to prevent Gatekeeper from blocking execution.
                        if (OperatingSystem.IsMacOS())
                        {
                            using Process xattrProcess = Process.Start("xattr",
                                ["-d", "com.apple.quarantine", updateFile]);

                            xattrProcess.WaitForExit();
                        }

                        try
                        {
                            InstallUpdate(taskDialog, updateFile);
                        }
                        catch (Exception e)
                        {
                            Logger.Warning?.Print(LogClass.Application, e.Message);
                            Logger.Warning?.Print(LogClass.Application, "Multi-Threaded update failed, falling back to single-threaded updater.");

                            DoUpdateWithSingleThread(taskDialog, downloadUrl, updateFile);
                        }
                    }
                };

                try
                {
                    client.DownloadDataAsync(new Uri(downloadUrl), i);
                }
                catch (WebException ex)
                {
                    Logger.Warning?.Print(LogClass.Application, ex.Message);
                    Logger.Warning?.Print(LogClass.Application, "Multi-Threaded update failed, falling back to single-threaded updater.");

                    foreach (WebClient webClient in webClients)
                    {
                        webClient.CancelAsync();
                    }

                    DoUpdateWithSingleThread(taskDialog, downloadUrl, updateFile);

                    return;
                }
            }
        }

        private static void DoUpdateWithSingleThreadWorker(TaskDialog taskDialog, string downloadUrl, string updateFile)
        {
            using HttpClient client = new();
            // We do not want to timeout while downloading
            client.Timeout = TimeSpan.FromDays(1);

            using HttpResponseMessage response = client.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead).Result;
            using Stream remoteFileStream = response.Content.ReadAsStreamAsync().Result;
            using FileStream updateFileStream = File.Open(updateFile, FileMode.Create);

            long totalBytes = response.Content.Headers.ContentLength.Value;
            long bytesWritten = 0;

            byte[] buffer = new byte[32 * 1024];

            while (true)
            {
                int readSize = remoteFileStream.Read(buffer);

                if (readSize == 0)
                {
                    break;
                }

                bytesWritten += readSize;

                taskDialog.SetProgressBarState(GetPercentage(bytesWritten, totalBytes), TaskDialogProgressState.Normal);
                RyujinxApp.SetTaskbarProgressValue(bytesWritten, totalBytes);

                updateFileStream.Write(buffer, 0, readSize);
            }

            // [Nextendo] This runs on a raw worker Thread (no SynchronizationContext), but
            // InstallUpdate sets TaskDialog properties directly — a UI-thread-only operation.
            // The multi-threaded path calls InstallUpdate from a WebClient completion handler,
            // which fires on the UI thread; this fallback must marshal it the same way or it
            // crashes with "Call from invalid thread". Wait so the process is not restarted before
            // the files are in place.
            Dispatcher.UIThread.InvokeAsync(() => InstallUpdate(taskDialog, updateFile)).Wait();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static double GetPercentage(double value, double max)
        {
            return max == 0 ? 0 : value / max * 100;
        }

        private static void DoUpdateWithSingleThread(TaskDialog taskDialog, string downloadUrl, string updateFile)
        {
            Thread worker = new(() => DoUpdateWithSingleThreadWorker(taskDialog, downloadUrl, updateFile))
            {
                Name = "Updater.SingleThreadWorker",
            };

            worker.Start();
        }

        [SupportedOSPlatform("linux")]
        [SupportedOSPlatform("macos")]
        private static void ExtractTarGzipFile(string archivePath, string outputDirectoryPath)
        {
            using FileStream inStream = File.OpenRead(archivePath);
            using GZipStream gzipStream = new(inStream, CompressionMode.Decompress);
            
            TarFile.ExtractToDirectory(gzipStream, outputDirectoryPath, true);
        }
        
        [SupportedOSPlatform("linux")]
        [SupportedOSPlatform("macos")]
        private static void ExtractTarXzipFile(string archivePath, string outputDirectoryPath)
        {
            using FileStream inStream = File.OpenRead(archivePath);
            using XZStream gzipStream = new(inStream);
            
            TarFile.ExtractToDirectory(gzipStream, outputDirectoryPath, true);
        }

        private static void ExtractZipFile(string archivePath, string outputDirectoryPath)
        {
            ZipFile.ExtractToDirectory(archivePath, outputDirectoryPath);
        }
        
        private static void Extract7ZipFile(string archivePath, string outputDirectoryPath)
        {
            IArchive archive = ArchiveFactory.OpenArchive(archivePath);
            archive.WriteToDirectory(outputDirectoryPath);
        }

        private static void InstallUpdate(TaskDialog taskDialog, string updateFile)
        {
            // Extract Update
            taskDialog.SubHeader = LocaleManager.Instance[LocaleKeys.UpdaterExtracting];
            taskDialog.SetProgressBarState(0, TaskDialogProgressState.Normal);

            // [Nextendo] Driven by what was actually downloaded rather than by the OS: a Nextendo
            // release ships .zip on Windows and .tar.gz on Linux, and picking the extractor from
            // the platform alone would hand a zip to the 7z reader.
            switch (_archiveKind)
            {
                case ArchiveKind.Zip:
                    ExtractZipFile(updateFile, _updateDir);
                    break;
                case ArchiveKind.SevenZip:
                    Extract7ZipFile(updateFile, _updateDir);
                    break;
                case ArchiveKind.TarGz:
                    ExtractTarGzipFile(updateFile, _updateDir);
                    break;
                case ArchiveKind.TarXz when OperatingSystem.IsLinux() || OperatingSystem.IsMacOS():
                    ExtractTarXzipFile(updateFile, _updateDir);
                    break;
                default:
                    throw new NotSupportedException($"Unsupported update archive: {_archiveKind}");
            }
            
            // The new decompression implementations don't have a way to show progress
            // so the progressbar is just set to 100% after the decompression is done
            taskDialog.SetProgressBarState(100, TaskDialogProgressState.Normal);

            // Delete downloaded zip
            File.Delete(updateFile);

            List<string> allFiles = EnumerateFilesToDelete().ToList();

            taskDialog.SubHeader = LocaleManager.Instance[LocaleKeys.UpdaterRenaming];
            taskDialog.SetProgressBarState(0, TaskDialogProgressState.Normal);

            // NOTE: On macOS, replacement is delayed to the restart phase.
            if (!OperatingSystem.IsMacOS())
            {
                // Replace old files
                double count = 0;
                foreach (string file in allFiles)
                {
                    count++;
                    try
                    {
                        File.Move(file, file + ".ryuold");

                        Dispatcher.UIThread.InvokeAsync(() =>
                        {
                            taskDialog.SetProgressBarState(GetPercentage(count, allFiles.Count), TaskDialogProgressState.Normal);
                        });
                    }
                    catch
                    {
                        Logger.Warning?.Print(LogClass.Application, LocaleManager.Instance.UpdateAndGetDynamicValue(LocaleKeys.UpdaterRenameFailed, file));
                    }
                }

                Dispatcher.UIThread.InvokeAsync(() =>
                {
                    taskDialog.SubHeader = LocaleManager.Instance[LocaleKeys.UpdaterAddingFiles];
                    taskDialog.SetProgressBarState(0, TaskDialogProgressState.Normal);
                });

                MoveAllFilesOver(_updatePublishDir, _homeDir, taskDialog);

                Directory.Delete(_updateDir, true);
            }

            _updateSuccessful = true;

            taskDialog.Hide();
        }

        public static bool CanUpdate(bool showWarnings = false)
        {
#if !DISABLE_UPDATER
            if (!NetworkInterface.GetIsNetworkAvailable())
            {
                if (showWarnings)
                {
                    Dispatcher.UIThread.InvokeAsync(() =>
                        ContentDialogHelper.CreateWarningDialog(
                            LocaleManager.Instance[LocaleKeys.DialogUpdaterNoInternetMessage],
                            LocaleManager.Instance[LocaleKeys.DialogUpdaterNoInternetSubMessage])
                    );
                }

                return false;
            }

            if (Program.Version.Contains("dirty") || !ReleaseInformation.IsValid)
            {
                if (showWarnings)
                {
                    Dispatcher.UIThread.InvokeAsync(() =>
                        ContentDialogHelper.CreateWarningDialog(
                            LocaleManager.Instance[LocaleKeys.DialogUpdaterDirtyBuildMessage],
                            LocaleManager.Instance[LocaleKeys.DialogUpdaterDirtyBuildSubMessage])
                    );
                }

                return false;
            }

            return true;
#else
            if (showWarnings)
            {
                Dispatcher.UIThread.InvokeAsync(() =>
                    ContentDialogHelper.CreateWarningDialog(
                        LocaleManager.Instance[LocaleKeys.UpdaterDisabledWarningTitle],
                        LocaleManager.Instance[LocaleKeys.DialogUpdaterDirtyBuildSubMessage])
                    );
            }

            return false;
#endif
        }

        // NOTE: This method should always reflect the latest build layout.
        private static IEnumerable<string> EnumerateFilesToDelete()
        {
            IEnumerable<string> files = Directory.EnumerateFiles(_homeDir); // All files directly in base dir.

            // Determine and exclude user files only when the updater is running, not when cleaning old files
            if (_running && !OperatingSystem.IsMacOS())
            {
                // Compare the loose files in base directory against the loose files from the incoming update, and store foreign ones in a user list.
                IEnumerable<string> oldFiles = Directory.EnumerateFiles(_homeDir, "*", SearchOption.TopDirectoryOnly).Select(Path.GetFileName);
                IEnumerable<string> newFiles = Directory.EnumerateFiles(_updatePublishDir, "*", SearchOption.TopDirectoryOnly).Select(Path.GetFileName);
                IEnumerable<string> userFiles = oldFiles.Except(newFiles).Select(filename => Path.Combine(_homeDir, filename));

                // Remove user files from the paths in files.
                files = files.Except(userFiles);
            }

            if (OperatingSystem.IsWindows())
            {
                foreach (string dir in _windowsDependencyDirs)
                {
                    string dirPath = Path.Combine(_homeDir, dir);
                    if (Directory.Exists(dirPath))
                    {
                        files = files.Concat(Directory.EnumerateFiles(dirPath, "*", SearchOption.AllDirectories));
                    }
                }
            }

            return files.Where(f => !new FileInfo(f).Attributes.HasFlag(FileAttributes.Hidden | FileAttributes.System));
        }

        private static void MoveAllFilesOver(string root, string dest, TaskDialog taskDialog)
        {
            int total = Directory.GetFiles(root, "*", SearchOption.AllDirectories).Length;
            foreach (string directory in Directory.GetDirectories(root))
            {
                string dirName = Path.GetFileName(directory);

                // [Nextendo] `portable` is the player's data — keys, firmware, account, saves and
                // Config.json. Our download packs ship a populated one so a first install works out
                // of the box, and this method moves files with overwrite:true, so updating from
                // such a pack would silently reset settings and game paths on every update. The
                // update only ever owns the emulator's own files; portable belongs to the player.
                if (dirName.Equals(NextendoPortableDirName, StringComparison.OrdinalIgnoreCase))
                {
                    // Still create it when missing: its presence is what selects portable mode.
                    Directory.CreateDirectory(Path.Combine(dest, dirName));

                    Logger.Info?.Print(LogClass.Application,
                        "[Nextendo] Preserving the existing 'portable' folder; the update will not write into it.");

                    continue;
                }

                if (!Directory.Exists(Path.Combine(dest, dirName)))
                {
                    Directory.CreateDirectory(Path.Combine(dest, dirName));
                }

                MoveAllFilesOver(directory, Path.Combine(dest, dirName), taskDialog);
            }

            double count = 0;
            foreach (string file in Directory.GetFiles(root))
            {
                count++;

                File.Move(file, Path.Combine(dest, Path.GetFileName(file)), true);

                Dispatcher.UIThread.InvokeAsync(() =>
                {
                    taskDialog.SetProgressBarState(GetPercentage(count, total), TaskDialogProgressState.Normal);
                });
            }
        }

        public static void CleanupUpdate() =>
            Directory.GetFiles(_homeDir, "*.ryuold", SearchOption.AllDirectories)
                .ForEach(File.Delete);

        private static string BuildPlatformExtension()
        {
            if (RunningPlatform.IsMacOS)
                return "macos_universal.app.tar.gz";

#pragma warning disable CS8509 // It is exhaustive for any values this can contain.
            string osPrefix = RunningPlatform.CurrentOS switch
            {
                OperatingSystemType.Linux => "linux",
                OperatingSystemType.Windows => "win"
            };

            string archSuffix = RunningPlatform.Architecture switch
            {
                Architecture.Arm64 => "arm64",
                Architecture.X64 => "x64",
                _ => throw new PlatformNotSupportedException($"Unknown architecture {Enum.GetName(RunningPlatform.Architecture)}."),
            };

            string fileExtension = RunningPlatform.CurrentOS switch
#pragma warning restore CS8509
            {
                OperatingSystemType.Linux => "tar.gz",
                OperatingSystemType.Windows => "zip"
            };

            return $"{osPrefix}_{archSuffix}.{fileExtension}";
        }
    }
}
