using Philche.Core.Domain.Models;

namespace Philche.Core.Discovery;

public sealed record KnownAgentCatalogEntry
{
    public required string AgentKey { get; init; }
    public required string DisplayName { get; init; }
    public IReadOnlyList<string> HostExecutablePaths { get; init; } = [];
    public IReadOnlyList<SkillsPathEntry> SkillsPaths { get; init; } = [];
    public string WslUserRelativePath { get; init; } = string.Empty;
    public IReadOnlyList<string> ExecutableNames { get; init; } = [];
}
