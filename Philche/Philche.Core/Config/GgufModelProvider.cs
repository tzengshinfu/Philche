using LLama;
using LLama.Common;

namespace Philche.Core.Config;

public sealed class GgufModelProvider : IModelProvider
{
    private readonly object syncLock = new();
    private LLamaWeights? cachedWeights;
    private bool loadFailed;
    private bool disposed;

    public string ModelPath { get; }

    public bool IsAvailable => !string.IsNullOrWhiteSpace(ModelPath) && File.Exists(ModelPath);

    public uint ContextSize { get; }

    public Exception? LastLoadError { get; private set; }

    public GgufModelProvider(string modelPath, uint contextSize = 1024)
    {
        ModelPath = modelPath ?? string.Empty;
        ContextSize = contextSize;
    }

    public LLamaWeights? GetWeights()
    {
        if (disposed)
        {
            return null;
        }

        if (cachedWeights is not null)
        {
            return cachedWeights;
        }

        if (loadFailed)
        {
            return null;
        }

        lock (syncLock)
        {
            if (cachedWeights is not null)
            {
                return cachedWeights;
            }

            if (loadFailed)
            {
                return null;
            }

            if (!IsAvailable)
            {
                return null;
            }

            try
            {
                var modelParams = new ModelParams(ModelPath)
                {
                    ContextSize = ContextSize,
                    GpuLayerCount = 0,
                };

                cachedWeights = LLamaWeights.LoadFromFile(modelParams);
                return cachedWeights;
            }
            catch (Exception ex)
            {
                loadFailed = true;
                LastLoadError = ex;
                return null;
            }
        }
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        cachedWeights?.Dispose();
        cachedWeights = null;
    }
}
