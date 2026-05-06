using Philche.Core.Domain.Models;

namespace Philche.Core.Data.Repositories;

public interface IScanRunRepository
{
    Task UpsertAsync(ScanRun run, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ScanRun>> ListRecentAsync(int limit, CancellationToken cancellationToken = default);
}
