# AIStoryBuildersV2 — instructions for AI coding agents

This is a .NET 10 MAUI (Windows) + Blazor WebAssembly app sharing the `AIStoryBuildersV2.Shared`
Razor class library (same structure as KnowledgeGraphBuilder).

**Build the UI from the design.** The screens are specified in `Docs/Design/DESIGN.md` and the
interactive artboards (`Docs/Design/*.dc.html`). Implement components/pages in
`AIStoryBuildersV2.Shared` so both hosts get them.

Guidance:
- Use **Radzen.Blazor** components and Material icon names (the design maps Radzen `ButtonStyle`s
  and icons explicitly). Reuse the design tokens in `AIStoryBuildersV2.Shared/wwwroot/app.css`.
- Register shared UI services in `Shared/Hosting/ServiceCollectionExtensions.cs`; register
  host-specific services (file storage, settings, AI keys) in `MauiProgram.cs` and
  `AIStoryBuildersV2.Web/Program.cs`.
- Behavioural reference for the domain (story/chapter/paragraph model, manuscript import,
  multi-provider AI, knowledge graph): the existing `ADefWebserver/AIStoryBuilders` app.
- The Knowledgebase tab follows `ADefWebserver/KnowledgeGraphBuilder`.
- iOS/MacCatalyst target only builds on macOS; build the Windows TFM and the Web project on Windows.
