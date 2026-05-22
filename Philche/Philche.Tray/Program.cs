using System.Threading;
using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Microsoft.Win32;

namespace Philche.Tray;

internal static class Program
{
    private const string CliArgument = "--cli";
    private const string FormatArgument = "--format";
    private const string SingleInstanceMutexName = "Philche.Tray.Singleton";
    private const string OpenModelsProtocol = "philche-open-models";
    private const string OpenModelsSignalName = "Philche.Tray.OpenModels";
    private const string ScanArgument = "--scan";
    private static Mutex? appMutex;
    private static EventWaitHandle? openModelsSignal;
    private static RegisteredWaitHandle? openModelsWaitHandle;
    private static int unhandledExceptionHooksRegistered;

    internal static bool AllowWindowClose { get; set; }
    internal static bool OpenModelsOnStartup { get; private set; }
    internal static IReadOnlyList<string> ScanPathsOnStartup { get; private set; } = [];

    [STAThread]
    public static void Main(string[] args)
    {
        RegisterUnhandledExceptionHooks();

        if (IsCliMode(args))
        {
            ConsoleHelper.EnsureConsole();
            var exitCode = CliRunner.RunAsync(ExtractScanPaths(args), ExtractFormat(args)).GetAwaiter().GetResult();
            Environment.Exit(exitCode);
            return;
        }

        RegisterOpenModelsProtocol();
        OpenModelsOnStartup = IsOpenModelsActivation(args);
        ScanPathsOnStartup = ExtractScanPaths(args);

        var debugMultiInstance = string.Equals(
            Environment.GetEnvironmentVariable("PHILCHE_DEBUG_MULTI_INSTANCE"),
            "1",
            StringComparison.OrdinalIgnoreCase);

        if (debugMultiInstance)
        {
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args, ShutdownMode.OnExplicitShutdown);
            return;
        }

        appMutex = new Mutex(true, SingleInstanceMutexName, out var createdNew);
        if (!createdNew)
        {
            if (ScanPathsOnStartup.Count > 0)
            {
                ScanQueueIpc.EnqueuePaths(ScanPathsOnStartup);
                ScanQueueIpc.Signal();
                return;
            }

            if (OpenModelsOnStartup)
            {
                SignalOpenModels();
                return;
            }

            ToastNotifier.TryShowAlreadyRunning();
            return;
        }

        try
        {
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args, ShutdownMode.OnExplicitShutdown);
        }
        finally
        {
            appMutex.Dispose();
        }
    }

    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>()
        .UsePlatformDetect()
        .LogToTrace();

    private static void RegisterUnhandledExceptionHooks()
    {
        if (Interlocked.Exchange(ref unhandledExceptionHooksRegistered, 1) == 1)
        {
            return;
        }

        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            try
            {
                Console.Error.WriteLine($"[Program] unhandled exception: {args.ExceptionObject}");
            }
            catch
            {
            }
        };

        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            try
            {
                Console.Error.WriteLine($"[Program] unobserved task exception: {args.Exception}");
                args.SetObserved();
            }
            catch
            {
            }
        };
    }

    internal static void RegisterOpenModelsListener(Action onOpenModels)
    {
        openModelsSignal ??= new EventWaitHandle(false, EventResetMode.AutoReset, OpenModelsSignalName);
        openModelsWaitHandle = ThreadPool.RegisterWaitForSingleObject(
            openModelsSignal,
            (_, _) => onOpenModels(),
            null,
            Timeout.Infinite,
            executeOnlyOnce: false);
    }

    internal static void RegisterScanRequestListener(Action<IReadOnlyList<string>> onPaths)
    {
        ScanQueueIpc.RegisterListener(onPaths);
    }

    internal static string? GetPreferredExecutablePath()
    {
        var assemblyPath = typeof(Program).Assembly.Location;
        if (!string.IsNullOrWhiteSpace(assemblyPath))
        {
            if (Path.GetExtension(assemblyPath).Equals(".exe", StringComparison.OrdinalIgnoreCase))
            {
                return assemblyPath;
            }

            var siblingExePath = Path.ChangeExtension(assemblyPath, ".exe");
            if (File.Exists(siblingExePath))
            {
                return siblingExePath;
            }
        }

        return Environment.ProcessPath;
    }

    internal static string? BuildLaunchCommand(params string[] args)
    {
        var executablePath = GetPreferredExecutablePath();
        if (!string.IsNullOrWhiteSpace(executablePath) &&
            Path.GetExtension(executablePath).Equals(".exe", StringComparison.OrdinalIgnoreCase))
        {
            return BuildCommand(executablePath, args);
        }

        var processPath = Environment.ProcessPath;
        var assemblyPath = typeof(Program).Assembly.Location;
        if (string.IsNullOrWhiteSpace(processPath) || string.IsNullOrWhiteSpace(assemblyPath))
        {
            return null;
        }

        return BuildCommand(processPath, [assemblyPath, .. args]);
    }

    private static bool IsOpenModelsActivation(string[] args)
    {
        return args.Any(arg => arg.StartsWith($"{OpenModelsProtocol}://open-models", StringComparison.OrdinalIgnoreCase));
    }

    internal static bool IsCliMode(string[] args)
    {
        return args.Any(static arg => string.Equals(arg, CliArgument, StringComparison.OrdinalIgnoreCase));
    }

    internal static IReadOnlyList<string> ExtractScanPaths(string[] args)
    {
        var flagIndex = Array.FindIndex(args, static arg => string.Equals(arg, ScanArgument, StringComparison.OrdinalIgnoreCase));
        if (flagIndex < 0 || flagIndex >= args.Length - 1)
        {
            return [];
        }

        var paths = new List<string>();
        for (var index = flagIndex + 1; index < args.Length; index++)
        {
            var arg = args[index]?.Trim();
            if (string.IsNullOrWhiteSpace(arg))
            {
                continue;
            }

            if (arg.StartsWith("--", StringComparison.Ordinal))
            {
                break;
            }

            paths.Add(arg);
        }

        return paths
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    internal static string ExtractFormat(string[] args)
    {
        var flagIndex = Array.FindIndex(args, static arg => string.Equals(arg, FormatArgument, StringComparison.OrdinalIgnoreCase));
        if (flagIndex < 0 || flagIndex >= args.Length - 1)
        {
            return "text";
        }

        var format = args[flagIndex + 1]?.Trim();
        return string.Equals(format, "json", StringComparison.OrdinalIgnoreCase)
            ? "json"
            : "text";
    }

    private static void SignalOpenModels()
    {
        try
        {
            using var signal = new EventWaitHandle(false, EventResetMode.AutoReset, OpenModelsSignalName);
            signal.Set();
        }
        catch
        {
        }
    }

    private static void RegisterOpenModelsProtocol()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        try
        {
            var command = BuildLaunchCommand("%1");
            if (string.IsNullOrWhiteSpace(command))
            {
                return;
            }

            using var key = Registry.CurrentUser.CreateSubKey($@"Software\Classes\{OpenModelsProtocol}");
            if (key is null)
            {
                return;
            }

            key.SetValue("", "URL:Philche Open Models");
            key.SetValue("URL Protocol", "");

            using var commandKey = key.CreateSubKey(@"shell\open\command");
            commandKey?.SetValue("", command);
        }
        catch
        {
        }
    }

    private static string BuildCommand(string executablePath, IReadOnlyList<string> args)
    {
        var commandParts = new List<string> { QuoteArgument(executablePath) };
        foreach (var arg in args)
        {
            commandParts.Add(QuoteArgument(arg));
        }

        return string.Join(" ", commandParts);
    }

    private static string QuoteArgument(string arg)
    {
        var escaped = arg.Replace("\"", "\\\"");
        return $"\"{escaped}\"";
    }

}
