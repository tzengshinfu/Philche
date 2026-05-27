using Philche.Core.Config;

namespace Philche.Core.Test;

public sealed class RuntimeFeatureFlagsTests
{
    [Fact(DisplayName = "執行期功能旗標測試：Defaults Should Disable Automatic Known Agent Catalog Refresh")]
    public void Defaults_ShouldDisableAutomaticKnownAgentCatalogRefresh()
    {
        var flags = new RuntimeFeatureFlags();
        Assert.False(flags.EnableAutomaticKnownAgentCatalogRefresh);
    }

    [Fact(DisplayName = "執行期功能旗標測試：Manual Policy Should Allow Explicit Override For Non Mvp Scenarios")]
    public void ManualPolicy_ShouldAllowExplicitOverrideForNonMvpScenarios()
    {
        var flags = new RuntimeFeatureFlags
        {
            EnableAutomaticKnownAgentCatalogRefresh = true,
        };

        Assert.True(flags.EnableAutomaticKnownAgentCatalogRefresh);
    }

    [Fact(DisplayName = "執行期功能旗標測試：Defaults Should Enable Guard Regex And Yara Stages")]
    public void Defaults_ShouldEnableGuardRegexAndYaraStages()
    {
        var flags = new RuntimeFeatureFlags();

        Assert.True(flags.EnableGuardRiskStage);
        Assert.True(flags.EnableMaliciousWordGroupRiskStage);
        Assert.True(flags.EnableInvisibleCharacterDetectionStage);
        Assert.True(flags.EnableRegexRiskStage);
        Assert.True(flags.EnableYaraCodeScanning);
        Assert.False(flags.EnableVirusTotalSkillUrlScan);
        Assert.False(flags.EnableVirusTotalScriptUrlScan);
    }
}


