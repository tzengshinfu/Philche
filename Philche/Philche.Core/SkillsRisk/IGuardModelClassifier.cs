namespace Philche.Core.SkillsRisk;

public interface IGuardModelClassifier
{
    Task<double> ScoreAsync(SkillEvaluationInput input, CancellationToken cancellationToken = default);
}
