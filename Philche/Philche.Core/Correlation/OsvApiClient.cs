using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace Philche.Core.Correlation;

public sealed class OsvApiClient(HttpClient httpClient) : IOsvClient
{
    public async Task<IReadOnlyList<VulnerabilityRecord>> QueryByPurlAsync(PackageIdentity identity, CancellationToken cancellationToken = default)
    {
        var request = new
        {
            package = new { purl = identity.Purl },
            version = identity.Version,
        };

        using var response = await httpClient.PostAsJsonAsync("https://api.osv.dev/v1/query", request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return [];
        }

        var payload = await response.Content.ReadFromJsonAsync<OsvResponse>(cancellationToken: cancellationToken);
        if (payload?.Vulns is null)
        {
            return [];
        }

        return payload.Vulns
            .Select(v => new VulnerabilityRecord(
                v.Id ?? "unknown",
                v.Summary,
                v.Details,
                v.Severity?.FirstOrDefault()?.Score,
                "osv",
                v.Id))
            .ToList();
    }

    private sealed class OsvResponse
    {
        [JsonPropertyName("vulns")]
        public List<OsvVulnerability>? Vulns { get; init; }
    }

    private sealed class OsvVulnerability
    {
        [JsonPropertyName("id")]
        public string? Id { get; init; }

        [JsonPropertyName("summary")]
        public string? Summary { get; init; }

        [JsonPropertyName("details")]
        public string? Details { get; init; }

        [JsonPropertyName("severity")]
        public List<OsvSeverity>? Severity { get; init; }
    }

    private sealed class OsvSeverity
    {
        [JsonPropertyName("score")]
        public string? Score { get; init; }
    }
}
