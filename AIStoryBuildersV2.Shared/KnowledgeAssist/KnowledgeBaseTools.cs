using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using AIStoryBuildersV2.Shared.Knowledge;
using AIStoryBuildersV2.Shared.Markdown;
using Microsoft.Extensions.AI;

namespace AIStoryBuildersV2.Shared.KnowledgeAssist;

/// <summary>Plan §6.4 constants for KB retrieval and tools.</summary>
public static class KnowledgeAssistConstants
{
    public const int MaxIterations = 4;
    public const int MaxKnowledgeNotesChars = 4000;
    public static readonly TimeSpan RetrieveTimeout = TimeSpan.FromSeconds(60);
    public const string NoRelevantPages = "NO_RELEVANT_PAGES";
}

/// <summary>
/// D14: KGB's four read tools for one story's KB, renamed with the <c>kb_</c> prefix so they never clash with the story
/// graph tools. There are no write tools. Paths are parsed with <see cref="KnowledgePath"/>, so they stay inside the KB.
/// </summary>
public sealed class KnowledgeBaseTools(IStoryKnowledgeBase kb, int maxToolResultChars = 20_000)
{
    public const string Prefix = "kb_";

    public IList<AITool> CreateReadOnly() =>
    [
        AIFunctionFactory.Create(SearchPagesAsync, Prefix + "search_pages", "Keyword search over the story's knowledge base: page titles, aliases, tags, paths and text. Returns the best matches with snippets."),
        AIFunctionFactory.Create(ReadPageAsync, Prefix + "read_page", "Reads a knowledge base page's full Markdown, including front-matter."),
        AIFunctionFactory.Create(ListFolderAsync, Prefix + "list_folder", "Lists the pages and folders in a knowledge base folder. Use \"\" for the root."),
        AIFunctionFactory.Create(GetLinksAsync, Prefix + "get_links", "Lists a knowledge base page's outgoing links and backlinks."),
    ];

    IKnowledgeWorkspace Workspace => kb.Workspace;

    static string Json(object value) => JsonSerializer.Serialize(value, AIJsonUtilities.DefaultOptions);

    static string Error(string message) => Json(new { error = message });

    static bool TryPage(string? path, out KnowledgePath page, [NotNullWhen(false)] out string? error) =>
        KnowledgePath.TryParse(path?.Trim().TrimStart('/'), isPage: true, out page, out error);

    public async Task<string> SearchPagesAsync(
        [Description("Keywords to search for")] string query,
        [Description("Maximum results, 1 to 20")] int top = 8,
        CancellationToken ct = default)
    {
        var hits = await Workspace.Graph.SearchAsync(query ?? "", Math.Clamp(top, 1, 20), ct);
        return Json(hits.Select(h => new { path = h.Path, title = h.Title, snippet = h.Snippet }));
    }

    public async Task<string> ReadPageAsync([Description("Root-relative page path, for example \"Lore/Necklace.md\"")] string path, CancellationToken ct = default)
    {
        if (!TryPage(path, out var page, out var error)) return Error(error);
        try
        {
            var content = await Workspace.ReadPageAsync(page.Value, ct);
            var info = await Workspace.Graph.GetPageInfoAsync(page.Value, ct);
            var truncated = content.Markdown.Length > maxToolResultChars;
            var markdown = truncated ? content.Markdown[..maxToolResultChars] : content.Markdown;
            return Json(new { path = content.Path, title = info?.Title ?? MarkdownPageParser.DefaultTitle(content.Path), markdown, truncated });
        }
        catch (KnowledgeNotFoundException)
        {
            var resolved = await Workspace.Graph.ResolveAsync("", KnowledgePath.StripExtension(page.Value), ct);
            return resolved is null || string.Equals(resolved, page.Value, StringComparison.OrdinalIgnoreCase)
                ? Error($"No page at {page.Value}")
                : await ReadPageAsync(resolved, ct);
        }
    }

    public async Task<string> ListFolderAsync([Description("Root-relative folder path; \"\" is the root")] string path = "", CancellationToken ct = default)
    {
        if (!KnowledgePath.TryParse(path?.Trim().Trim('/'), isPage: false, out var folder, out var error)) return Error(error);
        try
        {
            var nodes = await Workspace.GetChildrenAsync(folder.Value, ct);
            return Json(nodes.Select(n => new { name = n.Name, path = n.Path, isFolder = n.IsFolder }));
        }
        catch (KnowledgeNotFoundException) { return Error($"No folder at {folder.Value}"); }
    }

    public async Task<string> GetLinksAsync([Description("Root-relative page path")] string path, CancellationToken ct = default)
    {
        if (!TryPage(path, out var page, out var error)) return Error(error);
        var links = await Workspace.Graph.GetLinksAsync(page.Value, ct);
        return Json(new
        {
            outgoing = links.Outgoing.Select(l => new { target = l.RawTarget, resolved = l.ResolvedPath, line = l.Line }),
            backlinks = links.Backlinks.Select(l => new { source = l.SourcePath, line = l.Line }),
        });
    }
}
