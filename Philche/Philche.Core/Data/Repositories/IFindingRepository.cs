using Philche.Core.Domain.Models;

namespace Philche.Core.Data.Repositories;

public interface IFindingRepository
{
    Task UpsertAsync(Finding finding, CancellationToken cancellationToken = default);
    Task<Finding?> FindByIdAsync(string id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Finding>> ListByTargetAsync(string targetId, CancellationToken cancellationToken = default);
}
