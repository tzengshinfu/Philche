using System.Text.Json;
using Philche.Core.Config;
using Philche.Core.Domain.Enums;
using Philche.Core.Orchestration;
using Philche.Core.SkillsRisk;

namespace Philche.Tray;

internal static class CliRunner
{
    private const string VirusTotalSignupUrl = "https://www.virustotal.com/gui/sign-in";
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
    };

    public static Task<int> RunAsync(
        IReadOnlyList<string> rawPaths,
        string format,
        CliScanOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        return RunAsync(
            rawPaths,
            format,
            options ?? CliScanOptions.Disabled,
            new SettingsYamlStore(),
            new HttpModelDownloader(),
            Console.In,
            Console.Out,
            Console.Error,
            cancellationToken);
    }

    internal static async Task<int> RunAsync(
        IReadOnlyList<string> rawPaths,
        string format,
        CliScanOptions options,
        ISettingsYamlStore settingsStore,
        IModelDownloader modelDownloader,
        TextReader input,
        TextWriter output,
        TextWriter error,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var resolvedFiles = ResolveScannableFiles(rawPaths, out var pathErrors);
            if (pathErrors.Count > 0)
            {
                foreach (var pathError in pathErrors)
                {
                    await error.WriteLineAsync(pathError);
                }

                return 3;
            }

            if (resolvedFiles.Count == 0)
            {
                await error.WriteLineAsync("No scannable files were found. Provide a supported file or directory after --scan.");
                return 3;
            }

            var effectiveScanning = BuildScanningConfig(settingsStore.LoadScanningConfig(), options);
            if (RequiresVirusTotalApiKey(effectiveScanning))
            {
                await error.WriteLineAsync(BuildVirusTotalApiKeyMissingMessage());
                return 3;
            }

            var effectiveModelPaths = await EnsureGuardModelAsync(
                effectiveScanning,
                settingsStore.LoadModelPaths(),
                settingsStore,
                modelDownloader,
                input,
                output,
                error,
                cancellationToken);

            using var snapshot = new EvaluatorFactory(new CliSettingsStore(settingsStore, effectiveModelPaths, effectiveScanning)).Build();
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

            WriteOutput(format, results, output);
            return MapExitCode(results);
        }
        catch (OperationCanceledException)
        {
            await error.WriteLineAsync("CLI scan canceled.");
            return 3;
        }
        catch (Exception ex)
        {
            await error.WriteLineAsync($"CLI scan failed: {ex.Message}");
            return 3;
        }
    }

    internal static ScanningConfig BuildScanningConfig(ScanningConfig baseConfig, CliScanOptions options)
    {
        return new ScanningConfig
        {
            CodeFileExtensions = baseConfig.CodeFileExtensions,
            VirusTotalApiKey = string.IsNullOrWhiteSpace(options.VirusTotalApiKey)
                ? baseConfig.VirusTotalApiKey
                : options.VirusTotalApiKey.Trim(),
            EnableSemanticScan = options.EnableAll || options.EnableSemantic || baseConfig.EnableSemanticScan,
            EnableYaraScan = options.EnableAll || options.EnableYara || baseConfig.EnableYaraScan,
            EnableGuardModelScan = options.EnableAll || options.EnableGguf || baseConfig.EnableGuardModelScan,
            EnableMaliciousWordGroupList = options.EnableAll || options.EnableRules || baseConfig.EnableMaliciousWordGroupList,
            EnableInvisibleCharacterDetection = options.EnableAll || options.EnableRules || baseConfig.EnableInvisibleCharacterDetection,
            EnableLlmIntentRecognition = options.EnableAll || options.EnableGguf || baseConfig.EnableLlmIntentRecognition,
            EnableRegexScan = options.EnableAll || options.EnableRegex || baseConfig.EnableRegexScan,
            EnableVirusTotalSkillUrlScan = options.EnableAll || options.EnableVirusTotal || baseConfig.EnableVirusTotalSkillUrlScan,
            EnableVirusTotalScriptUrlScan = options.EnableAll || options.EnableVirusTotal || baseConfig.EnableVirusTotalScriptUrlScan,
            EnableCveCorrelation = options.EnableAll || baseConfig.EnableCveCorrelation,
        };
    }

    internal static bool RequiresVirusTotalApiKey(ScanningConfig scanningConfig)
    {
        return (scanningConfig.EnableVirusTotalSkillUrlScan || scanningConfig.EnableVirusTotalScriptUrlScan) &&
               string.IsNullOrWhiteSpace(scanningConfig.VirusTotalApiKey);
    }

    internal static string BuildVirusTotalApiKeyMissingMessage()
    {
        return $"VirusTotal scan was requested but no API key was provided. Register at {VirusTotalSignupUrl} and pass --virustotal-api-key <key> before scanning.";
    }

    internal static string BuildHelpText()
    {
        return string.Join(Environment.NewLine,
        [
            "Philche CLI Usage",
            "  philche --cli --scan <file-or-dir> [paths...] [options]",
            string.Empty,
            "Formats:",
            "  --format text|json          Output format (default: text)",
            string.Empty,
            "Scan method flags:",
            "  --semantic                  Enable semantic similarity detection",
            "  --rules                     Enable malicious-word and invisible-character detection",
            "  --regex                     Enable regex signal detection",
            "  --yara                      Enable YARA code scanning",
            "  --gguf                      Enable GGUF guard-model scanning",
            "  --virustotal                Enable VirusTotal URL scanning",
            "  --all                       Force-enable all scan methods",
            string.Empty,
            "VirusTotal:",
            "  --virustotal-api-key <key>  Provide the VirusTotal API key required by --virustotal/--all",
            $"  Register at: {VirusTotalSignupUrl}",
            string.Empty,
            "GGUF model behavior:",
            "  If GGUF scanning is enabled and no guard model is found, Philche prompts to download it and shows progress before scanning.",
            string.Empty,
            "Examples:",
            "  philche --cli --scan C:\\repo --semantic --rules --regex",
            "  philche --cli --scan C:\\repo --virustotal --virustotal-api-key <key>",
            "  philche --cli --scan C:\\repo --all --virustotal-api-key <key>",
            "  philche --help",
        ]);
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

    private static async Task<ModelPathsConfig> EnsureGuardModelAsync(
        ScanningConfig scanningConfig,
        ModelPathsConfig modelPaths,
        ISettingsYamlStore settingsStore,
        IModelDownloader modelDownloader,
        TextReader input,
        TextWriter output,
        TextWriter error,
        CancellationToken cancellationToken)
    {
        if (!RequiresGuardModel(scanningConfig))
        {
            return modelPaths;
        }

        var normalizedModelName = HuggingFaceGuardModelLocator.NormalizeModelName(modelPaths.ModelName);
        if (!string.IsNullOrWhiteSpace(modelPaths.GuardModelPath) && File.Exists(modelPaths.GuardModelPath))
        {
            return new ModelPathsConfig
            {
                ModelName = normalizedModelName,
                GuardModelPath = modelPaths.GuardModelPath,
                CveSummaryModelPath = modelPaths.CveSummaryModelPath,
            };
        }

        await error.WriteLineAsync("GGUF model was requested for guard scanning but no GGUF file was found.");
        await output.WriteAsync($"Download GGUF model '{normalizedModelName}' now? [y/N]: ");
        await output.FlushAsync();

        var answer = await input.ReadLineAsync();
        if (!string.Equals(answer?.Trim(), "y", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(answer?.Trim(), "yes", StringComparison.OrdinalIgnoreCase))
        {
            await error.WriteLineAsync("Continuing scan without GGUF model. Guard scan will run in degraded mode.");
            return new ModelPathsConfig
            {
                ModelName = normalizedModelName,
                GuardModelPath = modelPaths.GuardModelPath,
                CveSummaryModelPath = modelPaths.CveSummaryModelPath,
            };
        }

        var targetDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Philche",
            "models");

        var progress = new Progress<(long downloaded, long total)>(state => RenderDownloadProgress(output, state.downloaded, state.total));
        var downloader = new HuggingFaceGuardModelDownloader(modelDownloader);
        var downloadedPath = await downloader.DownloadAsync(
            normalizedModelName,
            targetDir,
            progress,
            status => WriteDownloadStatus(error, status),
            cancellationToken);

        await output.WriteLineAsync();
        await output.WriteLineAsync($"GGUF download completed: {downloadedPath}");

        var updated = new ModelPathsConfig
        {
            ModelName = normalizedModelName,
            GuardModelPath = downloadedPath,
            CveSummaryModelPath = modelPaths.CveSummaryModelPath,
        };

        settingsStore.SaveModelPaths(updated);
        return updated;
    }

    private static bool RequiresGuardModel(ScanningConfig scanningConfig)
    {
        return scanningConfig.EnableGuardModelScan && scanningConfig.EnableLlmIntentRecognition;
    }

    private static void RenderDownloadProgress(TextWriter output, long downloaded, long total)
    {
        if (total <= 0)
        {
            return;
        }

        const int width = 20;
        var ratio = Math.Clamp(downloaded / (double)total, 0d, 1d);
        var filled = (int)Math.Round(ratio * width, MidpointRounding.AwayFromZero);
        var bar = string.Concat(new string('█', filled), new string('░', Math.Max(0, width - filled)));
        var percentage = ratio * 100;
        var line = $"\rDownloading GGUF: [{bar}] {percentage,6:F1}% ({FormatBytes(downloaded)} / {FormatBytes(total)})";
        output.Write(line);
        output.Flush();
    }

    private static string FormatBytes(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB"];
        double value = bytes;
        var unitIndex = 0;
        while (value >= 1024 && unitIndex < units.Length - 1)
        {
            value /= 1024;
            unitIndex++;
        }

        return $"{value:F1} {units[unitIndex]}";
    }

    private static void WriteDownloadStatus(TextWriter error, GuardModelDownloadStatus status)
    {
        if (status.Kind == GuardModelDownloadStatusKind.Attempting)
        {
            error.WriteLine($"Attempting GGUF download {status.AttemptNumber}/{status.AttemptCount} from {HuggingFaceGuardModelDownloader.BuildSourceLabel(status.DownloadUri)}.");
            return;
        }

        var nextSource = status.NextDownloadUri is null
            ? "next source"
            : HuggingFaceGuardModelDownloader.BuildSourceLabel(status.NextDownloadUri);
        error.WriteLine($"GGUF download failed at {HuggingFaceGuardModelDownloader.BuildSourceLabel(status.DownloadUri)}: {status.Error?.Message}. Retrying with {nextSource}.");
    }

    private static void WriteOutput(string format, IReadOnlyList<CliScanResult> results, TextWriter output)
    {
        if (string.Equals(format, "json", StringComparison.OrdinalIgnoreCase))
        {
            output.WriteLine(JsonSerializer.Serialize(new CliScanReport(results), JsonOptions));
            return;
        }

        foreach (var result in results)
        {
            output.WriteLine($"[{result.RiskLevel}] {result.Kind}: {result.Path}");
            output.WriteLine($"  degraded={result.IsDegradedMode}, block={result.ShouldBlock}");

            foreach (var evidence in result.Evidence)
            {
                output.WriteLine($"  - {evidence.Detector}: score={evidence.Score:F3} {evidence.Message}");
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

    internal sealed record CliScanOptions(
        bool EnableAll,
        bool EnableVirusTotal,
        bool EnableGguf,
        bool EnableSemantic,
        bool EnableYara,
        bool EnableRules,
        bool EnableRegex,
        string VirusTotalApiKey)
    {
        public static CliScanOptions Disabled { get; } = new(false, false, false, false, false, false, false, string.Empty);
    }

    internal sealed record CliScanResult(
        string Path,
        string Kind,
        RiskLevel RiskLevel,
        bool IsDegradedMode,
        bool ShouldBlock,
        IReadOnlyList<SkillRiskEvidence> Evidence);

    private sealed class CliSettingsStore : ISettingsYamlStore
    {
        private readonly ISettingsYamlStore inner;
        private readonly ModelPathsConfig modelPaths;
        private readonly ScanningConfig scanningConfig;

        public CliSettingsStore(ISettingsYamlStore inner, ModelPathsConfig modelPaths, ScanningConfig scanningConfig)
        {
            this.inner = inner;
            this.modelPaths = modelPaths;
            this.scanningConfig = scanningConfig;
        }

        public string FilePath => inner.FilePath;

        public IReadOnlyList<Philche.Core.Discovery.KnownAgentCatalogEntry> LoadCatalog() => inner.LoadCatalog();

        public ModelPathsConfig LoadModelPaths() => modelPaths;

        public ScanningConfig LoadScanningConfig() => scanningConfig;

        public SchedulerConfig LoadSchedulerConfig() => inner.LoadSchedulerConfig();

        public ShellContextMenuConfig LoadShellContextMenuConfig() => inner.LoadShellContextMenuConfig();

        public void SaveModelPaths(ModelPathsConfig modelPaths) => inner.SaveModelPaths(modelPaths);

        public void SaveScanningConfig(ScanningConfig scanningConfig) => inner.SaveScanningConfig(scanningConfig);

        public void SaveSchedulerConfig(SchedulerConfig schedulerConfig) => inner.SaveSchedulerConfig(schedulerConfig);

        public void SaveShellContextMenuConfig(ShellContextMenuConfig shellContextMenuConfig) => inner.SaveShellContextMenuConfig(shellContextMenuConfig);

        public void SaveCatalog(IReadOnlyList<Philche.Core.Discovery.KnownAgentCatalogEntry> entries) => inner.SaveCatalog(entries);
    }
}
