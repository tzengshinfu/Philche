using System.Security.Cryptography;

namespace Philche.Core.Config;

public sealed class HttpModelDownloader : IModelDownloader
{
    private readonly HttpClient httpClient;

    public HttpModelDownloader(HttpClient? httpClient = null)
    {
        this.httpClient = httpClient ?? new HttpClient();
    }

    public async Task<string> DownloadAsync(
        Uri sourceUrl,
        string targetDir,
        string expectedSha256,
        IProgress<(long downloaded, long total)>? progress = null,
        CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(targetDir);

        var fileName = Path.GetFileName(sourceUrl.LocalPath);
        if (string.IsNullOrWhiteSpace(fileName))
        {
            fileName = "model.gguf";
        }

        var targetPath = Path.Combine(targetDir, fileName);
        var tempPath = targetPath + ".tmp";

        try
        {
            using var response = await httpClient.GetAsync(sourceUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            response.EnsureSuccessStatusCode();

            var total = response.Content.Headers.ContentLength ?? -1;
            await using var input = await response.Content.ReadAsStreamAsync(cancellationToken);
            {
                await using var output = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None);

                var buffer = new byte[81920];
                long downloaded = 0;
                int read;

                while ((read = await input.ReadAsync(buffer, cancellationToken)) > 0)
                {
                    await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                    downloaded += read;
                    progress?.Report((downloaded, total));
                }

                await output.FlushAsync(cancellationToken);
            }

            if (!string.IsNullOrWhiteSpace(expectedSha256))
            {
                string actualSha256;
                await using (var verifyStream = new FileStream(tempPath, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    var hash = await SHA256.HashDataAsync(verifyStream, cancellationToken);
                    actualSha256 = Convert.ToHexString(hash);
                }

                if (!actualSha256.Equals(expectedSha256, StringComparison.OrdinalIgnoreCase))
                {
                    File.Delete(tempPath);
                    throw new InvalidOperationException($"SHA256 mismatch. expected={expectedSha256}, actual={actualSha256}");
                }
            }

            File.Move(tempPath, targetPath, overwrite: true);
            return targetPath;
        }
        catch
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }

            throw;
        }
    }
}
