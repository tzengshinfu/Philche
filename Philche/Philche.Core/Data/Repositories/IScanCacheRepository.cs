using Philche.Core.Domain.Models;

namespace Philche.Core.Data.Repositories;

public interface IScanCacheRepository
{
    Task<ScanCacheEntry?> GetAsync(string skillPath, string scanType, CancellationToken cancellationToken = default);
    Task UpsertAsync(ScanCacheEntry entry, CancellationToken cancellationToken = default);
    Task DeleteByScanTypeAsync(string scanType, CancellationToken cancellationToken = default);
    Task ClearAllAsync(CancellationToken cancellationToken = default);
}
