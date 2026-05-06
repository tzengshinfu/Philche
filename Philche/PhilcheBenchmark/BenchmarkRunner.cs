using System.Diagnostics;
using Philche.Core.Config;
using Philche.Core.SkillsRisk;

namespace PhilcheBenchmark;

internal sealed class BenchmarkRunner
{
    private const uint ContextSize = 2048;

    public async Task<BenchmarkRunResult> RunAsync(string modelPath, IReadOnlyList<BenchmarkTestCase> testCases, CancellationToken cancellationToken)
    {
        if (testCases.Count == 0)
        {
            throw new InvalidOperationException("At least one test case is required.");
        }

        using var provider = new GgufModelProvider(modelPath, ContextSize);
        if (!provider.IsAvailable)
        {
            throw new FileNotFoundException($"Model file not found: {modelPath}", modelPath);
        }

        var weights = provider.GetWeights();
        if (weights is null)
        {
            throw new InvalidOperationException(provider.LastLoadError?.Message ?? "Model weights could not be loaded.");
        }

        var classifier = new LlamaGuardClassifier(provider);
        var warmupDuration = await MeasureAsync(classifier, new BenchmarkTestCase(
            Name: "warmup",
            RelativePath: "warmup.md",
            Language: "n/a",
            ExpectedUnsafe: false,
            Content: "Summarize this setup guide in one sentence."), cancellationToken);

        if (classifier.IsDegraded)
        {
            throw new InvalidOperationException("Warmup fell back to degraded keyword scoring; benchmark requires live model inference.");
        }

        var results = new List<BenchmarkCaseResult>(testCases.Count);
        foreach (var testCase in testCases)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var stopwatch = Stopwatch.StartNew();
            var score = await classifier.ScoreAsync(
                new SkillEvaluationInput(testCase.Content, testCase.RelativePath, false),
                cancellationToken);
            stopwatch.Stop();

            if (classifier.IsDegraded)
            {
                throw new InvalidOperationException($"Inference degraded while processing {testCase.RelativePath}; benchmark requires live model inference.");
            }

            results.Add(new BenchmarkCaseResult(
                TestCase: testCase,
                Score: score,
                IsUnsafe: score > 0.0,
                Duration: stopwatch.Elapsed));
        }

        return new BenchmarkRunResult(
            ModelPath: modelPath,
            ContextSize: ContextSize,
            WarmupDuration: warmupDuration,
            Hardware: HardwareInfoProvider.GetCurrent(),
            Results: results);
    }

    private static async Task<TimeSpan> MeasureAsync(LlamaGuardClassifier classifier, BenchmarkTestCase testCase, CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        _ = await classifier.ScoreAsync(
            new SkillEvaluationInput(testCase.Content, testCase.RelativePath, false),
            cancellationToken);
        stopwatch.Stop();
        return stopwatch.Elapsed;
    }
}
