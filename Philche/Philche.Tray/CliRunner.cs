using System.Text.Json;
using Philche.Core.Config;
using Philche.Core.Domain.Enums;
using Philche.Core.Orchestration;
using Philche.Core.SkillsRisk;

namespace Philche.Tray;

internal static class CliRunner
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
    };

    public static async Task<int> RunAsync(IReadOnlyList<string> rawPaths, string format, CancellationToken cancellationToken = default)
    {
        try
        {
            var resolvedFiles = ResolveScannableFiles(rawPaths, out var pathErrors);
            if (pathErrors.Count > 0)
            {
                foreach (var error in pathErrors)
                {
                    Console.Error.WriteLine(error);
                }

                return 3;
            }

            if (resolvedFiles.Count == 0)
            {
                Console.Error.WriteLine("No scannable files were found. Provide a supported file or directory after --scan.");
                return 3;
            }

            using var snapshot = new EvaluatorFactory(new SettingsYamlStore()).Build();
            var results = new List<CliScanResult>(resolvedFiles.Count);

            foreach (var filePath in resolvedFiles)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var content = await File.ReadAllTextAsync(filePath, cancellationToken);
                var isCode = IsCodeFile(filePath);
                var result = await snapshot.Evaluator.EvaluateAsync(
                    new SkillEvaluationInput(content, filePath, isCode),
                    cancellationToken);

                results.Add(new CliScanResult(
                    filePath,
                    isCode ? "code" : "prompt",
                    result.RiskLevel,
                    result.IsDegradedMode,
                    result.ShouldBlock,
                    result.Evidence));
            }

            WriteOutput(format, results);
            return MapExitCode(results);
        }
        catch (OperationCanceledException)
        {
            Console.Error.WriteLine("CLI scan canceled.");
            return 3;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"CLI scan failed: {ex.Message}");
            return 3;
        }
    }

    internal static IReadOnlyList<string> ResolveScannableFiles(IReadOnlyList<string> rawPaths, out IReadOnlyList<string> pathErrors)
    {
        var collector = new AgentCodeCollector();
        var files = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var errors = new List<string>();

        foreach (var rawPath in rawPaths.Where(static path => !string.IsNullOrWhiteSpace(path)))
        {
            var path = rawPath.Trim();
            if (Directory.Exists(path))
            {
                foreach (var file in collector.GetScannableFiles([path]))
                {
                    files.Add(file);
                }

                continue;
            }

            if (File.Exists(path))
            {
                if (!collector.IsScannableFilePath(path))
                {
                    errors.Add($"Unsupported scan target: {path}");
                    continue;
                }

                files.Add(path);
                continue;
            }

            errors.Add($"Scan target not found: {path}");
        }

        pathErrors = errors;
        return files.OrderBy(static path => path, StringComparer.OrdinalIgnoreCase).ToList();
    }

    internal static bool IsCodeFile(string filePath)
    {
        return !string.Equals(Path.GetExtension(filePath), ".md", StringComparison.OrdinalIgnoreCase);
    }

    private static void WriteOutput(string format, IReadOnlyList<CliScanResult> results)
    {
        if (string.Equals(format, "json", StringComparison.OrdinalIgnoreCase))
        {
            Console.WriteLine(JsonSerializer.Serialize(new CliScanReport(results), JsonOptions));
            return;
        }

        foreach (var result in results)
        {
            Console.WriteLine($"[{result.RiskLevel}] {result.Kind}: {result.Path}");
            Console.WriteLine($"  degraded={result.IsDegradedMode}, block={result.ShouldBlock}");

            foreach (var evidence in result.Evidence)
            {
                Console.WriteLine($"  - {evidence.Detector}: score={evidence.Score:F3} {evidence.Message}");
            }
        }
    }

    private static int MapExitCode(IReadOnlyList<CliScanResult> results)
    {
        var highestRisk = results.Count == 0
            ? RiskLevel.Low
            : results.Max(static result => result.RiskLevel);

        return highestRisk switch
        {
            RiskLevel.High => 2,
            RiskLevel.Medium => 1,
            _ => 0,
        };
    }

    internal sealed record CliScanReport(IReadOnlyList<CliScanResult> Results);

    internal sealed record CliScanResult(
        string Path,
        string Kind,
        RiskLevel RiskLevel,
        bool IsDegradedMode,
        bool ShouldBlock,
        IReadOnlyList<SkillRiskEvidence> Evidence);
}
