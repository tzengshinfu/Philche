using System.Text.RegularExpressions;

namespace Philche.Core.SkillsRisk;

public sealed class YaraCodeScanner
{
    private readonly IReadOnlyList<YaraCodeRule> rules;

    public YaraCodeScanner(IReadOnlyList<YaraCodeRule>? rules = null)
    {
        this.rules = rules ??
        [
            new YaraCodeRule("suspicious_shell_exec", "process\\.start|exec\\(|/bin/sh|cmd\\.exe"),
            new YaraCodeRule("sensitive_data_exfiltration", "api[_-]?key|private\\s+key|session\\s*cookie|credit\\s*card|password"),
            new YaraCodeRule("encoded_payload", "base64|frombase64string|hex|rot13")
        ];
    }

    public IReadOnlyList<SkillRiskEvidence> Scan(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return [];
        }

        var lines = content.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
        var evidence = new List<SkillRiskEvidence>();

        for (var index = 0; index < lines.Length; index++)
        {
            var line = lines[index];
            foreach (var rule in rules)
            {
                if (!rule.Regex.IsMatch(line))
                {
                    continue;
                }

                evidence.Add(new SkillRiskEvidence(
                    "yara",
                    0.25,
                    $"Rule matched: {rule.Name}",
                    index + 1,
                    index + 1));
            }
        }

        return evidence;
    }
}

public sealed record YaraCodeRule
{
    public string Name { get; }
    public Regex Regex { get; }

    public YaraCodeRule(string name, string pattern)
    {
        Name = name;
        Regex = new Regex(pattern, RegexOptions.IgnoreCase | RegexOptions.Compiled);
    }
}
