using System.Text;
using System.Text.RegularExpressions;
using LLama;
using LLama.Common;
using Philche.Core.Config;

namespace Philche.Core.Correlation;

public sealed class LlamaCveSummarySimplifier : ICveSummarySimplifier
{
    private readonly IModelProvider? modelProvider;

    public bool IsDegraded { get; private set; }

    public LlamaCveSummarySimplifier(IModelProvider? modelProvider = null)
    {
        this.modelProvider = modelProvider;
    }

    public async Task<string?> SimplifyAsync(string canonicalId, string? originalSummary, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(originalSummary))
        {
            return null;
        }

        if (modelProvider is null || !modelProvider.IsAvailable)
        {
            IsDegraded = true;
            return SimplifyWithRegexFallback(canonicalId, originalSummary);
        }

        var weights = modelProvider.GetWeights();
        if (weights is null)
        {
            IsDegraded = true;
            return SimplifyWithRegexFallback(canonicalId, originalSummary);
        }

        try
        {
            return await SimplifyWithModelAsync(weights, canonicalId, originalSummary, cancellationToken);
        }
        catch
        {
            IsDegraded = true;
            return SimplifyWithRegexFallback(canonicalId, originalSummary);
        }
    }

    private static async Task<string?> SimplifyWithModelAsync(
        LLamaWeights weights,
        string canonicalId,
        string originalSummary,
        CancellationToken cancellationToken)
    {
        var contextParams = new ModelParams("_")
        {
            ContextSize = 1024,
        };

        var executor = new StatelessExecutor(weights, contextParams);

        var prompt = $"""
            <|begin_of_text|><|start_header_id|>system<|end_header_id|>
            You are a vulnerability analyst. Simplify the following CVE description into one concise sentence in English. Include the vulnerability type and affected component. Do not include CVE IDs.
            <|eot_id|><|start_header_id|>user<|end_header_id|>
            {originalSummary}
            <|eot_id|><|start_header_id|>assistant<|end_header_id|>
            """;

        var inferenceParams = new InferenceParams
        {
            MaxTokens = 128,
            AntiPrompts = ["<|eot_id|>"],
            SamplingPipeline = new LLama.Sampling.DefaultSamplingPipeline
            {
                Temperature = 0.1f,
                TopP = 0.9f,
            },
        };

        var result = new StringBuilder();
        await foreach (var token in executor.InferAsync(prompt, inferenceParams, cancellationToken))
        {
            result.Append(token);
        }

        var simplified = result.ToString().Trim();
        return string.IsNullOrWhiteSpace(simplified)
            ? SimplifyWithRegexFallback(canonicalId, originalSummary)
            : $"{canonicalId}: {simplified}";
    }

    internal static string? SimplifyWithRegexFallback(string canonicalId, string originalSummary)
    {
        var compact = Regex.Replace(originalSummary, "\\s+", " ").Trim();
        // Match a sentence-ending period (followed by space+uppercase or end-of-string),
        // which avoids splitting on periods inside version numbers like "2.9.12".
        var match = Regex.Match(compact, @"\.(?=\s+[A-Z]|\s*$)|[。;；]");
        var core = match.Success ? compact[..match.Index].Trim() : compact;
        return $"{canonicalId}: {core}";
    }
}
