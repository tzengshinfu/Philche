using Philche.Core.Config;
using Philche.Core.SkillsRisk;
using LLama;

namespace Philche.Core.Test;

public sealed class LlamaGuardClassifierTests
{
    [Fact]
    public async Task ScoreAsync_FallsBackToKeywordStub_WhenNoModelProvider()
    {
        var classifier = new LlamaGuardClassifier(modelProvider: null);

        var score = await classifier.ScoreAsync(new SkillEvaluationInput(
            "exfiltrate password and steal api key",
            "test.md",
            false));

        Assert.True(classifier.IsDegraded);
        Assert.True(score > 0);
    }

    [Fact]
    public async Task ScoreAsync_FallsBackToKeywordStub_WhenModelUnavailable()
    {
        var classifier = new LlamaGuardClassifier(new FakeUnavailableModelProvider());

        var score = await classifier.ScoreAsync(new SkillEvaluationInput(
            "exfiltrate password",
            "test.md",
            false));

        Assert.True(classifier.IsDegraded);
        Assert.True(score > 0);
    }

    [Fact]
    public async Task ScoreAsync_ReturnsZero_WhenContentIsEmpty()
    {
        var classifier = new LlamaGuardClassifier(modelProvider: null);

        var score = await classifier.ScoreAsync(new SkillEvaluationInput(
            "",
            "test.md",
            false));

        Assert.Equal(0.0, score);
    }

    [Fact]
    public void ParseGuardOutput_Safe_ReturnsZero()
    {
        Assert.Equal(0.0, LlamaGuardClassifier.ParseGuardOutput("safe"));
        Assert.Equal(0.0, LlamaGuardClassifier.ParseGuardOutput("Safe\n"));
    }

    [Fact]
    public void ParseGuardOutput_UnsafeWithCategories_ReturnsScore()
    {
        Assert.Equal(0.5, LlamaGuardClassifier.ParseGuardOutput("unsafe\nS2, S7"));
        Assert.Equal(0.75, LlamaGuardClassifier.ParseGuardOutput("unsafe\nS2, S7, S14"));
    }

    [Fact]
    public void ParseGuardOutput_UnsafeWithoutCategories_Returns06()
    {
        Assert.Equal(0.6, LlamaGuardClassifier.ParseGuardOutput("unsafe"));
    }

    [Fact]
    public void ParseGuardOutput_Unparseable_ReturnsZero()
    {
        Assert.Equal(0.0, LlamaGuardClassifier.ParseGuardOutput("gibberish"));
        Assert.Equal(0.0, LlamaGuardClassifier.ParseGuardOutput(""));
    }

    [Fact]
    public void ParseGuardOutput_UnsafeManyCategoriesCapsAtOne()
    {
        Assert.Equal(1.0, LlamaGuardClassifier.ParseGuardOutput("unsafe\nS1, S2, S3, S4, S5"));
    }

    private sealed class FakeUnavailableModelProvider : IModelProvider
    {
        public string ModelPath => string.Empty;
        public bool IsAvailable => false;
        public LLamaWeights? GetWeights() => null;
        public void Dispose() { }
    }
}
