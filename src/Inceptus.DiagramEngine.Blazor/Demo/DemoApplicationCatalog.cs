using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Inceptus.DiagramEngine.Blazor.Demo;

internal sealed record DemoApplicationCatalog(int SchemaVersion, bool InspectEnabled, string InspectLabel, string Description)
{
    internal static async Task<DemoApplicationCatalog> LoadAsync(HttpClient client, CancellationToken token)
    {
        var catalog = await client.GetFromJsonAsync("demo/application-action-catalog.json",
            DemoApplicationCatalogJsonContext.Default.DemoApplicationCatalog, token);
        if (catalog is not { SchemaVersion: 1 } || string.IsNullOrWhiteSpace(catalog.InspectLabel) ||
            catalog.InspectLabel.Length > 128 || catalog.InspectLabel.Any(char.IsControl) ||
            string.IsNullOrWhiteSpace(catalog.Description) || catalog.Description.Length > 2048)
            throw new JsonException("The application catalogue has an unsupported schema or invalid label/description.");
        return catalog;
    }
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(DemoApplicationCatalog))]
internal sealed partial class DemoApplicationCatalogJsonContext : JsonSerializerContext;
