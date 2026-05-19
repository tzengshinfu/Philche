using System.Runtime.InteropServices;

namespace Philche.Tray;

internal static class ConsoleHelper
{
    private const uint AttachParentProcess = 0xFFFFFFFF;

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AttachConsole(uint dwProcessId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AllocConsole();

    public static void EnsureConsole()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        if (!AttachConsole(AttachParentProcess))
        {
            _ = AllocConsole();
        }
    }
}
