using Philche.Core.Domain.Enums;
using Philche.Core.Config;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Philche.Core.Data.Repositories;
using Philche.Core.Domain.Models;

namespace Philche.Core.SkillsRisk;

public sealed class SkillRiskEvaluator
{
    private readonly RuleDetector ruleDetector;
    private readonly ISemanticSimilarityDetector semanticDetector;
    private readonly IGuardModelClassifier guardClassifier;
    private readonly NonBlockingRiskActionPolicy actionPolicy;
    private readonly RuntimeFeatureFlags featureFlags;
    private readonly PromptPreprocessor promptPreprocessor;
    private readonly YaraCodeScanner? yaraCodeScanner;

    public SkillRiskEvaluator(
        RuleDetector ruleDetector,
        ISemanticSimilarityDetector semanticDetector,
        IGuardModelClassifier guardClassifier,
        NonBlockingRiskActionPolicy actionPolicy,
        RuntimeFeatureFlags? featureFlags = null,
        PromptPreprocessor? promptPreprocessor = null,
        YaraCodeScanner? yaraCodeScanner = null)
    {
        this.ruleDetector = ruleDetector;
        this.semanticDetector = semanticDetector;
        this.guardClassifier = guardClassifier;
        this.actionPolicy = actionPolicy;
        this.featureFlags = featureFlags ?? new RuntimeFeatureFlags();
        this.promptPreprocessor = promptPreprocessor ?? new PromptPreprocessor();
        this.yaraCodeScanner = yaraCodeScanner;
    }

    public async Task<SkillRiskResult> EvaluateAsync(SkillEvaluationInput input, CancellationToken cancellationToken = default)
    {
        var evidence = new List<SkillRiskEvidence>();
        var degradedMode = false;

        if (input.IsCode)
        {
            if (!featureFlags.EnableYaraCodeScanning)
            {
                evidence.Add(new SkillRiskEvidence("yara", 0, "Detector disabled by feature flag"));
                return new SkillRiskResult(RiskLevel.Low, false, false, evidence);
            }

            if (yaraCodeScanner is null)
            {
                evidence.Add(new SkillRiskEvidence("yara", 0, "Detector unavailable"));
                return new SkillRiskResult(RiskLevel.Low, true, false, evidence);
            }

            var yaraEvidence = yaraCodeScanner.Scan(input.Content);
            evidence.AddRange(yaraEvidence);

            var yaraScore = Math.Min(1.0, yaraEvidence.Sum(static item => item.Score));
            var codeLevel = yaraScore switch
            {
                >= 0.75 => RiskLevel.High,
                >= 0.35 => RiskLevel.Medium,
                _ => RiskLevel.Low,
            };

            return new SkillRiskResult(codeLevel, false, actionPolicy.ShouldBlock(codeLevel), evidence);
        }

        var rulesStageEnabled = featureFlags.EnableMaliciousWordGroupRiskStage || featureFlags.EnableInvisibleCharacterDetectionStage;
        double rulesAndCharsScore;
        if (!rulesStageEnabled)
        {
            rulesAndCharsScore = 0;
            evidence.Add(new SkillRiskEvidence("rules", 0, "Detector disabled by feature flag"));
        }
        else
        {
            rulesAndCharsScore = ruleDetector.ScoreRulesAndChars(
                input,
                featureFlags.EnableMaliciousWordGroupRiskStage,
                featureFlags.EnableInvisibleCharacterDetectionStage);
            evidence.Add(new SkillRiskEvidence("rules", rulesAndCharsScore, BuildRulesEvidenceMessage()));
        }

        double regexScore;
        if (!featureFlags.EnableRegexRiskStage)
        {
            regexScore = 0;
            evidence.Add(new SkillRiskEvidence("regex", 0, "Detector disabled by feature flag"));
        }
        else
        {
            regexScore = ruleDetector.ScoreRegexSignals(input);
            evidence.Add(new SkillRiskEvidence("regex", regexScore, "Regex pattern signal"));
        }

        var cleanedForModels = promptPreprocessor.PreprocessForSemanticAndGuard(
            input.Content,
            featureFlags.EnableJiebaPosFiltering);
        var modelInput = input with { Content = cleanedForModels };

        double semanticScore;
        if (!featureFlags.EnableSemanticRiskStage)
        {
            semanticScore = 0;
            evidence.Add(new SkillRiskEvidence("semantic", 0, "Detector disabled by feature flag"));
        }
        else
        {
            try
            {
                semanticScore = await semanticDetector.ScoreAsync(modelInput, cancellationToken);
                evidence.Add(new SkillRiskEvidence("semantic", semanticScore, "Embedding similarity signal"));
            }
            catch
            {
                semanticScore = 0;
                degradedMode = true;
                evidence.Add(new SkillRiskEvidence("semantic", 0, "Detector unavailable"));
            }
        }

        double guardScore;
        if (!featureFlags.EnableGuardRiskStage)
        {
            guardScore = 0;
            evidence.Add(new SkillRiskEvidence("guard", 0, "Detector disabled by feature flag"));
        }
        else
        {
            try
            {
                guardScore = await guardClassifier.ScoreAsync(modelInput, cancellationToken);
                evidence.Add(new SkillRiskEvidence("guard", guardScore, "Guard model classification"));
            }
            catch
            {
                guardScore = 0;
                degradedMode = true;
                evidence.Add(new SkillRiskEvidence("guard", 0, "Detector unavailable"));
            }
        }

        var combinedScore = (rulesAndCharsScore * 0.35) + (regexScore * 0.2) + (semanticScore * 0.25) + (guardScore * 0.2);

        var hasHighSignal = rulesAndCharsScore >= 0.45 || regexScore >= 0.45 || semanticScore >= 0.85 || guardScore >= 0.85;
        var level = hasHighSignal
            ? RiskLevel.High
            : combinedScore switch
            {
                >= 0.40 => RiskLevel.Medium,
                _ => RiskLevel.Low,
            };

        return new SkillRiskResult(level, degradedMode, actionPolicy.ShouldBlock(level), evidence);
    }

    private string BuildRulesEvidenceMessage()
    {
        if (featureFlags.EnableMaliciousWordGroupRiskStage && featureFlags.EnableInvisibleCharacterDetectionStage)
        {
            return "Malicious-word and invisible-character signal";
        }

        if (featureFlags.EnableMaliciousWordGroupRiskStage)
        {
            return "Malicious-word signal";
        }

        return "Invisible-character signal";
    }

    public static string ComputePromptContentHash(string content)
    {
        var bytes = Encoding.UTF8.GetBytes(content);
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash);
    }

    public async Task<SkillRiskResult> EvaluatePromptWithCacheAsync(
        string skillPath,
        SkillEvaluationInput input,
        IScanCacheRepository? scanCacheRepository,
        bool bypassCache = false,
        CancellationToken cancellationToken = default)
    {
        if (input.IsCode || scanCacheRepository is null)
        {
            return await EvaluateAsync(input, cancellationToken);
        }

        var contentHash = ComputePromptContentHash(input.Content);

        if (!bypassCache)
        {
            var cached = await scanCacheRepository.GetAsync(skillPath, ScanCacheTypes.Prompt, cancellationToken);
            if (cached is not null && string.Equals(cached.ContentHash, contentHash, StringComparison.Ordinal))
            {
                var cachedResult = DeserializePromptResult(cached.FindingsJson);
                if (cachedResult is not null)
                {
                    return cachedResult;
                }
            }
        }

        var evaluated = await EvaluateAsync(input, cancellationToken);

        await scanCacheRepository.UpsertAsync(
            new ScanCacheEntry
            {
                SkillPath = skillPath,
                ScanType = ScanCacheTypes.Prompt,
                ContentHash = contentHash,
                LastScannedAt = DateTimeOffset.UtcNow,
                FindingsJson = SerializePromptResult(evaluated),
            },
            cancellationToken);

        return evaluated;
    }

    private static string SerializePromptResult(SkillRiskResult result)
    {
        var cacheModel = new PromptCacheModel
        {
            RiskLevel = result.RiskLevel,
            IsDegradedMode = result.IsDegradedMode,
            ShouldBlock = result.ShouldBlock,
            Evidence = result.Evidence.ToList(),
        };

        return JsonSerializer.Serialize(cacheModel);
    }

    private static SkillRiskResult? DeserializePromptResult(string json)
    {
        try
        {
            var cacheModel = JsonSerializer.Deserialize<PromptCacheModel>(json);
            if (cacheModel is null)
            {
                return null;
            }

            return new SkillRiskResult(
                cacheModel.RiskLevel,
                cacheModel.IsDegradedMode,
                cacheModel.ShouldBlock,
                cacheModel.Evidence);
        }
        catch
        {
            return null;
        }
    }

    private sealed class PromptCacheModel
    {
        public RiskLevel RiskLevel { get; init; }
        public bool IsDegradedMode { get; init; }
        public bool ShouldBlock { get; init; }
        public List<SkillRiskEvidence> Evidence { get; init; } = [];
    }
}
