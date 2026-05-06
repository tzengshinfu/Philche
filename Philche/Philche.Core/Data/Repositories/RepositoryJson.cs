using System.Text.Json;

namespace Philche.Core.Data.Repositories;

internal static class RepositoryJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false,
    };
}
