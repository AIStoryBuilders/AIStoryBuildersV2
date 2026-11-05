using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace AIStoryBuildersV2.Shared.Stories;

/// <summary>
/// The fine-tuned model alias map (AIStoryBuildersDatabase.json on MAUI, shared with V1; asb2:db in the browser).
/// V1 loaded it in the constructor; V2 loads through <see cref="IAppDataStore"/>, so loading is async.
/// </summary>
public sealed class DatabaseService(IAppDataStore store)
{
    /// <summary>Model id → alias. Invalid JSON gives an empty map, as in V1.</summary>
    public async Task<Dictionary<string, string>> LoadDatabaseAsync(CancellationToken ct = default)
    {
        var result = new Dictionary<string, string>();
        var json = await store.ReadAsync(AppDataFile.Database, ct);
        if (string.IsNullOrWhiteSpace(json)) return result;
        try
        {
            if (JToken.Parse(json) is JObject obj)
            {
                foreach (var property in obj.Properties()) result[property.Name] = property.Value.ToString();
            }
        }
        catch (JsonException)
        {
            // V1 silently ignores an invalid file.
        }
        return result;
    }

    /// <summary>V1's format: Newtonsoft indented JSON, no trailing newline.</summary>
    public Task SaveDatabase(Dictionary<string, string> database, CancellationToken ct = default) =>
        store.WriteAsync(AppDataFile.Database, JsonConvert.SerializeObject(database, Formatting.Indented), ct);
}
