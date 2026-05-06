namespace Philche.Core.Config;

public sealed record RuntimeFeatureFlags
{
    public bool EnableWslScanning { get; init; } = true;
    public bool EnableSemanticRiskStage { get; init; } = true;
    public bool EnableGuardRiskStage { get; init; } = true;
    public bool EnableMaliciousWordGroupRiskStage { get; init; } = true;
    public bool EnableInvisibleCharacterDetectionStage { get; init; } = true;
    public bool EnableRegexRiskStage { get; init; } = true;
    public bool EnableYaraCodeScanning { get; init; } = true;
    public bool EnableJiebaPosFiltering { get; init; } = true;
    public bool EnableAutomaticKnownAgentCatalogRefresh { get; init; } = false;
}
