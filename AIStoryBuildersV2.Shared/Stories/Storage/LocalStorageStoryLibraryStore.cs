using System.Text.Json;
using AIStoryBuildersV2.Shared.Knowledge.Persistence;

namespace AIStoryBuildersV2.Shared.Stories.Storage;

public sealed record StoryIndex(int Version, List<StoryIndexEntry> Stories);

/// <summary>WASM story index: JSON <see cref="StoryIndex"/> under asb2:stories (plan §5.5, §6.2).</summary>
public sealed class LocalStorageStoryLibraryStore(IKeyValueStore store, IStoryIndexLock indexLock) : IStoryLibraryStore
{
    public async Task<IReadOnlyList<StoryIndexEntry>> ListAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        return (await ReadAsync()).Stories;
    }

    public async Task UpsertAsync(StoryIndexEntry entry, CancellationToken ct = default)
    {
        await indexLock.RunAsync(async () =>
        {
            ct.ThrowIfCancellationRequested();
            var index = await ReadAsync();
            var at = index.Stories.FindIndex(s => StoryTitle.SameTitle(s.Title, entry.Title));
            if (at >= 0) index.Stories[at] = entry with { Id = index.Stories[at].Id };
            else index.Stories.Add(entry with { Id = (index.Stories.Count + 1).ToString() });
            await WriteAsync(index);
        }, ct);
    }

    public async Task RemoveAsync(string title, CancellationToken ct = default)
    {
        await indexLock.RunAsync(async () =>
        {
            ct.ThrowIfCancellationRequested();
            var index = await ReadAsync();
            if (index.Stories.RemoveAll(s => StoryTitle.SameTitle(s.Title, title)) > 0) await WriteAsync(index);
        }, ct);
    }

    async Task<StoryIndex> ReadAsync()
    {
        var json = await store.GetAsync(StoryStorageConstants.StoriesIndexKey);
        if (string.IsNullOrWhiteSpace(json)) return new StoryIndex(1, []);
        try
        {
            var index = JsonSerializer.Deserialize<StoryIndex>(json, KeyValueJson.Options);
            return index is null ? new StoryIndex(1, []) : index with { Stories = index.Stories ?? [] };
        }
        catch (JsonException)
        {
            return new StoryIndex(1, []);
        }
    }

    Task WriteAsync(StoryIndex index) =>
        store.SetAsync(StoryStorageConstants.StoriesIndexKey, JsonSerializer.Serialize(index, KeyValueJson.Options));
}
