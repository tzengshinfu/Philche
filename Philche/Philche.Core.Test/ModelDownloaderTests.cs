using System.Net;
using System.Net.Http;
using System.Text;
using Philche.Core.Config;

namespace Philche.Core.Test;

public sealed class ModelDownloaderTests
{
    [Fact(DisplayName = "模型下載測試：Download Async Succeeds When Sha Matches")]
    public async Task DownloadAsync_Succeeds_WhenShaMatches()
    {
        var payload = Encoding.UTF8.GetBytes("test-model-content");
        var expectedSha = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(payload));

        using var httpClient = new HttpClient(new StaticResponseHandler(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(payload),
        }));

        var downloader = new HttpModelDownloader(httpClient);
        var tempDir = Path.Combine(Path.GetTempPath(), $"philche-download-{Guid.NewGuid():N}");

        try
        {
            var progressEvents = 0;
            var progress = new Progress<(long downloaded, long total)>(_ => progressEvents++);

            var path = await downloader.DownloadAsync(
                new Uri("https://example.com/model.gguf"),
                tempDir,
                expectedSha,
                progress,
                CancellationToken.None);

            Assert.True(File.Exists(path));
            Assert.True(progressEvents > 0);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }
    }

    [Fact(DisplayName = "模型下載測試：Download Async Throws When Sha Mismatch")]
    public async Task DownloadAsync_Throws_WhenShaMismatch()
    {
        var payload = Encoding.UTF8.GetBytes("test-model-content");

        using var httpClient = new HttpClient(new StaticResponseHandler(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(payload),
        }));

        var downloader = new HttpModelDownloader(httpClient);
        var tempDir = Path.Combine(Path.GetTempPath(), $"philche-download-{Guid.NewGuid():N}");

        try
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => downloader.DownloadAsync(
                new Uri("https://example.com/model.gguf"),
                tempDir,
                "DEADBEEF",
                cancellationToken: CancellationToken.None));
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }
    }

    [Fact(DisplayName = "模型下載測試：Download Async Throws Operation Canceled When Canceled")]
    public async Task DownloadAsync_ThrowsOperationCanceled_WhenCanceled()
    {
        var payload = Encoding.UTF8.GetBytes(new string('a', 1024 * 1024));

        using var httpClient = new HttpClient(new StaticResponseHandler(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(payload),
        }));

        var downloader = new HttpModelDownloader(httpClient);
        var tempDir = Path.Combine(Path.GetTempPath(), $"philche-download-{Guid.NewGuid():N}");
        using var cts = new CancellationTokenSource();

        try
        {
            cts.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => downloader.DownloadAsync(
                new Uri("https://example.com/model.gguf"),
                tempDir,
                string.Empty,
                cancellationToken: cts.Token));
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }
    }

    [Fact(DisplayName = "模型下載測試：Fetch Async Parses Manifest")]
    public async Task FetchAsync_ParsesManifest()
    {
        var json = """
                   {
                     "models": [
                       {
                         "key": "guard",
                         "version": "v1",
                         "downloadUrl": "https://example.com/guard.gguf",
                         "sha256": "ABC",
                         "sizeBytes": 123
                       }
                     ]
                   }
                   """;

        using var httpClient = new HttpClient(new StaticResponseHandler(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        }));

        var client = new ModelManifestClient(httpClient);
        var manifest = await client.FetchAsync(new Uri("https://example.com/manifest.json"));

        var entry = Assert.Single(manifest.Models);
        Assert.Equal("guard", entry.Key);
        Assert.Equal("v1", entry.Version);
        Assert.Equal("https://example.com/guard.gguf", entry.DownloadUrl);
    }

    private sealed class StaticResponseHandler : HttpMessageHandler
    {
        private readonly HttpResponseMessage response;

        public StaticResponseHandler(HttpResponseMessage response)
        {
            this.response = response;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(response);
        }
    }
}


