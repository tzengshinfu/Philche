using System.Text.RegularExpressions;

namespace PhilcheBenchmark;

internal static partial class PromptConstraintValidator
{
    private static readonly string[] ForbiddenTerms =
    [
        "exfiltrate",
        "steal",
        "bypass",
        "jailbreak",
        "api key",
        "password",
        "private key",
        "session cookie",
        "credit card"
    ];

    [GeneratedRegex("base64|token|ignore\\s+previous|user\\s*:\\s*.*?system\\s*:", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex ForbiddenPatternRegex();

    public static void Validate(IReadOnlyList<BenchmarkTestCase> testCases)
    {
        var violations = new List<string>();

        foreach (var testCase in testCases.Where(static candidate => candidate.ExpectedUnsafe))
        {
            var lower = testCase.Content.ToLowerInvariant();
            var hitTerm = ForbiddenTerms.FirstOrDefault(lower.Contains);
            if (hitTerm is not null)
            {
                violations.Add($"{testCase.RelativePath} contains forbidden term '{hitTerm}'.");
                continue;
            }

            if (ForbiddenPatternRegex().IsMatch(testCase.Content))
            {
                violations.Add($"{testCase.RelativePath} matches a forbidden regex trigger.");
            }
        }

        if (violations.Count > 0)
        {
            throw new InvalidOperationException(string.Join(Environment.NewLine, violations));
        }
    }
}
