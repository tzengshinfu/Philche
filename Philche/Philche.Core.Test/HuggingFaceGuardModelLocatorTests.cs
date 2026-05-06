using Philche.Core.Config;

namespace Philche.Core.Test;

public sealed class HuggingFaceGuardModelLocatorTests
{
    [Fact]
    public void BuildFileName_ConvertsRepoNameToLowercaseGgufFileName()
    {
        var fileName = HuggingFaceGuardModelLocator.BuildFileName("Llama-Guard-3-8B-Q4_K_M-GGUF");

        Assert.Equal("llama-guard-3-8b-q4_k_m.gguf", fileName);
    }

    [Fact]
    public void BuildCandidateDownloadUris_UsesPreferredOwnersInOrder()
    {
        var uris = HuggingFaceGuardModelLocator.BuildCandidateDownloadUris("Llama-Guard-3-8B-Q4_K_M-GGUF");

        Assert.Collection(
            uris,
            uri => Assert.Equal("https://huggingface.co/lauraharyo/Llama-Guard-3-8B-Q4_K_M-GGUF/resolve/main/llama-guard-3-8b-q4_k_m.gguf?download=true", uri.ToString()),
            uri => Assert.Equal("https://huggingface.co/QuantFactory/Llama-Guard-3-8B-Q4_K_M-GGUF/resolve/main/llama-guard-3-8b-q4_k_m.gguf?download=true", uri.ToString()));
    }
}