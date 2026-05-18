using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Philche.Core.SkillsRisk;

public class VirusTotalUrlScanner(HttpClient httpClient)
{
    private static readonly Regex UrlRegex = new(@"https?://[^\s)\]>\""']+", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private const string MissingApiKeyMessage = "VirusTotal API key is required. Register or get one at https://docs.virustotal.com/docs/please-give-me-an-api-key and then fill in VT_API_KEY.";

    public static IReadOnlyList<string> ExtractUrls(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return [];
        }

        return UrlRegex.Matches(content)
            .Select(static match => match.Value.TrimEnd('.', ',', ';', ':'))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public virtual async Task<IReadOnlyList<VirusTotalUrlScanResult>> ScanAsync(
        IEnumerable<string> urls,
        string apiKey,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new InvalidOperationException(MissingApiKeyMessage);
        }

        var results = new List<VirusTotalUrlScanResult>();

        foreach (var url in urls.Where(static value => !string.IsNullOrWhiteSpace(value)).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            results.Add(await ScanSingleAsync(url, apiKey, cancellationToken));
        }

        return results;
    }

    private async Task<VirusTotalUrlScanResult> ScanSingleAsync(string url, string apiKey, CancellationToken cancellationToken)
    {
        using var postRequest = new HttpRequestMessage(HttpMethod.Post, "https://www.virustotal.com/api/v3/urls")
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["url"] = url,
            }),
        };

        AddApiKey(postRequest.Headers, apiKey);

        using var postResponse = await httpClient.SendAsync(postRequest, cancellationToken);
        postResponse.EnsureSuccessStatusCode();

        var submitPayload = await ReadJsonAsync<VirusTotalSubmitResponse>(postResponse, cancellationToken)
            ?? throw new InvalidOperationException("VirusTotal submit response was empty.");
        var analysisId = submitPayload.Data?.Id;
        if (string.IsNullOrWhiteSpace(analysisId))
        {
            throw new InvalidOperationException("VirusTotal analysis id was missing.");
        }

        VirusTotalAnalysisResponse? analysisPayload = null;
        for (var attempt = 0; attempt < 6; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            using var getRequest = new HttpRequestMessage(HttpMethod.Get, $"https://www.virustotal.com/api/v3/analyses/{analysisId}");
            AddApiKey(getRequest.Headers, apiKey);

            using var getResponse = await httpClient.SendAsync(getRequest, cancellationToken);
            getResponse.EnsureSuccessStatusCode();
            analysisPayload = await ReadJsonAsync<VirusTotalAnalysisResponse>(getResponse, cancellationToken);

            if (string.Equals(analysisPayload?.Data?.Attributes?.Status, "completed", StringComparison.OrdinalIgnoreCase))
            {
                break;
            }

            await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
        }

        var stats = analysisPayload?.Data?.Attributes?.Stats
            ?? throw new InvalidOperationException("VirusTotal analysis stats were unavailable.");

        var denominator = stats.Malicious + stats.Suspicious + stats.Undetected + stats.Harmless;
        var score = denominator <= 0 ? 0 : Math.Min(1d, (stats.Malicious + (stats.Suspicious * 0.5d)) / denominator);
        var severity = stats.Malicious > 0
            ? "malicious"
            : stats.Suspicious > 0
                ? "suspicious"
                : "clean";

        return new VirusTotalUrlScanResult(url, score, stats.Malicious, stats.Suspicious, severity);
    }

    private static void AddApiKey(HttpRequestHeaders headers, string apiKey)
    {
        headers.Remove("x-apikey");
        headers.Add("x-apikey", apiKey);
    }

    private static async Task<T?> ReadJsonAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        return await JsonSerializer.DeserializeAsync<T>(stream, cancellationToken: cancellationToken);
    }

    private sealed class VirusTotalSubmitResponse
    {
        [JsonPropertyName("data")]
        public VirusTotalSubmitData? Data { get; init; }
    }

    private sealed class VirusTotalSubmitData
    {
        [JsonPropertyName("id")]
        public string? Id { get; init; }
    }

    private sealed class VirusTotalAnalysisResponse
    {
        [JsonPropertyName("data")]
        public VirusTotalAnalysisData? Data { get; init; }
    }

    private sealed class VirusTotalAnalysisData
    {
        [JsonPropertyName("attributes")]
        public VirusTotalAnalysisAttributes? Attributes { get; init; }
    }

    private sealed class VirusTotalAnalysisAttributes
    {
        [JsonPropertyName("status")]
        public string? Status { get; init; }

        [JsonPropertyName("stats")]
        public VirusTotalAnalysisStats? Stats { get; init; }

        [JsonPropertyName("last_analysis_stats")]
        public VirusTotalAnalysisStats? LastAnalysisStats
        {
            get => Stats;
            init => Stats = value;
        }
    }

    private sealed class VirusTotalAnalysisStats
    {
        [JsonPropertyName("malicious")]
        public int Malicious { get; init; }

        [JsonPropertyName("suspicious")]
        public int Suspicious { get; init; }

        [JsonPropertyName("undetected")]
        public int Undetected { get; init; }

        [JsonPropertyName("harmless")]
        public int Harmless { get; init; }
    }
}

public sealed record VirusTotalUrlScanResult(
    string Url,
    double Score,
    int MaliciousCount,
    int SuspiciousCount,
    string Verdict);
