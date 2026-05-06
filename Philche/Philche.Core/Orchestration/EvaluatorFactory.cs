using Philche.Core.Config;
using Philche.Core.Correlation;
using Philche.Core.SkillsRisk;

namespace Philche.Core.Orchestration;

public sealed class EvaluatorFactory
{
    private readonly ISettingsYamlStore settingsYamlStore;
    private readonly Action<string> warningLogger;

    public EvaluatorFactory(ISettingsYamlStore settingsYamlStore, Action<string>? warningLogger = null)
    {
        this.settingsYamlStore = settingsYamlStore;
        this.warningLogger = warningLogger ?? (static message => Console.Error.WriteLine(message));
    }

    public EvaluatorSnapshot Build()
    {
        var modelPaths = settingsYamlStore.LoadModelPaths();
        var scanning = settingsYamlStore.LoadScanningConfig();
        var llmIntentEnabled = scanning.EnableGuardModelScan && scanning.EnableLlmIntentRecognition;
        var rulesStageEnabled = scanning.EnableMaliciousWordGroupList || scanning.EnableInvisibleCharacterDetection;

        if (!scanning.EnableYaraScan &&
            !llmIntentEnabled &&
            !rulesStageEnabled &&
            !scanning.EnableRegexScan &&
            !scanning.EnableCveCorrelation)
        {
            warningLogger("[EvaluatorFactory] all scan methods are disabled in settings.");
        }

        var guardProvider = !llmIntentEnabled || string.IsNullOrWhiteSpace(modelPaths.GuardModelPath)
            ? null
            : new GgufModelProvider(modelPaths.GuardModelPath);

        var cveProvider = !scanning.EnableCveCorrelation || string.IsNullOrWhiteSpace(modelPaths.CveSummaryModelPath)
            ? null
            : new GgufModelProvider(modelPaths.CveSummaryModelPath);

        var flags = new RuntimeFeatureFlags
        {
            EnableSemanticRiskStage = false,
            EnableGuardRiskStage = llmIntentEnabled,
            EnableMaliciousWordGroupRiskStage = scanning.EnableMaliciousWordGroupList,
            EnableInvisibleCharacterDetectionStage = scanning.EnableInvisibleCharacterDetection,
            EnableRegexRiskStage = scanning.EnableRegexScan,
            EnableYaraCodeScanning = scanning.EnableYaraScan,
            EnableJiebaPosFiltering = false,
        };

        var guardClassifier = new LlamaGuardClassifier(guardProvider);
        var cveSimplifier = new LlamaCveSummarySimplifier(cveProvider);

        var evaluator = new SkillRiskEvaluator(
            new RuleDetector(),
            new StubSemanticDetector(),
            guardClassifier,
            new NonBlockingRiskActionPolicy(),
            flags,
            new PromptPreprocessor(),
            new YaraCodeScanner());

        return new EvaluatorSnapshot(
            evaluator,
            guardClassifier,
            cveSimplifier,
            guardProvider,
            cveProvider,
            scanning.EnableYaraScan,
                llmIntentEnabled,
            scanning.EnableRegexScan,
            scanning.EnableCveCorrelation);
    }

    private sealed class StubSemanticDetector : ISemanticSimilarityDetector
    {
        public Task<double> ScoreAsync(SkillEvaluationInput input, CancellationToken ct = default)
            => Task.FromResult(0.0);
    }
}

public sealed class EvaluatorSnapshot : IDisposable
{
    private bool disposed;

    public SkillRiskEvaluator Evaluator { get; }

    public IGuardModelClassifier GuardClassifier { get; }

    public ICveSummarySimplifier CveSummarySimplifier { get; }

    public GgufModelProvider? GuardProvider { get; }

    public GgufModelProvider? CveProvider { get; }

    public bool GuardDegraded => GuardProvider is null || !GuardProvider.IsAvailable;

    public bool CveDegraded => CveProvider is null || !CveProvider.IsAvailable;

    public bool YaraScanEnabled { get; }

    public bool GuardModelScanEnabled { get; }

    public bool RegexScanEnabled { get; }

    public bool CveCorrelationEnabled { get; }

    public bool IsDisposed { get; private set; }

    public EvaluatorSnapshot(
        SkillRiskEvaluator evaluator,
        IGuardModelClassifier guardClassifier,
        ICveSummarySimplifier cveSummarySimplifier,
        GgufModelProvider? guardProvider,
        GgufModelProvider? cveProvider,
        bool yaraScanEnabled,
        bool guardModelScanEnabled,
        bool regexScanEnabled,
        bool cveCorrelationEnabled)
    {
        Evaluator = evaluator;
        GuardClassifier = guardClassifier;
        CveSummarySimplifier = cveSummarySimplifier;
        GuardProvider = guardProvider;
        CveProvider = cveProvider;
        YaraScanEnabled = yaraScanEnabled;
        GuardModelScanEnabled = guardModelScanEnabled;
        RegexScanEnabled = regexScanEnabled;
        CveCorrelationEnabled = cveCorrelationEnabled;
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        GuardProvider?.Dispose();
        CveProvider?.Dispose();
        IsDisposed = true;
    }
}
