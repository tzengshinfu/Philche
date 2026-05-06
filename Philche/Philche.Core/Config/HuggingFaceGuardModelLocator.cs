namespace Philche.Core.Config;

public static class HuggingFaceGuardModelLocator
{
    public const string DefaultModelName = "Llama-Guard-3-8B-Q4_K_M-GGUF";

    private static readonly string[] PreferredOwners = ["lauraharyo", "QuantFactory"];

    public static IReadOnlyList<Uri> BuildCandidateDownloadUris(string modelName)
    {
        var normalizedModelName = NormalizeModelName(modelName);
        var fileName = BuildFileName(normalizedModelName);

        return PreferredOwners
            .Select(owner => new Uri($"https://huggingface.co/{owner}/{Uri.EscapeDataString(normalizedModelName)}/resolve/main/{Uri.EscapeDataString(fileName)}?download=true"))
            .ToList();
    }

    public static string BuildFileName(string modelName)
    {
        var normalizedModelName = NormalizeModelName(modelName).ToLowerInvariant();

        if (normalizedModelName.EndsWith("-gguf", StringComparison.Ordinal))
        {
            return normalizedModelName[..^5] + ".gguf";
        }

        if (normalizedModelName.EndsWith(".gguf", StringComparison.Ordinal))
        {
            return normalizedModelName;
        }

        return normalizedModelName + ".gguf";
    }

    public static string NormalizeModelName(string modelName)
    {
        return string.IsNullOrWhiteSpace(modelName)
            ? DefaultModelName
            : modelName.Trim();
    }
}