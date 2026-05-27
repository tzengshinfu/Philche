using Philche.Core.Config;
using Philche.Core.Correlation;
using LLama;

namespace Philche.Core.Test;

public sealed class LlamaCveSummarySimplifierTests
{
    [Fact(DisplayName = "Llama CVE 摘要簡化測試：Simplify Async Falls Back To Regex When No Model Provider")]
    public async Task SimplifyAsync_FallsBackToRegex_WhenNoModelProvider()
    {
        var simplifier = new LlamaCveSummarySimplifier(modelProvider: null);

        var result = await simplifier.SimplifyAsync("CVE-2024-1234", "A buffer overflow vulnerability in libfoo allows remote code execution. Attackers can exploit this via crafted input.");

        Assert.True(simplifier.IsDegraded);
        Assert.NotNull(result);
        Assert.StartsWith("CVE-2024-1234:", result);
    }

    [Fact(DisplayName = "Llama CVE 摘要簡化測試：Simplify Async Falls Back To Regex When Model Unavailable")]
    public async Task SimplifyAsync_FallsBackToRegex_WhenModelUnavailable()
    {
        var simplifier = new LlamaCveSummarySimplifier(new FakeUnavailableModelProvider());

        var result = await simplifier.SimplifyAsync("CVE-2024-5678", "SQL injection in the admin panel.");

        Assert.True(simplifier.IsDegraded);
        Assert.NotNull(result);
        Assert.StartsWith("CVE-2024-5678:", result);
    }

    [Fact(DisplayName = "Llama CVE 摘要簡化測試：Simplify Async Returns Null When Summary Is Empty")]
    public async Task SimplifyAsync_ReturnsNull_WhenSummaryIsEmpty()
    {
        var simplifier = new LlamaCveSummarySimplifier(modelProvider: null);

        var result = await simplifier.SimplifyAsync("CVE-2024-0000", null);

        Assert.Null(result);
    }

    [Fact(DisplayName = "Llama CVE 摘要簡化測試：Simplify With Regex Fallback Extracts First Sentence")]
    public void SimplifyWithRegexFallback_ExtractsFirstSentence()
    {
        var result = LlamaCveSummarySimplifier.SimplifyWithRegexFallback(
            "CVE-2024-9999",
            "Buffer overflow in libxml2 before 2.9.12. Allows remote code execution.");

        Assert.Equal("CVE-2024-9999: Buffer overflow in libxml2 before 2.9.12", result);
    }

    [Fact(DisplayName = "Llama CVE 摘要簡化測試：Simplify With Regex Fallback Handles No Sentence End")]
    public void SimplifyWithRegexFallback_HandlesNoSentenceEnd()
    {
        var result = LlamaCveSummarySimplifier.SimplifyWithRegexFallback(
            "CVE-2024-0001",
            "A vulnerability in something");

        Assert.Equal("CVE-2024-0001: A vulnerability in something", result);
    }

    private sealed class FakeUnavailableModelProvider : IModelProvider
    {
        public string ModelPath => string.Empty;
        public bool IsAvailable => false;
        public LLamaWeights? GetWeights() => null;
        public void Dispose() { }
    }
}


