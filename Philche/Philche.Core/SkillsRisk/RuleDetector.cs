using System.Text.RegularExpressions;
using System.Text;
using Philche.Core.Config;

namespace Philche.Core.SkillsRisk;

public sealed class RuleDetector
{
    private const string DefaultMaliciousPhraseFileName = "malicious-phrases.txt";
    private const string DefaultDangerousPatternFileName = "dangerous-patterns.txt";

    private static readonly Regex UnicodeEscapePattern = new(
        "\\\\u[0-9a-fA-F]{4}|%u[0-9a-fA-F]{4}|&#x[0-9a-fA-F]+;",
        RegexOptions.Compiled);

    private static readonly Regex UserSystemPattern = new(
        "user\\s*:\\s*.*?system\\s*:",
        RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);

    private static readonly Regex ZeroWidthPattern = new(
        "[\\u200B-\\u200D\\uFEFF\\u2060]",
        RegexOptions.Compiled);

    private static readonly Regex BidiControlPattern = new(
        "[\\u202A-\\u202E\\u2066-\\u2069]",
        RegexOptions.Compiled);

    private static readonly Regex EmojiPattern = new(
        "[\\uD83C-\\uDBFF][\\uDC00-\\uDFFF]|\\p{So}",
        RegexOptions.Compiled);

    private static readonly Regex RepeatedPunctuationPattern = new(
        "([!?.。,，；;:_-])\\1{2,}",
        RegexOptions.Compiled);

    private static readonly Regex CardCandidatePattern = new(
        "(?:\\d[ -]?){13,19}",
        RegexOptions.Compiled);

    private static readonly Regex CvcPattern = new(
        "\\b\\d{3,4}\\b",
        RegexOptions.Compiled);

    private static readonly Regex CvcKeywordPattern = new(
        "cvc|cvv|security\\s*code|驗證碼|安全碼",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex NonWordSeparatorPattern = new(
        "[^\\p{L}\\p{N}]+",
        RegexOptions.Compiled);

    private static readonly Regex LatinPattern = new("[A-Za-z]", RegexOptions.Compiled);
    private static readonly Regex CyrillicPattern = new("[\\u0400-\\u04FF]", RegexOptions.Compiled);

    private static readonly HashSet<string> EnglishStopwords =
    [
        "a", "an", "the", "is", "are", "am", "was", "were", "be", "been", "being",
        "to", "of", "in", "on", "at", "for", "from", "by", "with", "about",
        "and", "or", "but", "if", "then", "else", "as", "that", "this", "these", "those",
        "i", "you", "he", "she", "it", "we", "they", "me", "him", "her", "them",
        "my", "your", "his", "their", "our", "do", "does", "did", "have", "has", "had",
    ];

    private static readonly string[] BuiltInMaliciousPhrases =
    [
        "ignore previous",
        "ignore all instructions",
        "disregard previous",
        "disregard all",
        "forget previous",
        "override instructions",
        "new instructions",
        "do anything now",
        "pretend you are",
        "you are now free",
        "suppress training",
        "circumvent",
        "new persona",
        "act as if you have",
        "system prompt",
        "jailbreak",
        "bypass",
        "exfiltrate",
        "exfil",
        "steal",
        "harvest credentials",
        "dump",
        "keylog",
        "keystroke",
        "clipboard",
        "screen capture",
        "shell execute",
        "remote code",
        "command injection",
        "password",
        "api key",
        "secret key",
        "access token",
        "auth token",
        "bearer token",
        "refresh token",
        "credentials",
        "session cookie",
        "private key",
        "credit card",
        "cvc",
        "cvv",
        "base64",
        "token",
        "忽略之前",
        "忽略先前",
        "忽略上述",
        "忽略以上",
        "忘記之前",
        "忘記先前",
        "新的指令",
        "你現在是",
        "不受限制",
        "繞過安全",
        "越獄",
        "解除限制",
        "系統提示詞",
        "系統提示",
        "注入指令",
        "竊取資料",
        "洩漏資料",
        "外洩資料",
        "取得密碼",
        "取得金鑰",
        "傳送密碼",
        "竊取",
    ];

    private static readonly string[] BuiltInDangerousPatterns =
    [
        "ignore\\s+previous",
        "exfiltrate",
        "credit\\s*card",
        "password",
        "api[_-]?key",
        "base64",
    ];

    private readonly AhoCorasickMatcher maliciousMatcher;
    private readonly Regex[] dangerousPatterns;

    public RuleDetector(string? maliciousPhraseFilePath = null, string? dangerousPatternFilePath = null)
    {
        maliciousMatcher = new AhoCorasickMatcher(LoadMaliciousPhrases(maliciousPhraseFilePath));
        dangerousPatterns = LoadDangerousPatterns(dangerousPatternFilePath);
    }

    internal static string GetDefaultMaliciousPhraseFilePath()
    {
        var settingsFilePath = new SettingsYamlStore().FilePath;
        var settingsDirectory = Path.GetDirectoryName(settingsFilePath);

        if (string.IsNullOrWhiteSpace(settingsDirectory))
        {
            return Path.GetFullPath(DefaultMaliciousPhraseFileName);
        }

        return Path.Combine(settingsDirectory, DefaultMaliciousPhraseFileName);
    }

    internal static IReadOnlyList<string> LoadMaliciousPhrases(string? maliciousPhraseFilePath = null)
    {
        var path = ResolveMaliciousPhraseFilePath(maliciousPhraseFilePath);
        if (!File.Exists(path))
        {
            EnsureDefaultMaliciousPhraseFileExists(path);

            if (!File.Exists(path))
            {
                return BuiltInMaliciousPhrases;
            }

            return ParseMaliciousPhrases(path);
        }

        return ParseMaliciousPhrases(path);
    }

    internal static string GetDefaultDangerousPatternFilePath()
    {
        var settingsFilePath = new SettingsYamlStore().FilePath;
        var settingsDirectory = Path.GetDirectoryName(settingsFilePath);

        if (string.IsNullOrWhiteSpace(settingsDirectory))
        {
            return Path.GetFullPath(DefaultDangerousPatternFileName);
        }

        return Path.Combine(settingsDirectory, DefaultDangerousPatternFileName);
    }

    internal static Regex[] LoadDangerousPatterns(string? dangerousPatternFilePath = null)
    {
        var path = ResolveDangerousPatternFilePath(dangerousPatternFilePath);
        if (!File.Exists(path))
        {
            EnsureDefaultDangerousPatternFileExists(path);

            if (!File.Exists(path))
            {
                return CompileRegexPatterns(BuiltInDangerousPatterns);
            }

            return ParseDangerousPatterns(path);
        }

        return ParseDangerousPatterns(path);
    }

    private static IReadOnlyList<string> ParseMaliciousPhrases(string path)
    {
        return File
            .ReadLines(path, Encoding.UTF8)
            .Select(static line => line.Trim())
            .Where(static line => !string.IsNullOrWhiteSpace(line) && !line.StartsWith('#'))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static Regex[] ParseDangerousPatterns(string path)
    {
        var patterns = File
            .ReadLines(path, Encoding.UTF8)
            .Select(static line => line.Trim())
            .Where(static line => !string.IsNullOrWhiteSpace(line) && !line.StartsWith('#'))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return CompileRegexPatterns(patterns);
    }

    private static Regex[] CompileRegexPatterns(IEnumerable<string> patterns)
    {
        var compiled = new List<Regex>();

        foreach (var pattern in patterns)
        {
            try
            {
                compiled.Add(new Regex(pattern, RegexOptions.IgnoreCase | RegexOptions.Compiled));
            }
            catch (ArgumentException)
            {
            }
        }

        return [.. compiled];
    }

    private static void EnsureDefaultMaliciousPhraseFileExists(string path)
    {
        try
        {
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllLines(path, GetDefaultMaliciousPhraseFileLines(), Encoding.UTF8);
        }
        catch
        {
        }
    }

    private static string ResolveDangerousPatternFilePath(string? dangerousPatternFilePath)
    {
        if (!string.IsNullOrWhiteSpace(dangerousPatternFilePath))
        {
            return Path.GetFullPath(dangerousPatternFilePath);
        }

        return GetDefaultDangerousPatternFilePath();
    }

    private static void EnsureDefaultDangerousPatternFileExists(string path)
    {
        try
        {
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllLines(path, GetDefaultDangerousPatternFileLines(), Encoding.UTF8);
        }
        catch
        {
        }
    }

    private static IReadOnlyList<string> GetDefaultMaliciousPhraseFileLines()
    {
        return
        [
            "# Prompt injection / jailbreak (English)",
            "ignore previous",
            "ignore all instructions",
            "disregard previous",
            "disregard all",
            "forget previous",
            "override instructions",
            "new instructions",
            "do anything now",
            "pretend you are",
            "you are now free",
            "suppress training",
            "circumvent",
            "new persona",
            "act as if you have",
            "system prompt",
            "jailbreak",
            "bypass",
            string.Empty,
            "# Data exfiltration / credential theft (English)",
            "exfiltrate",
            "exfil",
            "steal",
            "harvest credentials",
            "dump",
            "keylog",
            "keystroke",
            "clipboard",
            "screen capture",
            "shell execute",
            "remote code",
            "command injection",
            string.Empty,
            "# Sensitive data patterns (English)",
            "password",
            "api key",
            "secret key",
            "access token",
            "auth token",
            "bearer token",
            "refresh token",
            "credentials",
            "session cookie",
            "private key",
            "credit card",
            "cvc",
            "cvv",
            "base64",
            "token",
            string.Empty,
            "# Prompt injection / jailbreak (Chinese)",
            "忽略之前",
            "忽略先前",
            "忽略上述",
            "忽略以上",
            "忘記之前",
            "忘記先前",
            "新的指令",
            "你現在是",
            "不受限制",
            "繞過安全",
            "越獄",
            "解除限制",
            "系統提示詞",
            "系統提示",
            "注入指令",
            string.Empty,
            "# Data exfiltration / credential theft (Chinese)",
            "竊取資料",
            "洩漏資料",
            "外洩資料",
            "取得密碼",
            "取得金鑰",
            "傳送密碼",
            "竊取",
        ];
    }

    private static IReadOnlyList<string> GetDefaultDangerousPatternFileLines()
    {
        return
        [
            "# Dangerous regex patterns for prompt scanning",
            "ignore\\s+previous",
            "exfiltrate",
            "credit\\s*card",
            "password",
            "api[_-]?key",
            "base64",
        ];
    }

    private static string ResolveMaliciousPhraseFilePath(string? maliciousPhraseFilePath)
    {
        if (!string.IsNullOrWhiteSpace(maliciousPhraseFilePath))
        {
            return Path.GetFullPath(maliciousPhraseFilePath);
        }

        return GetDefaultMaliciousPhraseFilePath();
    }

    public double Score(SkillEvaluationInput input)
    {
        return Math.Min(1.0, ScoreRulesAndChars(input) + ScoreRegexSignals(input));
    }

    public double ScoreRulesAndChars(
        SkillEvaluationInput input,
        bool enableMaliciousWordGroupList = true,
        bool enableInvisibleCharacterDetection = true)
    {
        var content = input.Content ?? string.Empty;
        if (string.IsNullOrWhiteSpace(content))
        {
            return 0.0;
        }

        var cleaned = Preprocess(content, normalizeObfuscation: enableInvisibleCharacterDetection);
        var score = 0.0;

        if (enableInvisibleCharacterDetection)
        {
            if (UnicodeEscapePattern.IsMatch(content))
            {
                score += 0.12;
            }

            if (ZeroWidthPattern.IsMatch(content) || BidiControlPattern.IsMatch(content))
            {
                score += 0.25;
            }

            if (ContainsSuspiciousHomoglyphMix(content))
            {
                score += 0.2;
            }

            var posLikeFiltered = ApplyPosLikeFilter(cleaned);

            var entropy = EstimateEntropy(posLikeFiltered);
            if (entropy >= 4.0)
            {
                score += 0.15;
            }
        }

        if (enableMaliciousWordGroupList)
        {
            var maliciousHits = maliciousMatcher.CountMatches(cleaned);
            if (maliciousHits > 0)
            {
                score += Math.Min(0.30, maliciousHits * 0.08);
            }
        }

        return Math.Min(1.0, score);
    }

    public double ScoreRegexSignals(SkillEvaluationInput input)
    {
        var content = input.Content ?? string.Empty;
        if (string.IsNullOrWhiteSpace(content))
        {
            return 0.0;
        }

        var cleaned = Preprocess(content);
        var score = 0.0;

        if (dangerousPatterns.Any(pattern => pattern.IsMatch(cleaned)))
        {
            score += 0.35;
        }

        if (UserSystemPattern.IsMatch(cleaned))
        {
            score += 0.2;
        }

        if (ContainsValidCardNumber(cleaned))
        {
            score += 0.22;
        }

        if (HasCvcNearCardContext(cleaned))
        {
            score += 0.18;
        }

        return Math.Min(1.0, score);
    }

    private static string Preprocess(string input, bool normalizeObfuscation = true)
    {
        var normalized = input;
        if (normalizeObfuscation)
        {
            normalized = DecodeEscapes(normalized);
            normalized = normalized.Normalize(NormalizationForm.FormKC);
            normalized = ZeroWidthPattern.Replace(normalized, string.Empty);
            normalized = NormalizeHomoglyphs(normalized);
        }

        normalized = EmojiPattern.Replace(normalized, " ");
        normalized = RepeatedPunctuationPattern.Replace(normalized, "$1");
        normalized = Regex.Replace(normalized, "\\s+", " ").Trim();
        return normalized;
    }

    private static string DecodeEscapes(string input)
    {
        var value = Regex.Replace(input, "\\\\u([0-9a-fA-F]{4})", static m =>
        {
            var code = Convert.ToInt32(m.Groups[1].Value, 16);
            return char.ConvertFromUtf32(code);
        });

        value = Regex.Replace(value, "%u([0-9a-fA-F]{4})", static m =>
        {
            var code = Convert.ToInt32(m.Groups[1].Value, 16);
            return char.ConvertFromUtf32(code);
        });

        value = Regex.Replace(value, "&#x([0-9a-fA-F]+);", static m =>
        {
            var code = Convert.ToInt32(m.Groups[1].Value, 16);
            return char.ConvertFromUtf32(code);
        });

        return value;
    }

    private static string NormalizeHomoglyphs(string input)
    {
        var builder = new StringBuilder(input.Length);
        foreach (var ch in input)
        {
            builder.Append(ch switch
            {
                'а' => 'a',
                'е' => 'e',
                'о' => 'o',
                'р' => 'p',
                'с' => 'c',
                'х' => 'x',
                'у' => 'y',
                'і' => 'i',
                'ј' => 'j',
                'А' => 'A',
                'Е' => 'E',
                'О' => 'O',
                'Р' => 'P',
                'С' => 'C',
                'Х' => 'X',
                'У' => 'Y',
                'І' => 'I',
                'Ј' => 'J',
                _ => ch,
            });
        }

        return builder.ToString();
    }

    private static bool ContainsSuspiciousHomoglyphMix(string input)
    {
        return LatinPattern.IsMatch(input) && CyrillicPattern.IsMatch(input);
    }

    private static bool ContainsValidCardNumber(string content)
    {
        foreach (Match match in CardCandidatePattern.Matches(content))
        {
            var digits = new string(match.Value.Where(char.IsDigit).ToArray());
            if (digits.Length is < 13 or > 19)
            {
                continue;
            }

            if (PassesLuhn(digits))
            {
                return true;
            }
        }

        return false;
    }

    private static bool PassesLuhn(string digits)
    {
        var sum = 0;
        var doubleDigit = false;

        for (var index = digits.Length - 1; index >= 0; index--)
        {
            var value = digits[index] - '0';
            if (doubleDigit)
            {
                value *= 2;
                if (value > 9)
                {
                    value -= 9;
                }
            }

            sum += value;
            doubleDigit = !doubleDigit;
        }

        return sum % 10 == 0;
    }

    private static bool HasCvcNearCardContext(string content)
    {
        if (!CvcKeywordPattern.IsMatch(content))
        {
            return false;
        }

        var lowered = content.ToLowerInvariant();
        var hasCardContext = lowered.Contains("card", StringComparison.Ordinal) || lowered.Contains("信用卡", StringComparison.Ordinal);
        if (!hasCardContext)
        {
            return false;
        }

        return CvcPattern.IsMatch(content);
    }

    private static string ApplyPosLikeFilter(string content)
    {
        var tokens = NonWordSeparatorPattern.Split(content)
            .Where(static t => !string.IsNullOrWhiteSpace(t))
            .Select(static t => t.Trim())
            .ToList();

        var kept = new List<string>(tokens.Count);
        foreach (var token in tokens)
        {
            if (IsLikelyVerbOrNoun(token))
            {
                kept.Add(token);
            }
        }

        if (kept.Count == 0)
        {
            return content;
        }

        return string.Join(' ', kept);
    }

    private static bool IsLikelyVerbOrNoun(string token)
    {
        if (token.Length == 0)
        {
            return false;
        }

        var lower = token.ToLowerInvariant();

        if (EnglishStopwords.Contains(lower))
        {
            return false;
        }

        var containsCjk = token.Any(static c => c >= 0x4E00 && c <= 0x9FFF);
        if (containsCjk)
        {
            return token.Length >= 1;
        }

        if (char.IsDigit(token[0]))
        {
            return false;
        }

        return true;
    }

    private static double EstimateEntropy(string input)
    {
        var total = input.Length;
        if (total == 0)
        {
            return 0;
        }

        var groups = input
            .GroupBy(static c => c)
            .Select(g => (double)g.Count() / total)
            .ToList();

        if (groups.Count == 0)
        {
            return 0;
        }

        double entropy = 0;
        foreach (var p in groups)
        {
            entropy -= p * Math.Log2(p);
        }

        return entropy;
    }
}

internal sealed class AhoCorasickMatcher
{
    private sealed class Node
    {
        public Dictionary<char, Node> Next { get; } = new();
        public Node? Fail { get; set; }
        public int OutputCount { get; set; }
    }

    private readonly Node root = new();

    public AhoCorasickMatcher(IEnumerable<string> patterns)
    {
        foreach (var rawPattern in patterns)
        {
            var pattern = rawPattern.Trim().ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(pattern))
            {
                continue;
            }

            var node = root;
            foreach (var c in pattern)
            {
                if (!node.Next.TryGetValue(c, out var next))
                {
                    next = new Node();
                    node.Next[c] = next;
                }

                node = next;
            }

            node.OutputCount++;
        }

        BuildFailureLinks();
    }

    public int CountMatches(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return 0;
        }

        var count = 0;
        var node = root;

        foreach (var c in text.ToLowerInvariant())
        {
            while (node != root && !node.Next.ContainsKey(c))
            {
                node = node.Fail ?? root;
            }

            if (node.Next.TryGetValue(c, out var nextNode))
            {
                node = nextNode;
            }

            count += node.OutputCount;
        }

        return count;
    }

    private void BuildFailureLinks()
    {
        var queue = new Queue<Node>();
        root.Fail = root;

        foreach (var child in root.Next.Values)
        {
            child.Fail = root;
            queue.Enqueue(child);
        }

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            foreach (var pair in current.Next)
            {
                var transition = pair.Key;
                var target = pair.Value;

                var failure = current.Fail;
                while (failure != root && !failure!.Next.ContainsKey(transition))
                {
                    failure = failure.Fail;
                }

                if (failure!.Next.TryGetValue(transition, out var fallback))
                {
                    target.Fail = fallback;
                }
                else
                {
                    target.Fail = root;
                }

                target.OutputCount += target.Fail.OutputCount;
                queue.Enqueue(target);
            }
        }
    }
}
