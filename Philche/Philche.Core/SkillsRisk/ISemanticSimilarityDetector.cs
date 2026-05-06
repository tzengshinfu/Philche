namespace Philche.Core.SkillsRisk;

public interface ISemanticSimilarityDetector
{
    Task<double> ScoreAsync(SkillEvaluationInput input, CancellationToken cancellationToken = default);
}
