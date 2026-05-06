using Philche.Core.Config;

namespace Philche.Core.Test;

public sealed class GgufModelProviderTests
{
    [Fact]
    public void IsAvailable_ReturnsFalse_WhenPathIsEmpty()
    {
        var provider = new GgufModelProvider(string.Empty);

        Assert.False(provider.IsAvailable);
        Assert.Null(provider.GetWeights());
    }

    [Fact]
    public void IsAvailable_ReturnsFalse_WhenFileDoesNotExist()
    {
        var provider = new GgufModelProvider("/nonexistent/model.gguf");

        Assert.False(provider.IsAvailable);
        Assert.Null(provider.GetWeights());
    }

    [Fact]
    public void GetWeights_ReturnsNull_WhenFileDoesNotExist()
    {
        var provider = new GgufModelProvider("/nonexistent/model.gguf");

        var weights1 = provider.GetWeights();
        var weights2 = provider.GetWeights();

        Assert.Null(weights1);
        Assert.Null(weights2);
    }

    [Fact]
    public void GetWeights_DoesNotMarkPermanentFailure_WhenFileDoesNotExist()
    {
        var provider = new GgufModelProvider("/nonexistent/model.gguf");

        var first = provider.GetWeights();
        var second = provider.GetWeights();

        Assert.Null(first);
        Assert.Null(second);
        Assert.Null(provider.LastLoadError);
    }

    [Fact]
    public void GetWeights_Retries_WhenFileAppearsLater()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"philche-model-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        var modelPath = Path.Combine(tempDir, "late-model.gguf");

        try
        {
            var provider = new GgufModelProvider(modelPath);

            var first = provider.GetWeights();
            Assert.Null(first);
            Assert.Null(provider.LastLoadError);

            File.WriteAllText(modelPath, "not-a-real-gguf");

            var second = provider.GetWeights();
            Assert.Null(second);
            Assert.NotNull(provider.LastLoadError);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }
    }

    [Fact]
    public void GetWeights_MarksPermanentFailure_WhenLoadThrows()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"philche-model-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        var modelPath = Path.Combine(tempDir, "invalid.gguf");
        File.WriteAllText(modelPath, "not-a-real-gguf");

        try
        {
            var provider = new GgufModelProvider(modelPath);

            var first = provider.GetWeights();
            var second = provider.GetWeights();

            Assert.Null(first);
            Assert.Null(second);
            Assert.NotNull(provider.LastLoadError);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }
    }

    [Fact]
    public void Dispose_DoesNotThrow_WhenNoModelLoaded()
    {
        var provider = new GgufModelProvider(string.Empty);
        provider.Dispose();

        Assert.Null(provider.GetWeights());
    }

    [Fact]
    public void Dispose_CanBeCalledMultipleTimes()
    {
        var provider = new GgufModelProvider(string.Empty);
        provider.Dispose();
        provider.Dispose();

        Assert.Null(provider.GetWeights());
    }
}
