# AIStoryBuildersV2

A .NET 10 rewrite scaffold for **AI StoryBuilders**, structured like
[KnowledgeGraphBuilder](https://github.com/ADefWebserver/KnowledgeGraphBuilder): a **Windows MAUI
Blazor Hybrid** app and a **Blazor WebAssembly** app that share one Razor class library, wired for
local-dev launch with .NET Aspire.

## Projects

| Project | Kind | Purpose |
|---------|------|---------|
| `AIStoryBuildersV2` | MAUI (net10.0-windows + android/ios/maccatalyst) | Desktop/mobile shell; hosts the Blazor UI in a `BlazorWebView` |
| `AIStoryBuildersV2.Web` | Blazor WebAssembly | Browser app; hosts the same UI |
| `AIStoryBuildersV2.Shared` | Razor class library | **All shared UI and app logic lives here** |
| `AIStoryBuildersV2.AppHost` | Aspire AppHost | Dev-time only: launches the Windows app and the WASM dev server |
| `AIStoryBuildersV2.ServiceDefaults` | Aspire shared | OpenTelemetry / resilience defaults |
| `AIStoryBuildersV2.Tests` | xUnit | Tests |

`AIStoryBuildersV2.slnx` ties them together.

## Build

```bash
dotnet build AIStoryBuildersV2.Web/AIStoryBuildersV2.Web.csproj
dotnet build AIStoryBuildersV2/AIStoryBuildersV2.csproj -f net10.0-windows10.0.19041.0
```

Build the whole solution in Visual Studio (the MAUI project multi-targets iOS/MacCatalyst, which
only build on macOS). Run the web app with `dotnet run` in `AIStoryBuildersV2.Web`, or run both via
Aspire from `AIStoryBuildersV2.AppHost`.

## The UI is specified in `Docs/Design`

This scaffold intentionally ships a minimal shell (a Home page + layout). **The application's
screens are specified in [`Docs/Design/DESIGN.md`](Docs/Design/DESIGN.md)** and the accompanying
interactive artboards. Implement the UI in `AIStoryBuildersV2.Shared` to match them.
