using System.Text;
using LLama;
using LLama.Common;
using Philche.Core.Config;

namespace Philche.Core.SkillsRisk;

public sealed class LlamaGuardClassifier : IGuardModelClassifier
{
    private readonly IModelProvider? modelProvider;

    private static readonly string[] RiskTerms =
    [
        "exfiltrate",
        "steal",
        "bypass",
        "jailbreak",
        "api key",
        "password",
        "private key",
        "session cookie",
        "credit card"
    ];

    private const string SafetyCategories = """
        S2: Non-Violent Crimes.
        S7: Privacy.
        S14: Code Interpreter Abuse.
        """;

    public bool IsDegraded { get; private set; }

    public LlamaGuardClassifier(IModelProvider? modelProvider = null)
    {
        this.modelProvider = modelProvider;
    }

    public async Task<double> ScoreAsync(SkillEvaluationInput input, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(input.Content))
        {
            return 0.0;
        }

        if (modelProvider is null || !modelProvider.IsAvailable)
        {
            IsDegraded = true;
            return ScoreWithKeywordStub(input.Content);
        }

        var weights = modelProvider.GetWeights();
        if (weights is null)
        {
            IsDegraded = true;
            return ScoreWithKeywordStub(input.Content);
        }

        try
        {
            return await ScoreWithModelAsync(weights, input.Content, cancellationToken);
        }
        catch
        {
            IsDegraded = true;
            return ScoreWithKeywordStub(input.Content);
        }
    }

    private static async Task<double> ScoreWithModelAsync(LLamaWeights weights, string content, CancellationToken cancellationToken)
    {
        var contextParams = new ModelParams("_")
        {
            ContextSize = 2048,
        };

        var executor = new StatelessExecutor(weights, contextParams);

        var prompt = $"""
            <|begin_of_text|><|start_header_id|>user<|end_header_id|>

            Task: Check if there is unsafe content in 'Agent' messages in the following conversation according to our safety policy with the below categories.

            <BEGIN UNSAFE CONTENT CATEGORIES>
            {SafetyCategories}
            <END UNSAFE CONTENT CATEGORIES>

            <BEGIN CONVERSATION>

            User: Analyze this agent skill.
            Agent: {content}

            <END CONVERSATION>

            Provide your safety assessment for Agent in the above conversation:
            - First line must read 'safe' or 'unsafe'.
            - If unsafe, a second line must include a comma-separated list of violated categories.<|eot_id|><|start_header_id|>assistant<|end_header_id|>
            """;

        var inferenceParams = new InferenceParams
        {
            MaxTokens = 32,
            AntiPrompts = ["<|eot_id|>"],
            SamplingPipeline = new LLama.Sampling.DefaultSamplingPipeline
            {
                Temperature = 0.0f,
            },
        };

        var result = new StringBuilder();
        await foreach (var token in executor.InferAsync(prompt, inferenceParams, cancellationToken))
        {
            result.Append(token);
        }

        return ParseGuardOutput(result.ToString());
    }

    internal static double ParseGuardOutput(string output)
    {
        var trimmed = output.Trim();

        if (trimmed.StartsWith("safe", StringComparison.OrdinalIgnoreCase) &&
            !trimmed.StartsWith("unsafe", StringComparison.OrdinalIgnoreCase))
        {
            return 0.0;
        }

        if (!trimmed.StartsWith("unsafe", StringComparison.OrdinalIgnoreCase))
        {
            return 0.0;
        }

        var lines = trimmed.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        if (lines.Length < 2)
        {
            return 0.6;
        }

        var categories = lines[1].Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        return Math.Min(1.0, categories.Length * 0.25);
    }

    private static double ScoreWithKeywordStub(string content)
    {
        var lower = content.ToLowerInvariant();
        var hits = RiskTerms.Count(lower.Contains);
        return Math.Min(1.0, hits * 0.18);
    }
}
