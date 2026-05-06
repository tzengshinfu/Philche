using System.Text;
using System.Text.RegularExpressions;
using JiebaNet.Segmenter.PosSeg;

namespace Philche.Core.SkillsRisk;

public sealed class PromptPreprocessor
{
    private static readonly Regex ZeroWidthPattern = new("[\u200B-\u200D\uFEFF\u2060]", RegexOptions.Compiled);
    private static readonly Regex BidiControlPattern = new("[\u202A-\u202E\u2066-\u2069]", RegexOptions.Compiled);
    private static readonly Regex EmojiPattern = new(@"[\uD83C-\uDBFF][\uDC00-\uDFFF]|\p{So}", RegexOptions.Compiled);
    private static readonly Regex RepeatedPunctuationPattern = new(@"([!?.。,，；;:_-])\1{2,}", RegexOptions.Compiled);
    private static readonly Regex NonWordSeparatorPattern = new(@"[^\p{L}\p{N}]+", RegexOptions.Compiled);

    private static readonly HashSet<string> EnglishStopwords =
    [
        "a", "an", "the", "is", "are", "am", "was", "were", "be", "been", "being",
        "to", "of", "in", "on", "at", "for", "from", "by", "with", "about",
        "and", "or", "but", "if", "then", "else", "as", "that", "this", "these", "those",
        "i", "you", "he", "she", "it", "we", "they", "me", "him", "her", "them",
        "my", "your", "his", "their", "our", "do", "does", "did", "have", "has", "had",
    ];

    public string PreprocessForSemanticAndGuard(string content, bool enableJiebaPosFiltering)
    {
        var precleaned = Preclean(content);
        var posLikeFiltered = ApplyPosLikeFilter(precleaned);

        if (!enableJiebaPosFiltering)
        {
            return posLikeFiltered;
        }

        try
        {
            var jiebaFiltered = ApplyJiebaVerbNounFilter(posLikeFiltered);
            return string.IsNullOrWhiteSpace(jiebaFiltered) ? posLikeFiltered : jiebaFiltered;
        }
        catch
        {
            return posLikeFiltered;
        }
    }

    private static string Preclean(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return string.Empty;
        }

        var decoded = DecodeEscapes(input);
        var normalized = decoded.Normalize(NormalizationForm.FormKC);
        normalized = ZeroWidthPattern.Replace(normalized, string.Empty);
        normalized = BidiControlPattern.Replace(normalized, string.Empty);
        normalized = EmojiPattern.Replace(normalized, " ");
        normalized = RepeatedPunctuationPattern.Replace(normalized, "$1");
        normalized = NormalizeHomoglyphs(normalized);
        normalized = normalized.ToLowerInvariant();
        normalized = Regex.Replace(normalized, @"\s+", " ").Trim();

        return normalized;
    }

    private static string DecodeEscapes(string input)
    {
        var value = Regex.Replace(input, @"\\u([0-9a-fA-F]{4})", static m =>
        {
            var code = Convert.ToInt32(m.Groups[1].Value, 16);
            return char.ConvertFromUtf32(code);
        });

        value = Regex.Replace(value, @"%u([0-9a-fA-F]{4})", static m =>
        {
            var code = Convert.ToInt32(m.Groups[1].Value, 16);
            return char.ConvertFromUtf32(code);
        });

        value = Regex.Replace(value, @"&#x([0-9a-fA-F]+);", static m =>
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
                'а' => 'a', 'е' => 'e', 'о' => 'o', 'р' => 'p', 'с' => 'c', 'х' => 'x', 'у' => 'y',
                'і' => 'i', 'ј' => 'j', 'А' => 'A', 'Е' => 'E', 'О' => 'O', 'Р' => 'P', 'С' => 'C',
                'Х' => 'X', 'У' => 'Y', 'І' => 'I', 'Ј' => 'J',
                _ => ch,
            });
        }

        return builder.ToString();
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
            return true;
        }

        return !char.IsDigit(token[0]);
    }

    private string ApplyJiebaVerbNounFilter(string content)
    {
        var posSegmenter = new PosSegmenter();
        var pairs = posSegmenter.Cut(content, hmm: true);
        var kept = new List<string>();

        foreach (var pair in pairs)
        {
            if (string.IsNullOrWhiteSpace(pair.Word))
            {
                continue;
            }

            var flag = pair.Flag ?? string.Empty;
            if (flag.StartsWith("n", StringComparison.OrdinalIgnoreCase) ||
                flag.StartsWith("v", StringComparison.OrdinalIgnoreCase))
            {
                kept.Add(pair.Word);
            }
        }

        return string.Join(' ', kept);
    }
}
