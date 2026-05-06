namespace PhilcheBenchmark;

internal static class TestCaseLoader
{
    private static readonly string[] Categories = ["malicious", "benign"];
    private static readonly string[] Languages = ["zh", "en"];

    public static IReadOnlyList<BenchmarkTestCase> Load(string rootPath)
    {
        if (string.IsNullOrWhiteSpace(rootPath))
        {
            throw new InvalidOperationException("Test case root path is required.");
        }

        if (!Directory.Exists(rootPath))
        {
            throw new InvalidOperationException($"Test case directory not found: {rootPath}");
        }

        var testCases = new List<BenchmarkTestCase>();

        foreach (var category in Categories)
        {
            foreach (var language in Languages)
            {
                var directory = Path.Combine(rootPath, category, language);
                if (!Directory.Exists(directory))
                {
                    continue;
                }

                foreach (var filePath in Directory.EnumerateFiles(directory, "*.md", SearchOption.TopDirectoryOnly).OrderBy(static path => path, StringComparer.OrdinalIgnoreCase))
                {
                    var content = File.ReadAllText(filePath).Trim();
                    if (string.IsNullOrWhiteSpace(content))
                    {
                        continue;
                    }

                    testCases.Add(new BenchmarkTestCase(
                        Name: Path.GetFileNameWithoutExtension(filePath),
                        RelativePath: Path.GetRelativePath(rootPath, filePath).Replace('\\', '/'),
                        Language: language,
                        ExpectedUnsafe: string.Equals(category, "malicious", StringComparison.Ordinal),
                        Content: content));
                }
            }
        }

        if (testCases.Count == 0)
        {
            throw new InvalidOperationException($"No .md test cases were found under {rootPath}.");
        }

        return testCases;
    }
}
