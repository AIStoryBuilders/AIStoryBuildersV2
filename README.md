# AIStoryBuildersV2

AI StoryBuilders V2 on **.NET 11** (release candidate): a **Windows MAUI Blazor Hybrid** app and a
server-less **Blazor WebAssembly** app that share one Razor class library. The structure follows
[KnowledgeGraphBuilder](https://github.com/ADefWebserver/KnowledgeGraphBuilder). Aspire is used
only at development time to launch both apps.

The implementation plan is [Docs/AIStoryBuildersV2-Plan.md](Docs/AIStoryBuildersV2-Plan.md), and
the rules for contributors and agents are in [AGENTS.md](AGENTS.md).

## Projects

| Project | Kind | Purpose |
|---------|------|---------|
| `AIStoryBuildersV2` | MAUI (`net11.0-windows10.0.19041.0`) | Desktop shell; hosts the Blazor UI in a `BlazorWebView` |
| `AIStoryBuildersV2.Web` | Blazor WebAssembly (`net11.0`) | Browser app (static files only); hosts the same UI |
| `AIStoryBuildersV2.Shared` | Razor class library | **All shared UI and app logic lives here** |
| `AIStoryBuildersV2.AppHost` | Aspire AppHost | Dev-time only: launches the Windows app and the WASM dev server |
| `AIStoryBuildersV2.ServiceDefaults` | Aspire shared | Kept in the solution, referenced by nothing |
| `AIStoryBuildersV2.Tests` | xUnit | Tests |

The Android, iOS and Mac Catalyst targets are opt-in: pass `-p:IncludeMobileTargets=true`
(iOS and Mac Catalyst need macOS). Only Windows is a supported target.

## Build and test

Requires the .NET SDK `11.0.100-rc.3` (pinned in `global.json`) and the `maui-windows` workload.
See [Docs/Dev-Environment.md](Docs/Dev-Environment.md).

```bash
dotnet build AIStoryBuildersV2/AIStoryBuildersV2.csproj -f net11.0-windows10.0.19041.0
```

```bash
dotnet build AIStoryBuildersV2.Web/AIStoryBuildersV2.Web.csproj
```

```bash
dotnet test AIStoryBuildersV2.Tests/AIStoryBuildersV2.Tests.csproj
```

Run the web app with `dotnet run --project AIStoryBuildersV2.Web`, or run both apps with
`aspire run` from the repository root.

## The UI is specified in `Docs/Design`

The screens are specified in [`Docs/Design/DESIGN.md`](Docs/Design/DESIGN.md) and the interactive
artboards, with behavior in [`Docs/UI-Behavior-Spec.md`](Docs/UI-Behavior-Spec.md).
