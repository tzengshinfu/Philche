using Philche.Core.Domain.Enums;
using Philche.Core.Domain.Models;

namespace Philche.Core.Data.Repositories;

public interface IScanTargetRepository
{
    Task UpsertAsync(ScanTarget target, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ScanTarget>> ListBySurfaceAsync(SurfaceType surfaceType, CancellationToken cancellationToken = default);
}
