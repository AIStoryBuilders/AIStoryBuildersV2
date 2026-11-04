using Microsoft.Extensions.DependencyInjection;
using Radzen;

namespace AIStoryBuildersV2.Shared.Hosting;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers the shared UI services used by both the MAUI (Windows) app and the Web (WASM) app.
    /// Add the application's own services here as they are built from the designs in <c>Docs/Design</c>
    /// (e.g. the story store, AI chat clients, settings, and the knowledge-graph services).
    /// </summary>
    public static IServiceCollection AddAIStoryBuildersV2Shared(this IServiceCollection services)
    {
        services.AddRadzenComponents();
        return services;
    }
}
