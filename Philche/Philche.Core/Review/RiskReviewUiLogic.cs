using Philche.Core.Domain.Models;

namespace Philche.Core.Review;

public static class RiskReviewUiLogic
{
    public static string ResolveRiskLevel(Finding finding)
    {
        if (finding.SkillsRiskLevel.HasValue)
        {
            return finding.SkillsRiskLevel.Value.ToString().ToUpperInvariant();
        }

        var severity = finding.Severity?.Trim().ToUpperInvariant();
        return severity switch
        {
            "CRITICAL" => "HIGH",
            "HIGH" => "HIGH",
            "MEDIUM" => "MEDIUM",
            _ => "LOW",
        };
    }

    public static string ResolveGuidanceKey(Finding finding)
    {
        var risk = ResolveRiskLevel(finding);
        return risk switch
        {
            "HIGH" => "guidance.high",
            "MEDIUM" => "guidance.medium",
            _ => "guidance.low",
        };
    }

    public static bool HasAnySelectedTargets(IEnumerable<bool> selectedStates)
    {
        return selectedStates.Any(isSelected => isSelected);
    }

    public static string BuildNewTargetNotification(
        IReadOnlyList<string> pendingTargetDisplayNames,
        Func<string, string> localizedText)
    {
        if (pendingTargetDisplayNames.Count == 0)
        {
            return localizedText("targets.notification.none");
        }

        if (pendingTargetDisplayNames.Count == 1)
        {
            return string.Format(localizedText("targets.notification.single"), pendingTargetDisplayNames[0]);
        }

        var names = string.Join(", ", pendingTargetDisplayNames.Take(5));
        return string.Format(localizedText("targets.notification.multiple"), pendingTargetDisplayNames.Count, names);
    }
}
