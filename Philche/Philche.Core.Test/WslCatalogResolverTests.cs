using Philche.Core.Discovery;
using Philche.Core.Domain.Models;
using Philche.Core.SkillsRisk;
using Xunit.Abstractions;

namespace Philche.Core.Test;

public sealed class WslCatalogResolverTests(ITestOutputHelper output)
{
    [Fact(DisplayName = "WSL 目錄解析測試：Expand Async Generates Existing Windows And Wsl Paths As Default Trusted")]
    public async Task ExpandAsync_GeneratesExistingWindowsAndWslPathsAsDefaultTrusted()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), $"philche-wsl-{Guid.NewGuid():N}");
        var homeRoot = Path.Combine(tempRoot, "Ubuntu-22.04", "home");
        var windowsProfileRoot = Path.Combine(tempRoot, "windows-profile");
        var windowsSkillsRoot = Path.Combine(windowsProfileRoot, ".openclaw", "workspace", "skills");
        var aliceSkillsRoot = Path.Combine(homeRoot, "alice", ".openclaw", "workspace", "skills");

        try
        {
            Directory.CreateDirectory(windowsSkillsRoot);
            Directory.CreateDirectory(aliceSkillsRoot);
            Directory.CreateDirectory(Path.Combine(homeRoot, "bob"));

            var resolver = new WslCatalogResolver(
                new FakeWslProvider(["Ubuntu-22.04"]),
                distro => Path.Combine(tempRoot, distro, "home"),
                () => windowsProfileRoot);

            var resolved = Assert.Single(await resolver.ExpandAsync([
                new KnownAgentCatalogEntry
                {
                    AgentKey = "openclaw",
                    DisplayName = "OpenClaw",
                    SkillsPaths = [],
                    WslUserRelativePath = ".openclaw/workspace/skills",
                }
            ]));

            Assert.Equal(2, resolved.SkillsPaths.Count);
            Assert.Contains(resolved.SkillsPaths, path =>
                path.Default &&
                path.Trusted &&
                path.Path.Equals(windowsSkillsRoot, StringComparison.OrdinalIgnoreCase));

            Assert.Contains(resolved.SkillsPaths, path =>
                path.Default &&
                path.Trusted &&
                path.Path.Equals(aliceSkillsRoot, StringComparison.OrdinalIgnoreCase));

            Assert.DoesNotContain(resolved.SkillsPaths, path =>
                path.Path.Contains(@"\bob\", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }

    [Fact(DisplayName = "WSL 目錄解析測試：Expand Async Preserves Existing Trusted Flag On Duplicate Path")]
    public async Task ExpandAsync_PreservesExistingTrustedFlagOnDuplicatePath()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), $"philche-wsl-{Guid.NewGuid():N}");
        var homeRoot = Path.Combine(tempRoot, "Ubuntu-22.04", "home");

        try
        {
            Directory.CreateDirectory(Path.Combine(homeRoot, "alice", ".openclaw", "workspace", "skills"));
            var generatedPath = Path.Combine(homeRoot, "alice", ".openclaw", "workspace", "skills");

            var resolver = new WslCatalogResolver(
                new FakeWslProvider(["Ubuntu-22.04"]),
                distro => Path.Combine(tempRoot, distro, "home"),
                () => Path.Combine(tempRoot, "windows-profile"));

            var resolved = Assert.Single(await resolver.ExpandAsync([
                new KnownAgentCatalogEntry
                {
                    AgentKey = "openclaw",
                    DisplayName = "OpenClaw",
                    SkillsPaths = [new SkillsPathEntry(generatedPath, Trusted: false)],
                    WslUserRelativePath = ".openclaw/workspace/skills",
                }
            ]));

            var merged = Assert.Single(resolved.SkillsPaths);
            Assert.Equal(generatedPath, merged.Path);
            Assert.False(merged.Trusted); // Preserved user choice
            Assert.True(merged.Default);
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }

    [Fact(DisplayName = "WSL 目錄解析測試：Integration Can Read Demo Local Skill From Wsl Path")]
    public async Task Integration_CanReadDemoLocalSkillFromWslPath()
    {
        const string expectedSkillFile = @"\\wsl.localhost\Ubuntu-22.04\home\y1938\.openclaw\workspace\skills\demo-local-skill\SKILL.md";

        if (!File.Exists(expectedSkillFile))
        {
            output.WriteLine($"Skip integration check because file does not exist: {expectedSkillFile}");
            return;
        }

        var resolver = new WslCatalogResolver(new CommandWslDistroProvider());
        var resolved = Assert.Single(await resolver.ExpandAsync([
            new KnownAgentCatalogEntry
            {
                AgentKey = "openclaw",
                DisplayName = "OpenClaw",
                SkillsPaths = [new SkillsPathEntry(@"%USERPROFILE%\.openclaw\workspace\skills", Trusted: true)],
                WslUserRelativePath = ".openclaw/workspace/skills",
            }
        ]));

        var wslPath = Assert.Single(
            resolved.SkillsPaths,
            path => path.Path.Equals(@"\\wsl.localhost\Ubuntu-22.04\home\y1938\.openclaw\workspace\skills", StringComparison.OrdinalIgnoreCase));

        Assert.True(wslPath.Trusted);
        Assert.True(wslPath.Default);

        var files = new AgentCodeCollector().GetScannableFiles([wslPath.Path]);
        Assert.Contains(files, path => path.Equals(expectedSkillFile, StringComparison.OrdinalIgnoreCase));

        var content = await File.ReadAllTextAsync(expectedSkillFile);
        output.WriteLine(content);
        Assert.False(string.IsNullOrWhiteSpace(content));
    }

    private sealed class FakeWslProvider(IReadOnlyList<string> distros) : IWslDistroProvider
    {
        public Task<IReadOnlyList<string>> ListDistrosAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(distros);
        }
    }
}


