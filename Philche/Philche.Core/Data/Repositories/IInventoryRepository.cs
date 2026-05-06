using Philche.Core.Domain.Models;

namespace Philche.Core.Data.Repositories;

public interface IInventoryRepository
{
    Task UpsertAsync(InventoryItem item, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<InventoryItem>> ListAsync(CancellationToken cancellationToken = default);
    Task<InventoryItem?> GetLatestByAgentKeyAsync(string agentKey, CancellationToken cancellationToken = default);
}
