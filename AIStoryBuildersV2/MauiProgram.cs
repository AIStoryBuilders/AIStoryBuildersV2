using AIStoryBuildersV2.Shared.Hosting;
using Microsoft.Extensions.Logging;

namespace AIStoryBuildersV2;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiApp<App>()
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
            });

#if WINDOWS
        Platforms.Windows.WindowsShell.Configure(builder);
#endif

        builder.Services.AddMauiBlazorWebView();
        builder.Services.AddSingleton(_ => new HttpClient { Timeout = TimeSpan.FromMinutes(5) });
        builder.Services.AddAIStoryBuildersV2Shared();

#if DEBUG
        builder.Services.AddBlazorWebViewDeveloperTools();
        builder.Logging.AddDebug();
#endif

        return builder.Build();
    }
}
