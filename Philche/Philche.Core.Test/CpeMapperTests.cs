using Philche.Core.Correlation;

namespace Philche.Core.Test;

public sealed class CpeMapperTests
{
    private readonly CpeMapper mapper = new();

    [Theory]
    [InlineData("pkg:generic/github-copilot-cli@1.0.0", "cpe:2.3:a:github-copilot-cli:github-copilot-cli:*:*:*:*:*:*:*:*")]
    [InlineData("pkg:nuget/Newtonsoft.Json@13.0.3", "cpe:2.3:a:newtonsoft_json:newtonsoft_json:*:*:*:*:*:*:*:*")]
    [InlineData("pkg:pypi/requests@2.31.0", "cpe:2.3:a:requests:requests:*:*:*:*:*:*:*:*")]
    [InlineData("pkg:npm/%40angular/core@17.0.0", "cpe:2.3:a:angular:core:*:*:*:*:*:*:*:*")]
    [InlineData("pkg:npm/lodash@4.17.21", "cpe:2.3:a:lodash:lodash:*:*:*:*:*:*:*:*")]
    public void TryMapToCpe_ReturnsExpectedCpe(string purl, string expectedCpe)
    {
        var identity = new PackageIdentity("test", "1.0.0", purl);
        var result = mapper.TryMapToCpe(identity);
        Assert.Equal(expectedCpe, result);
    }

    [Fact]
    public void TryMapToCpe_WithEmptyPurl_FallsBackToName()
    {
        var identity = new PackageIdentity("my-tool", "2.0.0", "");
        var result = mapper.TryMapToCpe(identity);
        Assert.Equal("cpe:2.3:a:my-tool:my-tool:*:*:*:*:*:*:*:*", result);
    }

    [Fact]
    public void TryMapToCpe_WithMalformedPurlMissingSlash_ReturnsNull()
    {
        var identity = new PackageIdentity("tool", "1.0.0", "pkg:npm");
        var result = mapper.TryMapToCpe(identity);
        Assert.Null(result);
    }

    [Fact]
    public void TryMapToCpe_SanitisesSpecialCharacters()
    {
        var identity = new PackageIdentity("My Tool", "1.0.0", "pkg:generic/My Tool@1.0.0");
        var result = mapper.TryMapToCpe(identity);
        Assert.NotNull(result);
        Assert.DoesNotContain(" ", result);
    }

    [Fact]
    public void TryMapToCpe_ProducesVersionWildcard()
    {
        var identity = new PackageIdentity("curl", "8.5.0", "pkg:generic/curl@8.5.0");
        var result = mapper.TryMapToCpe(identity);
        Assert.NotNull(result);
        // Version portion should be wildcard in the virtual match string
        Assert.Contains(":*:*:*:*:*:*:*:*", result);
        Assert.DoesNotContain("8.5.0", result);
    }
}
