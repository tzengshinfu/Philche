using Philche.Core.Config;

namespace Philche.Core.Test;

public sealed class HuggingFaceGuardModelDownloaderTests
{
    [Fact]
    public async Task DownloadAsync_FallsBackToSecondSource_WhenFirstSourceFails()
    {
        var downloader = new SequenceModelDownloader(
            _ => throw new HttpRequestException("first source unavailable"),
            uri => Task.FromResult($@"C:\models\{Path.GetFileName(uri.LocalPath)}"));
        var sut = new HuggingFaceGuardModelDownloader(downloader);
        var statuses = new List<GuardModelDownloadStatus>();

        var downloadedPath = await sut.DownloadAsync(
            "Llama-Guard-3-8B-Q4_K_M-GGUF",
            @"C:\models",
            onStatus: statuses.Add,
            cancellationToken: CancellationToken.None);

        Assert.Equal(@"C:\models\llama-guard-3-8b-q4_k_m.gguf", downloadedPath);
        Assert.Equal(2, downloader.RequestedUris.Count);
        Assert.Equal("https://huggingface.co/lauraharyo/Llama-Guard-3-8B-Q4_K_M-GGUF/resolve/main/llama-guard-3-8b-q4_k_m.gguf?download=true", downloader.RequestedUris[0].ToString());
        Assert.Equal("https://huggingface.co/QuantFactory/Llama-Guard-3-8B-Q4_K_M-GGUF/resolve/main/llama-guard-3-8b-q4_k_m.gguf?download=true", downloader.RequestedUris[1].ToString());

        Assert.Collection(
            statuses,
            status =>
            {
                Assert.Equal(GuardModelDownloadStatusKind.Attempting, status.Kind);
                Assert.Equal(1, status.AttemptNumber);
            },
            status =>
            {
                Assert.Equal(GuardModelDownloadStatusKind.Fallback, status.Kind);
                Assert.NotNull(status.Error);
                Assert.NotNull(status.NextDownloadUri);
            },
            status =>
            {
                Assert.Equal(GuardModelDownloadStatusKind.Attempting, status.Kind);
                Assert.Equal(2, status.AttemptNumber);
            });
    }

    [Fact]
    public async Task DownloadAsync_ThrowsLastError_WhenAllSourcesFail()
    {
        var downloader = new SequenceModelDownloader(
            _ => throw new HttpRequestException("first source unavailable"),
            _ => throw new InvalidOperationException("second source unavailable"));
        var sut = new HuggingFaceGuardModelDownloader(downloader);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => sut.DownloadAsync(
            "Llama-Guard-3-8B-Q4_K_M-GGUF",
            @"C:\models",
            cancellationToken: CancellationToken.None));

        Assert.Equal("second source unavailable", error.Message);
    }

    private sealed class SequenceModelDownloader : IModelDownloader
    {
        private readonly Queue<Func<Uri, Task<string>>> responses;

        public SequenceModelDownloader(params Func<Uri, Task<string>>[] responses)
        {
            this.responses = new Queue<Func<Uri, Task<string>>>(responses);
        }

        public List<Uri> RequestedUris { get; } = [];

        public Task<string> DownloadAsync(
            Uri sourceUrl,
            string targetDir,
            string expectedSha256,
            IProgress<(long downloaded, long total)>? progress = null,
            CancellationToken cancellationToken = default)
        {
            RequestedUris.Add(sourceUrl);

            if (responses.Count == 0)
            {
                throw new InvalidOperationException("No configured response for download call.");
            }

            return responses.Dequeue().Invoke(sourceUrl);
        }
    }
}