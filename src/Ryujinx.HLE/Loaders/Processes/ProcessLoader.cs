using LibHac.Common;
using LibHac.Fs;
using LibHac.Fs.Fsa;
using LibHac.FsSystem;
using LibHac.Ns;
using LibHac.Tools.Fs;
using LibHac.Tools.FsSystem;
using LibHac.Tools.FsSystem.NcaUtils;
using Ryujinx.Common;
using Ryujinx.Common.Logging;
using Ryujinx.Graphics.Gpu;
using Ryujinx.HLE.HOS.SystemState;
using Ryujinx.HLE.Loaders.Executables;
using Ryujinx.HLE.Loaders.Processes.Extensions;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using Path = System.IO.Path;

namespace Ryujinx.HLE.Loaders.Processes
{
    public class ProcessLoader
    {
        private readonly Switch _device;

        private readonly ConcurrentDictionary<ulong, ProcessResult> _processesByPid;

        private ulong _latestPid;

        private readonly object _pidLock = new();

#nullable enable
        public ProcessResult? ActiveApplication
        {
            get
            {
                lock (_pidLock)
                {
                    // Check if _latestPid is still valid
                    if (_latestPid == 0)
                    {
                        return null;
                    }

                    // Verify process still exists in kernel (authoritative source)
                    if (!_device.System.KernelContext.Processes.TryGetValue(_latestPid, out HOS.Kernel.Process.KProcess? kernelProcess))
                    {
                        // Process no longer exists in kernel, clear stale state
                        Logger.Warning?.Print(LogClass.Loader,
                            $"ActiveApplication PID {_latestPid} no longer exists in kernel, clearing stale state");

                        _processesByPid.TryRemove(_latestPid, out _);
                        _latestPid = 0;
                        TitleIDs.CurrentApplication.Value = null;

                        return null;
                    }

                    // Verify process still exists in ProcessLoader's dictionary
                    if (_processesByPid.TryGetValue(_latestPid, out ProcessResult? processResult))
                    {
                        // Additional check: verify process state
                        if (kernelProcess.State == HOS.Kernel.Process.ProcessState.Exited ||
                            kernelProcess.State == HOS.Kernel.Process.ProcessState.Exiting)
                        {
                            Logger.Warning?.Print(LogClass.Loader,
                                $"ActiveApplication PID {_latestPid} is in state {kernelProcess.State}, clearing");

                            _processesByPid.TryRemove(_latestPid, out _);
                            _latestPid = 0;
                            TitleIDs.CurrentApplication.Value = null;

                            return null;
                        }

                        return processResult;
                    }

                    // Fallback: clear stale PID if not in our dictionary
                    Logger.Warning?.Print(LogClass.Loader,
                        $"ActiveApplication PID {_latestPid} not in ProcessLoader dictionary, clearing");
                    _latestPid = 0;
                    return null;
                }
            }
        }
#nullable disable

        public ProcessLoader(Switch device)
        {
            _device = device;
            _processesByPid = new ConcurrentDictionary<ulong, ProcessResult>();
        }

        public bool TryGetProcess(ulong pid, out ProcessResult process)
        {
            return _processesByPid.TryGetValue(pid, out process);
        }

        public ProcessResult GetProcess(ulong pid)
        {
            if (_processesByPid.TryGetValue(pid, out ProcessResult process))
            {
                return process;
            }

            Logger.Warning?.Print(LogClass.Loader, $"Process metadata for pid {pid} was not found. Falling back to active application metadata.");

            return ActiveApplication;
        }

        public bool LoadXci(string path, ulong applicationId)
        {
            FileStream stream = new(path, FileMode.Open, FileAccess.Read);
            Xci xci = new(_device.Configuration.VirtualFileSystem.KeySet, stream.AsStorage());

            if (!xci.HasPartition(XciPartitionType.Secure))
            {
                Logger.Error?.Print(LogClass.Loader, "Unable to load XCI: Could not find XCI Secure partition");

                return false;
            }

            (bool success, ProcessResult processResult) = xci.OpenPartition(XciPartitionType.Secure).TryLoad(_device, path, applicationId, out string errorMessage);

            if (!success)
            {
                Logger.Error?.Print(LogClass.Loader, errorMessage, nameof(PartitionFileSystemExtensions.TryLoad));

                return false;
            }

            if (processResult.ProcessId != 0 && _processesByPid.TryAdd(processResult.ProcessId, processResult))
            {
                if (processResult.Start(_device))
                {
                    _latestPid = processResult.ProcessId;

                    TitleIDs.CurrentApplication.Value = processResult.ProgramIdText;

                    return true;
                }
            }

            return false;
        }

        public bool LoadNsp(string path, ulong applicationId)
        {
            FileStream file = new(path, FileMode.Open, FileAccess.Read);
            PartitionFileSystem partitionFileSystem = new();
            partitionFileSystem.Initialize(file.AsStorage()).ThrowIfFailure();

            (bool success, ProcessResult processResult) = partitionFileSystem.TryLoad(_device, path, applicationId, out string errorMessage);

            if (processResult.ProcessId == 0)
            {
                // This is not a normal NSP, it's actually a ExeFS as a NSP
                processResult = partitionFileSystem.Load(_device, new BlitStruct<ApplicationControlProperty>(1), partitionFileSystem.GetNpdm(), 0, true);
            }

            if (processResult.ProcessId != 0 && _processesByPid.TryAdd(processResult.ProcessId, processResult))
            {
                if (processResult.Start(_device))
                {
                    _latestPid = processResult.ProcessId;

                    TitleIDs.CurrentApplication.Value = processResult.ProgramIdText;

                    return true;
                }
            }

            if (!success)
            {
                Logger.Error?.Print(LogClass.Loader, errorMessage, nameof(PartitionFileSystemExtensions.TryLoad));
            }

            return false;
        }

        public bool LoadNca(string path, BlitStruct<ApplicationControlProperty>? customNacpData = null)
        {
            FileStream file = new(path, FileMode.Open, FileAccess.Read);
            Nca nca = new(_device.Configuration.VirtualFileSystem.KeySet, file.AsStorage(false));

            ProcessResult processResult = nca.Load(_device, null, null, customNacpData);

            if (processResult.ProcessId != 0 && _processesByPid.TryAdd(processResult.ProcessId, processResult))
            {
                if (processResult.Start(_device))
                {
                    // NOTE: Check if process is SystemApplicationId or ApplicationId
                    if (processResult.ProgramId > 0x01000000000007FF)
                    {
                        _latestPid = processResult.ProcessId;

                        TitleIDs.CurrentApplication.Value = processResult.ProgramIdText;
                    }

                    return true;
                }
            }

            return false;
        }

        public bool LoadUnpackedNca(string exeFsDirPath, string romFsPath = null)
        {
            ProcessResult processResult = new LocalFileSystem(exeFsDirPath).Load(_device, romFsPath);
            
            if (processResult.ProcessId != 0 && _processesByPid.TryAdd(processResult.ProcessId, processResult))
            {
                if (processResult.Start(_device))
                {
                    _latestPid = processResult.ProcessId;

                    TitleIDs.CurrentApplication.Value = processResult.ProgramIdText;

                    return true;
                }
            }

            return false;
        }

        public bool LoadNxo(string path)
        {
            BlitStruct<ApplicationControlProperty> nacpData = new(1);
            IFileSystem dummyExeFs = null;
            Stream romfsStream = null;

            string programName = string.Empty;
            ulong programId = 0000000000000000;

            // Load executable.
            IExecutable executable;

            if (Path.GetExtension(path).Equals(".nro", StringComparison.OrdinalIgnoreCase))
            {
                FileStream input = new(path, FileMode.Open);
                NroExecutable nro = new(input.AsStorage());

                executable = nro;

                // Open RomFS if exists.
                IStorage romFsStorage = nro.OpenNroAssetSection(LibHac.Tools.Ro.NroAssetType.RomFs, false);
                romFsStorage.GetSize(out long romFsSize).ThrowIfFailure();
                if (romFsSize != 0)
                {
                    romfsStream = romFsStorage.AsStream();
                }

                // Load Nacp if exists.
                IStorage nacpStorage = nro.OpenNroAssetSection(LibHac.Tools.Ro.NroAssetType.Nacp, false);
                nacpStorage.GetSize(out long nacpSize).ThrowIfFailure();
                if (nacpSize != 0)
                {
                    nacpStorage.Read(0, nacpData.ByteSpan);

                    programName = nacpData.Value.Title[(int)_device.System.State.DesiredTitleLanguage].NameString.ToString();

                    if ("Switch Verification" ==
                        nacpData.Value.Title[(int)TitleLanguage.AmericanEnglish].NameString.ToString())
                        throw new InvalidOperationException();

                    if (string.IsNullOrWhiteSpace(programName))
                    {
                        foreach (ApplicationControlProperty.ApplicationTitle nacpTitles in nacpData.Value.Title)
                        {
                            if (nacpTitles.Name[0] != 0)
                                continue;

                            programName = nacpTitles.NameString.ToString();
                        }
                    }

                    if (nacpData.Value.PresenceGroupId != 0)
                    {
                        programId = nacpData.Value.PresenceGroupId;
                        TitleIDs.CurrentApplication.Value = programId.ToString("X16");
                    }
                    else if (nacpData.Value.SaveDataOwnerId != 0)
                    {
                        programId = nacpData.Value.SaveDataOwnerId;
                        TitleIDs.CurrentApplication.Value = programId.ToString("X16");
                    }
                    else if (nacpData.Value.AddOnContentBaseId != 0)
                    {
                        programId = nacpData.Value.AddOnContentBaseId - 0x1000;
                        TitleIDs.CurrentApplication.Value = programId.ToString("X16");
                    }
                }

                // TODO: Add icon maybe ?
            }
            else
            {
                programName = Path.GetFileNameWithoutExtension(path);

                executable = new NsoExecutable(new LocalStorage(path, FileAccess.Read), programName);
            }

            // Explicitly null TitleId to disable the shader cache.
            GraphicsConfig.TitleId = null;
            _device.Gpu.HostInitalized.Set();

            ProcessResult processResult = ProcessLoaderHelper.LoadNsos(_device,
                                                                       _device.System.KernelContext,
                                                                       dummyExeFs.GetNpdm(),
                                                                       nacpData,
                                                                       diskCacheEnabled: false,
                                                                       diskCacheSelector: null,
                                                                       allowCodeMemoryForJit: true,
                                                                       programName,
                                                                       programId,
                                                                       0,
                                                                       null,
                                                                       executable);

            // Make sure the process id is valid.
            if (processResult.ProcessId != 0)
            {
                // Load RomFS.
                if (romfsStream != null)
                {
                    _device.Configuration.VirtualFileSystem.SetRomFs(processResult.ProcessId, romfsStream);
                }

                // Start process.
                if (_processesByPid.TryAdd(processResult.ProcessId, processResult))
                {
                    if (processResult.Start(_device))
                    {
                        _latestPid = processResult.ProcessId;

                        return true;
                    }
                }
            }

            return false;
        }

        /// <summary>
        /// Clears a specific process from the ProcessLoader's tracking.
        /// This should be called when a process exits or is terminated.
        /// </summary>
        /// <param name="pid">The process ID to clear</param>
        public void ClearProcess(ulong pid)
        {
            lock (_pidLock)
            {
                if (_processesByPid.TryRemove(pid, out _))
                {
                    if (_latestPid == pid)
                    {
                        _latestPid = 0;
                        TitleIDs.CurrentApplication.Value = null;
                    }
                }
            }
        }

        /// <summary>
        /// Clears all processes from the ProcessLoader's tracking.
        /// This should be called during system shutdown.
        /// </summary>
        public void ClearAllProcesses()
        {
            lock (_pidLock)
            {
                _processesByPid.Clear();
                _latestPid = 0;
                TitleIDs.CurrentApplication.Value = null;
            }
        }
    }
}
