namespace Philche.Core.Config;

public interface IModelDownloader
{
    Task<string> DownloadAsync(
        Uri sourceUrl,
        string targetDir,
        string expectedSha256,
        IProgress<(long downloaded, long total)>? progress = null,
        CancellationToken cancellationToken = default);
}
