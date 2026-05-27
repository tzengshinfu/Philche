using Philche.Core.Domain.Enums;
using Philche.Core.Domain.Models;
using Philche.Core.Review;

namespace Philche.Core.Test;

public sealed class RiskReviewUiLogicTests
{
    [Fact(DisplayName = "風險檢視 UI 邏輯測試：Resolve Risk Level Uses Skills Risk Level When Present")]
    public void ResolveRiskLevel_UsesSkillsRiskLevelWhenPresent()
    {
        var finding = new Finding
        {
            Id = "f-1",
            CanonicalVulnerabilityId = "CVE-2026-0001",
            TargetId = "host:localhost",
            FindingType = FindingType.Skills,
            Severity = "CRITICAL",
            SkillsRiskLevel = RiskLevel.Medium,
            Provenance = [],
        };

        var level = RiskReviewUiLogic.ResolveRiskLevel(finding);
        Assert.Equal("MEDIUM", level);
    }

    [Theory(DisplayName = "風險檢視 UI 邏輯測試：Resolve Risk Level Maps Severity To Normalized Risk")]
    [InlineData("CRITICAL", "HIGH")]
    [InlineData("HIGH", "HIGH")]
    [InlineData("MEDIUM", "MEDIUM")]
    [InlineData("LOW", "LOW")]
    [InlineData(null, "LOW")]
    public void ResolveRiskLevel_MapsSeverityToNormalizedRisk(string? severity, string expected)
    {
        var finding = new Finding
        {
            Id = "f-2",
            CanonicalVulnerabilityId = "CVE-2026-0002",
            TargetId = "wsl:ubuntu",
            FindingType = FindingType.Cve,
            Severity = severity,
            SkillsRiskLevel = null,
            Provenance = [],
        };

        var level = RiskReviewUiLogic.ResolveRiskLevel(finding);
        Assert.Equal(expected, level);
    }

    [Fact(DisplayName = "風險檢視 UI 邏輯測試：Resolve Guidance Key Returns Expected Key For Risk")]
    public void ResolveGuidanceKey_ReturnsExpectedKeyForRisk()
    {
        var highFinding = new Finding
        {
            Id = "f-3",
            CanonicalVulnerabilityId = "CVE-2026-0003",
            TargetId = "host",
            FindingType = FindingType.Cve,
            Severity = "HIGH",
            Provenance = [],
        };

        var mediumFinding = highFinding with
        {
            Id = "f-4",
            Severity = "MEDIUM",
        };

        var lowFinding = highFinding with
        {
            Id = "f-5",
            Severity = "LOW",
        };

        Assert.Equal("guidance.high", RiskReviewUiLogic.ResolveGuidanceKey(highFinding));
        Assert.Equal("guidance.medium", RiskReviewUiLogic.ResolveGuidanceKey(mediumFinding));
        Assert.Equal("guidance.low", RiskReviewUiLogic.ResolveGuidanceKey(lowFinding));
    }

    [Fact(DisplayName = "風險檢視 UI 邏輯測試：Build New Target Notification Formats None Single And Multiple")]
    public void BuildNewTargetNotification_FormatsNoneSingleAndMultiple()
    {
        var dictionary = new Dictionary<string, string>
        {
            ["targets.notification.none"] = "none",
            ["targets.notification.single"] = "single {0}",
            ["targets.notification.multiple"] = "multi {0}: {1}",
        };

        string Resolve(string key) => dictionary[key];

        var none = RiskReviewUiLogic.BuildNewTargetNotification([], Resolve);
        var single = RiskReviewUiLogic.BuildNewTargetNotification(["Ubuntu"], Resolve);
        var multiple = RiskReviewUiLogic.BuildNewTargetNotification(["Ubuntu", "Debian", "api", "mysql", "redis", "extra"], Resolve);

        Assert.Equal("none", none);
        Assert.Equal("single Ubuntu", single);
        Assert.Equal("multi 6: Ubuntu, Debian, api, mysql, redis", multiple);
    }

    [Fact(DisplayName = "風險檢視 UI 邏輯測試：Has Any Selected Targets Returns Expected State")]
    public void HasAnySelectedTargets_ReturnsExpectedState()
    {
        Assert.False(RiskReviewUiLogic.HasAnySelectedTargets([false, false, false]));
        Assert.True(RiskReviewUiLogic.HasAnySelectedTargets([false, true, false]));
    }
}


