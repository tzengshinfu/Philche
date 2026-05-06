namespace Philche.Core.Correlation;

public interface ICveSummarySimplifier
{
    Task<string?> SimplifyAsync(string canonicalId, string? originalSummary, CancellationToken cancellationToken = default);
}
