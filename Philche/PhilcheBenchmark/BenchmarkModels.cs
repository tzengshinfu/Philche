namespace PhilcheBenchmark;

internal sealed record BenchmarkTestCase(
    string Name,
    string RelativePath,
    string Language,
    bool ExpectedUnsafe,
    string Content);

internal sealed record BenchmarkCaseResult(
    BenchmarkTestCase TestCase,
    double Score,
    bool IsUnsafe,
    TimeSpan Duration);

internal sealed record HardwareInfo(
    string CpuName,
    string TotalMemory,
    string OperatingSystem,
    int LogicalCores);

internal sealed record BenchmarkRunResult(
    string ModelPath,
    uint ContextSize,
    TimeSpan WarmupDuration,
    HardwareInfo Hardware,
    IReadOnlyList<BenchmarkCaseResult> Results);
