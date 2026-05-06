using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace Philche.Core.Correlation;

public sealed class NvdApiClient(HttpClient httpClient) : INvdClient
{
    public async Task<IReadOnlyList<VulnerabilityRecord>> QueryByCpeAsync(
        string virtualMatchString,
        string version,
        CancellationToken cancellationToken = default)
    {
        var encodedCpe = Uri.EscapeDataString(virtualMatchString);
        var encodedVersion = Uri.EscapeDataString(version);
        var url = $"https://services.nvd.nist.gov/rest/json/cves/2.0?virtualMatchString={encodedCpe}&versionEndIncluding={encodedVersion}";

        using var response = await httpClient.GetAsync(url, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return [];
        }

        var payload = await response.Content.ReadFromJsonAsync<NvdResponse>(cancellationToken: cancellationToken);
        if (payload?.Vulnerabilities is null)
        {
            return [];
        }

        return payload.Vulnerabilities
            .Where(x => x.Cve?.Id is not null)
            .Select(x => new VulnerabilityRecord(
                x.Cve!.Id!,
                x.Cve.Descriptions?.FirstOrDefault()?.Value,
                x.Cve.Descriptions?.FirstOrDefault()?.Value,
                x.Cve.Metrics?.CvssMetricV31?.FirstOrDefault()?.CvssData?.BaseSeverity,
                "nvd",
                x.Cve.Id))
            .ToList();
    }

    private sealed class NvdResponse
    {
        [JsonPropertyName("vulnerabilities")]
        public List<NvdVulnerabilityWrapper>? Vulnerabilities { get; init; }
    }

    private sealed class NvdVulnerabilityWrapper
    {
        [JsonPropertyName("cve")]
        public NvdCve? Cve { get; init; }
    }

    private sealed class NvdCve
    {
        [JsonPropertyName("id")]
        public string? Id { get; init; }

        [JsonPropertyName("descriptions")]
        public List<NvdDescription>? Descriptions { get; init; }

        [JsonPropertyName("metrics")]
        public NvdMetrics? Metrics { get; init; }
    }

    private sealed class NvdDescription
    {
        [JsonPropertyName("value")]
        public string? Value { get; init; }
    }

    private sealed class NvdMetrics
    {
        [JsonPropertyName("cvssMetricV31")]
        public List<NvdCvssMetric>? CvssMetricV31 { get; init; }
    }

    private sealed class NvdCvssMetric
    {
        [JsonPropertyName("cvssData")]
        public NvdCvssData? CvssData { get; init; }
    }

    private sealed class NvdCvssData
    {
        [JsonPropertyName("baseSeverity")]
        public string? BaseSeverity { get; init; }
    }
}
