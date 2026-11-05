using AIStoryBuildersV2.Shared.Services;
using Microsoft.Extensions.DependencyInjection;
using Radzen;

namespace AIStoryBuildersV2.Shared.Hosting;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers the shared services used by both the MAUI (Windows) app and the Web (WASM) app (plan §5.3).
    /// The host registers its own services first: <see cref="IHostCapabilities"/>, <see cref="IFileExportService"/>,
    /// <see cref="Stories.IStoryPersistence"/> and an <see cref="HttpClient"/>.
    /// </summary>
    public static IServiceCollection AddAIStoryBuildersV2Shared(this IServiceCollection services)
    {
        services.AddRadzenComponents();
        // Scoped: in MAUI, IJSRuntime is scoped to the BlazorWebView, so a singleton would capture the root one and fail.
        services.AddScoped<BrowserInterop>();
        return services;
    }
}
