namespace Philche.Core.Config;

public sealed class HuggingFaceGuardModelDownloader
{
    private readonly IModelDownloader modelDownloader;

    public HuggingFaceGuardModelDownloader(IModelDownloader modelDownloader)
    {
        this.modelDownloader = modelDownloader;
    }

    public async Task<string> DownloadAsync(
        string modelName,
        string targetDir,
        IProgress<(long downloaded, long total)>? progress = null,
        Action<GuardModelDownloadStatus>? onStatus = null,
        CancellationToken cancellationToken = default)
    {
        var normalizedModelName = HuggingFaceGuardModelLocator.NormalizeModelName(modelName);
        var downloadUris = HuggingFaceGuardModelLocator.BuildCandidateDownloadUris(normalizedModelName);

        Exception? lastError = null;

        for (var index = 0; index < downloadUris.Count; index++)
        {
            var downloadUri = downloadUris[index];
            onStatus?.Invoke(new GuardModelDownloadStatus(
                GuardModelDownloadStatusKind.Attempting,
                normalizedModelName,
                downloadUri,
                index + 1,
                downloadUris.Count,
                null,
                null));

            try
            {
                return await modelDownloader.DownloadAsync(
                    downloadUri,
                    targetDir,
                    string.Empty,
                    progress,
                    cancellationToken);
            }
            catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException)
            {
                lastError = ex;

                if (index < downloadUris.Count - 1)
                {
                    onStatus?.Invoke(new GuardModelDownloadStatus(
                        GuardModelDownloadStatusKind.Fallback,
                        normalizedModelName,
                        downloadUri,
                        index + 1,
                        downloadUris.Count,
                        ex,
                        downloadUris[index + 1]));
                }
            }
        }

        throw lastError ?? new InvalidOperationException("No downloadable model source was available.");
    }

    public static string BuildSourceLabel(Uri downloadUri)
    {
        if (downloadUri.Segments.Length >= 3)
        {
            return $"{downloadUri.Segments[1].TrimEnd('/')}/{downloadUri.Segments[2].TrimEnd('/')}";
        }

        return downloadUri.Host;
    }
}

public enum GuardModelDownloadStatusKind
{
    Attempting,
    Fallback,
}

public sealed record GuardModelDownloadStatus(
    GuardModelDownloadStatusKind Kind,
    string ModelName,
    Uri DownloadUri,
    int AttemptNumber,
    int AttemptCount,
    Exception? Error,
    Uri? NextDownloadUri);