using LLama;

namespace Philche.Core.Config;

public interface IModelProvider : IDisposable
{
    string ModelPath { get; }

    bool IsAvailable { get; }

    LLamaWeights? GetWeights();
}
