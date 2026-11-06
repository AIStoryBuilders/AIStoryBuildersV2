using AIStoryBuildersV2.Shared.Stories.Storage;
using Microsoft.JSInterop;

namespace AIStoryBuildersV2.Web.Services;

public sealed class BrowserStoryIndexLock(IJSRuntime js) : IStoryIndexLock
{
    public async Task RunAsync(Func<Task> action, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        using var target = DotNetObjectReference.Create(new LockCallback(action, ct));
        await js.InvokeVoidAsync("asb.withLock", "asb2:stories", target);
    }

    sealed class LockCallback(Func<Task> action, CancellationToken ct)
    {
        [JSInvokable]
        public Task RunAsync()
        {
            ct.ThrowIfCancellationRequested();
            return action();
        }
    }
}
