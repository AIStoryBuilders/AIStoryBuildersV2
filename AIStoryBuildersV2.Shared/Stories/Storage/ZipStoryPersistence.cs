namespace AIStoryBuildersV2.Shared.Stories.Storage;

/// <summary>
/// WASM persistence: one base64 zip per story in localStorage (D3, §5.5).
/// T1 stub: a no-op until T3 implements the zip lifecycle.
/// </summary>
public sealed class ZipStoryPersistence : IStoryPersistence
{
    public Task OpenAsync(string title, CancellationToken ct = default) => Task.CompletedTask;
    public void MarkDirty(string title) { }
    public Task FlushAsync(string? title = null, CancellationToken ct = default) => Task.CompletedTask;
    public Task DeleteAsync(string title, CancellationToken ct = default) => Task.CompletedTask;
    public Task ReloadAsync(string title, CancellationToken ct = default) => Task.CompletedTask;
    public bool IsDirty(string title) => false;
    public Task<StorageUsage?> GetUsageAsync(CancellationToken ct = default) => Task.FromResult<StorageUsage?>(null);

    public event Action<StoryPersistenceEvent>? Changed { add { } remove { } }
}
