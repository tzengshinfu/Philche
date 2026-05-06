using Philche.Core.Config;

namespace Philche.Core.Test;

public sealed class RuntimeFeatureFlagsTests
{
    [Fact]
    public void Defaults_ShouldDisableAutomaticKnownAgentCatalogRefresh()
    {
        var flags = new RuntimeFeatureFlags();
        Assert.False(flags.EnableAutomaticKnownAgentCatalogRefresh);
    }

    [Fact]
    public void ManualPolicy_ShouldAllowExplicitOverrideForNonMvpScenarios()
    {
        var flags = new RuntimeFeatureFlags
        {
            EnableAutomaticKnownAgentCatalogRefresh = true,
        };

        Assert.True(flags.EnableAutomaticKnownAgentCatalogRefresh);
    }

    [Fact]
    public void Defaults_ShouldEnableGuardRegexAndYaraStages()
    {
        var flags = new RuntimeFeatureFlags();

        Assert.True(flags.EnableGuardRiskStage);
        Assert.True(flags.EnableMaliciousWordGroupRiskStage);
        Assert.True(flags.EnableInvisibleCharacterDetectionStage);
        Assert.True(flags.EnableRegexRiskStage);
        Assert.True(flags.EnableYaraCodeScanning);
    }
}
