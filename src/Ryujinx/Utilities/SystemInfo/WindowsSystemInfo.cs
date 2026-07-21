using Ryujinx.Common.Logging;
using System;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Ryujinx.Ava.Utilities.SystemInfo
{
    [SupportedOSPlatform("windows")]
    partial class WindowsSystemInfo : SystemInfo
    {
        internal WindowsSystemInfo()
        {
            CpuName = $"{GetCpuidCpuName() ?? GetCpuNameFromRegistry()} ; {LogicalCoreCount} logical";
            (RamTotal, RamAvailable) = GetMemoryStats();
        }

        private static (ulong Total, ulong Available) GetMemoryStats()
        {
            MemoryStatusEx memStatus = new();
            if (GlobalMemoryStatusEx(ref memStatus))
            {
                return (memStatus.TotalPhys, memStatus.AvailPhys); // Bytes
            }

            Logger.Error?.Print(LogClass.Application, $"GlobalMemoryStatusEx failed. Error {Marshal.GetLastWin32Error():X}");

            return (0, 0);
        }

        private static string GetCpuNameFromRegistry()
        {
            try
            {
                using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0");

                return key?.GetValue("ProcessorNameString")?.ToString()?.Trim();
            }
            catch (Exception ex)
            {
                Logger.Error?.Print(LogClass.Application, $"Registry CPU name lookup failed: {ex.Message}");

                return Environment.GetEnvironmentVariable("PROCESSOR_IDENTIFIER")?.Trim();
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MemoryStatusEx()
        {
            public uint Length = (uint)Marshal.SizeOf<MemoryStatusEx>();
            public uint MemoryLoad;
            public ulong TotalPhys;
            public ulong AvailPhys;
            public ulong TotalPageFile;
            public ulong AvailPageFile;
            public ulong TotalVirtual;
            public ulong AvailVirtual;
            public ulong AvailExtendedVirtual;
        }

        [LibraryImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static partial bool GlobalMemoryStatusEx(ref MemoryStatusEx lpBuffer);
    }
}
