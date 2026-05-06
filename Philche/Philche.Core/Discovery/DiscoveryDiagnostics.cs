namespace Philche.Core.Discovery;

public sealed record DiscoveryDiagnostics(
    int KnownAgentsDiscovered,
    int UnknownCandidatesIgnored,
    int WslTargetsEnumerated);
