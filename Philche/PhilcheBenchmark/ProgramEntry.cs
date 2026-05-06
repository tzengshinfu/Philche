using Philche.Core.Config;
using Philche.Core.SkillsRisk;

namespace PhilcheBenchmark;

internal static class ProgramEntry
{
    public static async Task<int> RunAsync(string[] args)
    {
        CommandLineOptions options;

        try
        {
            options = CommandLineOptions.Parse(args);
        }
        catch (ArgumentException ex)
        {
            Console.Error.WriteLine(ex.Message);
            Console.Error.WriteLine();
            Console.Error.WriteLine(CommandLineOptions.Usage);
            return 1;
        }

        if (options.ShowHelp)
        {
            Console.WriteLine(CommandLineOptions.Usage);
            return 0;
        }

        var modelPath = ResolveModelPath(options);

        if (string.IsNullOrWhiteSpace(modelPath))
        {
            Console.Error.WriteLine("Missing model path. Provide --model <path>, set PHILCHE_GUARD_MODEL_PATH, configure GuardModelPath in Settings.yaml, or place a GGUF under %LOCALAPPDATA%/Philche/models.");
            return 1;
        }

        var casesPath = options.CasesPath;
        if (string.IsNullOrWhiteSpace(casesPath))
        {
            casesPath = Path.Combine(AppContext.BaseDirectory, "test-cases");
        }

        IReadOnlyList<BenchmarkTestCase> testCases;
        try
        {
            testCases = TestCaseLoader.Load(casesPath);
            PromptConstraintValidator.Validate(testCases);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            Console.Error.WriteLine($"Failed to load test cases: {ex.Message}");
            return 1;
        }

        using var cancellation = new CancellationTokenSource();
        Console.CancelKeyPress += (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            cancellation.Cancel();
        };

        try
        {
            var runner = new BenchmarkRunner();
            var runResult = await runner.RunAsync(modelPath, testCases, cancellation.Token);
            ReportWriter.Write(Console.Out, runResult);
            return 0;
        }
        catch (OperationCanceledException)
        {
            Console.Error.WriteLine("Benchmark canceled.");
            return 1;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Benchmark failed: {ex.Message}");
            return 1;
        }
    }

    private static string? ResolveModelPath(CommandLineOptions options)
    {
        if (!string.IsNullOrWhiteSpace(options.ModelPath))
        {
            return options.ModelPath;
        }

        var envPath = Environment.GetEnvironmentVariable("PHILCHE_GUARD_MODEL_PATH");
        if (!string.IsNullOrWhiteSpace(envPath))
        {
            return envPath;
        }

        var settingsPath = new SettingsYamlStore().LoadModelPaths().GuardModelPath;
        if (!string.IsNullOrWhiteSpace(settingsPath))
        {
            return settingsPath;
        }

        var defaultModelDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Philche",
            "models");

        if (!Directory.Exists(defaultModelDirectory))
        {
            return null;
        }

        return Directory
            .EnumerateFiles(defaultModelDirectory, "*.gguf", SearchOption.TopDirectoryOnly)
            .OrderBy(static path => path, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
    }
}
