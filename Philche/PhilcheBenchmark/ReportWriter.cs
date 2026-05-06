namespace PhilcheBenchmark;

internal static class ReportWriter
{
    public static void Write(TextWriter writer, BenchmarkRunResult runResult)
    {
        var rows = runResult.Results
            .Select(result => new[]
            {
                result.TestCase.RelativePath,
                result.TestCase.ExpectedUnsafe ? "malicious" : "benign",
                result.TestCase.Language,
                result.IsUnsafe ? "unsafe" : "safe",
                $"{result.Score:0.00}",
                $"{result.Duration.TotalMilliseconds:0} ms",
                result.IsUnsafe == result.TestCase.ExpectedUnsafe ? "OK" : "MISS"
            })
            .ToList();

        var headers = new[] { "File", "Type", "Lang", "Verdict", "Score", "Latency", "Match" };
        var widths = Enumerable.Range(0, headers.Length)
            .Select(index => Math.Max(headers[index].Length, rows.Count == 0 ? 0 : rows.Max(row => row[index].Length)))
            .ToArray();

        writer.WriteLine("============================================================");
        writer.WriteLine(" Philche LlamaGuard Benchmark");
        writer.WriteLine("============================================================");
        writer.WriteLine($" Model: {Path.GetFileName(runResult.ModelPath)}");
        writer.WriteLine($" CPU: {runResult.Hardware.CpuName}");
        writer.WriteLine($" RAM: {runResult.Hardware.TotalMemory}");
        writer.WriteLine($" OS: {runResult.Hardware.OperatingSystem}");
        writer.WriteLine($" Logical cores: {runResult.Hardware.LogicalCores}");
        writer.WriteLine($" Context size: {runResult.ContextSize}");
        writer.WriteLine($" Warmup: {runResult.WarmupDuration.TotalSeconds:0.00}s");
        writer.WriteLine();

        WriteTable(writer, headers, widths, rows);
        writer.WriteLine();

        WriteSummary(writer, "Overall", runResult.Results);
        WriteSummary(writer, "Chinese (zh)", runResult.Results.Where(static result => result.TestCase.Language == "zh"));
        WriteSummary(writer, "English (en)", runResult.Results.Where(static result => result.TestCase.Language == "en"));
    }

    private static void WriteSummary(TextWriter writer, string label, IEnumerable<BenchmarkCaseResult> source)
    {
        var results = source.ToList();
        if (results.Count == 0)
        {
            return;
        }

        var malicious = results.Where(static result => result.TestCase.ExpectedUnsafe).ToList();
        var benign = results.Where(static result => !result.TestCase.ExpectedUnsafe).ToList();
        var avgLatency = results.Average(static result => result.Duration.TotalMilliseconds);
        var p95Latency = Percentile(results.Select(static result => result.Duration.TotalMilliseconds).OrderBy(static value => value).ToArray(), 0.95);
        var recall = malicious.Count == 0 ? 1.0 : malicious.Count(static result => result.IsUnsafe) / (double)malicious.Count;
        var benignSafeRate = benign.Count == 0 ? 1.0 : benign.Count(static result => !result.IsUnsafe) / (double)benign.Count;

        writer.WriteLine($"[{label}]");
        writer.WriteLine($"  Cases: {results.Count}");
        writer.WriteLine($"  Avg latency: {avgLatency:0} ms");
        writer.WriteLine($"  P95 latency: {p95Latency:0} ms");
        writer.WriteLine($"  Malicious recall: {recall:P1}");
        writer.WriteLine($"  Benign precision: {benignSafeRate:P1}");
        writer.WriteLine();
    }

    private static void WriteTable(TextWriter writer, IReadOnlyList<string> headers, IReadOnlyList<int> widths, IReadOnlyList<string[]> rows)
    {
        writer.WriteLine(BuildRow(headers, widths));
        writer.WriteLine(BuildSeparator(widths));

        foreach (var row in rows)
        {
            writer.WriteLine(BuildRow(row, widths));
        }
    }

    private static string BuildRow(IReadOnlyList<string> columns, IReadOnlyList<int> widths)
    {
        return string.Join(" | ", columns.Select((column, index) => column.PadRight(widths[index])));
    }

    private static string BuildSeparator(IReadOnlyList<int> widths)
    {
        return string.Join("-+-", widths.Select(static width => new string('-', width)));
    }

    private static double Percentile(double[] sortedValues, double percentile)
    {
        if (sortedValues.Length == 0)
        {
            return 0;
        }

        var position = (sortedValues.Length - 1) * percentile;
        var lowerIndex = (int)Math.Floor(position);
        var upperIndex = (int)Math.Ceiling(position);
        if (lowerIndex == upperIndex)
        {
            return sortedValues[lowerIndex];
        }

        var weight = position - lowerIndex;
        return sortedValues[lowerIndex] + ((sortedValues[upperIndex] - sortedValues[lowerIndex]) * weight);
    }
}
