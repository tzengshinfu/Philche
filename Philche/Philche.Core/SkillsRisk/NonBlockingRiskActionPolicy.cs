using Philche.Core.Domain.Enums;

namespace Philche.Core.SkillsRisk;

public sealed class NonBlockingRiskActionPolicy
{
    public bool ShouldBlock(RiskLevel riskLevel) => false;
}
