namespace Philche.Core.Discovery;

public interface IWslDistroProvider
{
    Task<IReadOnlyList<string>> ListDistrosAsync(CancellationToken cancellationToken = default);
}
