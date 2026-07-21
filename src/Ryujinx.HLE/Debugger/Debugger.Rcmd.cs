using Gommon;
using Ryujinx.Common.Logging;
using Ryujinx.HLE.HOS.Kernel.Memory;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace Ryujinx.HLE.Debugger
{
    public partial class Debugger
    {
        private sealed record RcmdEntry(string[] Names, Func<Debugger, string, string> Handler, string[] HelpLines);

        // Atmosphere/libraries/libmesosphere/source/kern_k_memory_block_manager.cpp
        private static readonly string[] _memoryStateNames =
        {
            "----- Free -----",
            "Io              ",
            "Static          ",
            "Code            ",
            "CodeData        ",
            "Normal          ",
            "Shared          ",
            "Alias           ",
            "AliasCode       ",
            "AliasCodeData   ",
            "Ipc             ",
            "Stack           ",
            "ThreadLocal     ",
            "Transfered      ",
            "SharedTransfered",
            "SharedCode      ",
            "Inaccessible    ",
            "NonSecureIpc    ",
            "NonDeviceIpc    ",
            "Kernel          ",
            "GeneratedCode   ",
            "CodeOut         ",
            "Coverage        ",
        };

        static Debugger()
        {
            _rcmdDelegates.Add(new RcmdEntry(
                ["help"],
                (dbgr, _) => _rcmdDelegates
                    .Where(entry => entry.HelpLines.Length > 0)
                    .SelectMany(entry => entry.HelpLines)
                    .JoinToString('\n') + '\n',
                Array.Empty<string>()));

            _rcmdDelegates.Add(new RcmdEntry(["get info"], (dbgr, _) => dbgr.GetProcessInfo(), ["get info"]));
            _rcmdDelegates.Add(new RcmdEntry(["backtrace", "bt"], (dbgr, _) => dbgr.GetStackTrace(), ["backtrace", "bt"]));
            _rcmdDelegates.Add(new RcmdEntry(["registers", "reg"], (dbgr, _) => dbgr.GetRegisters(), ["registers", "reg"]));
            _rcmdDelegates.Add(new RcmdEntry(["minidump"], (dbgr, _) => dbgr.GetMinidump(), ["minidump"]));
            _rcmdDelegates.Add(new RcmdEntry(["get mappings"], (dbgr, args) => dbgr.GetMemoryMappings(args), ["get mappings", "get mappings {address}"]));
            _rcmdDelegates.Add(new RcmdEntry(["get mapping"], (dbgr, args) => dbgr.GetMemoryMapping(args), ["get mapping {address}"]));
        }

        private static readonly List<RcmdEntry> _rcmdDelegates = [];

        public static string CallRcmdDelegate(Debugger debugger, string command)
        {
            string originalCommand = command ?? string.Empty;
            string trimmedCommand = originalCommand.Trim();

            foreach (RcmdEntry entry in _rcmdDelegates)
            {
                foreach (string name in entry.Names)
                {
                    if (trimmedCommand.Equals(name, StringComparison.OrdinalIgnoreCase))
                    {
                        return entry.Handler(debugger, string.Empty);
                    }

                    if (trimmedCommand.Length > name.Length &&
                        trimmedCommand.StartsWith(name, StringComparison.OrdinalIgnoreCase) &&
                        char.IsWhiteSpace(trimmedCommand[name.Length]))
                    {
                        string arguments = trimmedCommand[name.Length..].TrimStart();
                        return entry.Handler(debugger, arguments);
                    }
                }
            }

            return $"Unknown command: {originalCommand}\n";
        }

        public string GetStackTrace()
        {
            if (GThreadId == null)
                return "No thread selected\n";

            return Process?.Debugger?.GetGuestStackTrace(DebugProcess.GetThread(GThreadId.Value)) ?? "No application process found\n"; 
        }

        public string GetRegisters()
        {
            if (GThreadId == null)
                return "No thread selected\n";

            return Process?.Debugger?.GetCpuRegisterPrintout(DebugProcess.GetThread(GThreadId.Value)) ?? "No application process found\n";
        }

        public string GetMinidump()
        {
            if (Process is not { } kProcess)
                return "No application process found\n";

            if (kProcess.Debugger is not { } debugger)
                return "Error getting minidump: debugger is null\n";

            string response = debugger.GetMinidump();

            Logger.Info?.Print(LogClass.GdbStub, response);
            return response;
        }

        public string GetProcessInfo()
        {
            try
            {
                if (Process is not { } kProcess)
                    return "No application process found\n";

                return kProcess.Debugger?.GetProcessInfoPrintout() 
                       ?? "Error getting process info: debugger is null\n";
            }
            catch (Exception e)
            {
                Logger.Error?.Print(LogClass.GdbStub, $"Error getting process info: {e.Message}");
                return $"Error getting process info: {e.Message}\n";
            }
        }

        public string GetMemoryMappings(string arguments)
        {
            if (Process?.MemoryManager is not { } memoryManager)
            {
                return "No application process found\n";
            }

            string trimmedArgs = arguments?.Trim() ?? string.Empty;

            ulong startAddress = 0;
            if (!string.IsNullOrEmpty(trimmedArgs))
            {
                if (!TryParseAddressArgument(trimmedArgs, out startAddress))
                {
                    return $"Invalid address: {trimmedArgs}\n";
                }
            }

            ulong requestedAddress = startAddress;
            ulong currentAddress = Math.Max(requestedAddress, memoryManager.AddrSpaceStart);
            StringBuilder sb = new();
            sb.AppendLine($"Mappings (starting from 0x{requestedAddress:x10}):");

            if (currentAddress >= memoryManager.AddrSpaceEnd)
            {
                return sb.ToString();
            }

            while (currentAddress < memoryManager.AddrSpaceEnd)
            {
                KMemoryInfo info = memoryManager.QueryMemory(currentAddress);

                try
                {
                    if (info.Size == 0 || info.Address >= memoryManager.AddrSpaceEnd)
                    {
                        break;
                    }

                    sb.AppendLine(FormatMapping(info, indent: true));

                    if (info.Address > ulong.MaxValue - info.Size)
                    {
                        break;
                    }

                    ulong nextAddress = info.Address + info.Size;
                    if (nextAddress <= currentAddress)
                    {
                        break;
                    }

                    currentAddress = nextAddress;
                }
                finally
                {
                    KMemoryInfo.Pool.Release(info);
                }
            }

            return sb.ToString();
        }

        public string GetMemoryMapping(string arguments)
        {
            if (Process?.MemoryManager is not { } memoryManager)
            {
                return "No application process found\n";
            }

            string trimmedArgs = arguments?.Trim() ?? string.Empty;

            if (string.IsNullOrEmpty(trimmedArgs))
            {
                return "Missing address argument for `get mapping`\n";
            }

            if (!TryParseAddressArgument(trimmedArgs, out ulong address))
            {
                return $"Invalid address: {trimmedArgs}\n";
            }

            KMemoryInfo info = memoryManager.QueryMemory(address);

            try
            {
                return FormatMapping(info, indent: false) + '\n';
            }
            finally
            {
                KMemoryInfo.Pool.Release(info);
            }
        }

        private static string FormatMapping(KMemoryInfo info, bool indent)
        {
            ulong endAddress;

            if (info.Size == 0)
            {
                endAddress = info.Address;
            }
            else if (info.Address > ulong.MaxValue - (info.Size - 1))
            {
                endAddress = ulong.MaxValue;
            }
            else
            {
                endAddress = info.Address + info.Size - 1;
            }

            string prefix = indent ? "  " : string.Empty;
            return $"{prefix}0x{info.Address:x10} - 0x{endAddress:x10} {GetPermissionString(info)} {GetMemoryStateName(info.State)} {GetAttributeFlags(info)} [{info.IpcRefCount}, {info.DeviceRefCount}]";
        }

        private static string GetPermissionString(KMemoryInfo info)
        {
            if ((info.State & MemoryState.UserMask) == MemoryState.Unmapped)
            {
                return "   ";
            }

            return info.Permission switch
            {
                KMemoryPermission.ReadAndExecute => "r-x",
                KMemoryPermission.Read => "r--",
                KMemoryPermission.ReadAndWrite => "rw-",
                _ => "---"
            };
        }

        private static string GetMemoryStateName(MemoryState state)
        {
            int stateIndex = (int)(state & MemoryState.UserMask);
            if ((uint)stateIndex < _memoryStateNames.Length)
            {
                return _memoryStateNames[stateIndex];
            }

            return "Unknown         ";
        }

        private static bool TryParseAddressArgument(string text, out ulong value)
        {
            value = 0;

            if (string.IsNullOrWhiteSpace(text))
            {
                return false;
            }

            string trimmed = text.Trim();

            if (trimmed.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            {
                trimmed = trimmed[2..];
            }

            if (trimmed.Length == 0)
            {
                return false;
            }

            return ulong.TryParse(trimmed, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out value);
        }

        private static string GetAttributeFlags(KMemoryInfo info)
        {
            char locked = info.Attribute.HasFlag(MemoryAttribute.Borrowed) ? 'L' : '-';
            char ipc = info.Attribute.HasFlag(MemoryAttribute.IpcMapped) ? 'I' : '-';
            char device = info.Attribute.HasFlag(MemoryAttribute.DeviceMapped) ? 'D' : '-';
            char uncached = info.Attribute.HasFlag(MemoryAttribute.Uncached) ? 'U' : '-';

            return $"{locked}{ipc}{device}{uncached}";
        }
    }
}
