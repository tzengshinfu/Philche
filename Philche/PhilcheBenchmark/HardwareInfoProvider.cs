using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace PhilcheBenchmark;

internal static class HardwareInfoProvider
{
    public static HardwareInfo GetCurrent()
    {
        return new HardwareInfo(
            CpuName: GetCpuName(),
            TotalMemory: GetTotalMemory(),
            OperatingSystem: RuntimeInformation.OSDescription,
            LogicalCores: Environment.ProcessorCount);
    }

    private static string GetCpuName()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            var value = Registry.GetValue(@"HKEY_LOCAL_MACHINE\HARDWARE\DESCRIPTION\System\CentralProcessor\0", "ProcessorNameString", null) as string;
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value.Trim();
            }
        }

        return Environment.GetEnvironmentVariable("PROCESSOR_IDENTIFIER") ?? "Unknown CPU";
    }

    private static string GetTotalMemory()
    {
        try
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows) && TryGetGlobalMemoryStatus(out var memoryStatus))
            {
                return FormatBytes(memoryStatus.ullTotalPhys);
            }
        }
        catch
        {
        }

        return "Unknown RAM";
    }

    private static string FormatBytes(ulong bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        var size = bytes;
        var unitIndex = 0;
        double display = size;
        while (display >= 1024 && unitIndex < units.Length - 1)
        {
            display /= 1024;
            unitIndex++;
        }

        return $"{display:0.##} {units[unitIndex]}";
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct MemoryStatusEx
    {
        public uint dwLength;
        public uint dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatusEx lpBuffer);

    private static bool TryGetGlobalMemoryStatus(out MemoryStatusEx memoryStatus)
    {
        memoryStatus = new MemoryStatusEx
        {
            dwLength = (uint)Marshal.SizeOf<MemoryStatusEx>(),
        };

        return GlobalMemoryStatusEx(ref memoryStatus);
    }
}
