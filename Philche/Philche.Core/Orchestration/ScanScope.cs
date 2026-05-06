namespace Philche.Core.Orchestration;

public sealed record ScanScope(
    bool IsFullScan,
    string? AgentKey = null,
    IReadOnlyList<string>? FilePaths = null)
{
    public static ScanScope Full() => new(true);

    public static ScanScope Scoped(string? agentKey, IReadOnlyList<string> filePaths) =>
        new(false, agentKey, filePaths);
}
