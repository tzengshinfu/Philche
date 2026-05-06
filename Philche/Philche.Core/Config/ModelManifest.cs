using System.Text.Json;

namespace Philche.Core.Config;

public sealed class ModelManifestEntry
{
    public string Key { get; init; } = string.Empty;
    public string Version { get; init; } = string.Empty;
    public string DownloadUrl { get; init; } = string.Empty;
    public string Sha256 { get; init; } = string.Empty;
    public long SizeBytes { get; init; }
}

public sealed class ModelManifest
{
    public List<ModelManifestEntry> Models { get; init; } = [];

    public ModelManifestEntry? FindByKey(string key)
    {
        return Models.FirstOrDefault(m => m.Key.Equals(key, StringComparison.OrdinalIgnoreCase));
    }
}

public sealed class ModelManifestClient
{
    private readonly HttpClient httpClient;

    public ModelManifestClient(HttpClient? httpClient = null)
    {
        this.httpClient = httpClient ?? new HttpClient();
    }

    public async Task<ModelManifest> FetchAsync(Uri manifestUrl, CancellationToken cancellationToken = default)
    {
        using var response = await httpClient.GetAsync(manifestUrl, cancellationToken);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        var dto = JsonSerializer.Deserialize<ModelManifestDto>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
        });

        if (dto?.Models is null)
        {
            return new ModelManifest();
        }

        return new ModelManifest
        {
            Models = dto.Models
                .Where(m => !string.IsNullOrWhiteSpace(m.Key) && !string.IsNullOrWhiteSpace(m.DownloadUrl))
                .Select(m => new ModelManifestEntry
                {
                    Key = m.Key!,
                    Version = m.Version ?? string.Empty,
                    DownloadUrl = m.DownloadUrl!,
                    Sha256 = m.Sha256 ?? string.Empty,
                    SizeBytes = m.SizeBytes ?? 0,
                })
                .ToList(),
        };
    }

    private sealed class ModelManifestDto
    {
        public List<ModelManifestItemDto>? Models { get; init; }
    }

    private sealed class ModelManifestItemDto
    {
        public string? Key { get; init; }
        public string? Version { get; init; }
        public string? DownloadUrl { get; init; }
        public string? Sha256 { get; init; }
        public long? SizeBytes { get; init; }
    }
}
