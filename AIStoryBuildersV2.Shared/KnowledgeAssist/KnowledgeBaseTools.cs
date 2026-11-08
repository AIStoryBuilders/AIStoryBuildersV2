using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using System.Text.Json;
using AIStoryBuildersV2.Shared.Knowledge;
using AIStoryBuildersV2.Shared.Markdown;
using AIStoryBuildersV2.Shared.Models;
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
/// graph tools, plus the page edits Story Chat proposes (<see cref="PlanAsync"/>, <see cref="ApplyAsync"/>). Paths are
/// parsed with <see cref="KnowledgePath"/>, so they stay inside the KB.
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

    // ── Page edits for Story Chat (D14 as amended, §5.8) ─────────────
    // The chat can create, rewrite and delete any page, but only as a proposal: Plan checks it now, and Apply writes it
    // after the user clicks Apply (SEC-001). Apply passes the page's ticks from Plan, so a newer save is never overwritten.

    public const string CreatePageTool = Prefix + "create_page";
    public const string UpdatePageTool = Prefix + "update_page";
    public const string DeletePageTool = Prefix + "delete_page";

    /// <summary>The kb_ tools that propose a change. Story Chat offers them; paragraph writing never does.</summary>
    public static readonly IReadOnlySet<string> EditToolNames = new HashSet<string>(StringComparer.Ordinal) { CreatePageTool, UpdatePageTool, DeletePageTool };

    public static string ToolName(KbEditKind kind) => kind switch
    {
        KbEditKind.Create => CreatePageTool,
        KbEditKind.Update => UpdatePageTool,
        _ => DeletePageTool,
    };

    /// <summary>Checks a proposed edit and describes it. <see cref="KbEditPlan.Preview"/> has an error when it can't be applied.</summary>
    public async Task<KbEditPlan> PlanAsync(KbEditKind kind, string? path, string? markdown, CancellationToken ct = default)
    {
        var tool = ToolName(kind);
        MutationResult Fail(string message) => new() { Operation = tool, Entity = path ?? "", Error = message };

        if (!TryPage(path, out var page, out var error)) return new KbEditPlan(Fail(error), "", 0);
        var pagePath = page.Value;
        var exists = await Workspace.ExistsAsync(pagePath, ct);

        if (kind == KbEditKind.Create && exists)
            return new KbEditPlan(Fail($"{pagePath} already exists. Read it, then use {UpdatePageTool}."), pagePath, 0);
        if (kind != KbEditKind.Create && !exists)
            return new KbEditPlan(Fail($"No page at {pagePath}"), pagePath, 0);
        if (kind != KbEditKind.Delete)
        {
            if (string.IsNullOrWhiteSpace(markdown)) return new KbEditPlan(Fail("The page's Markdown is required."), pagePath, 0);
            if (Encoding.UTF8.GetByteCount(markdown) > MaxPageBytes)
                return new KbEditPlan(Fail("The page is over the 1 MB limit."), pagePath, 0);
        }

        var ticks = 0L;
        if (exists)
        {
            var current = await Workspace.ReadPageAsync(pagePath, ct);
            ticks = current.ModifiedTicks;
            if (kind == KbEditKind.Update && current.Markdown == markdown)
                return new KbEditPlan(Fail($"The new text of {pagePath} is the same as the current text."), pagePath, ticks);
        }

        var summary = kind switch
        {
            KbEditKind.Create => $"Create the knowledge base page {pagePath}",
            KbEditKind.Update => $"Replace the text of the knowledge base page {pagePath}",
            _ => $"Delete the knowledge base page {pagePath}",
        };
        var preview = new MutationResult
        {
            Success = true, IsPreview = true, Operation = tool, Entity = pagePath, Summary = summary,
            AffectedFiles = [$"{StoryKnowledgeBaseProvider.FolderName}/{pagePath}"],
        };
        return new KbEditPlan(preview, pagePath, ticks);
    }

    /// <summary>Writes an edit that <see cref="PlanAsync"/> accepted. Fails, and writes nothing, when the page changed since.</summary>
    public async Task<MutationResult> ApplyAsync(KbEditKind kind, KbEditPlan plan, string? markdown, CancellationToken ct = default)
    {
        var result = new MutationResult { Operation = ToolName(kind), Entity = plan.PagePath, Summary = plan.Preview.Summary, AffectedFiles = plan.Preview.AffectedFiles };
        try
        {
            if (kind == KbEditKind.Delete)
            {
                var current = await Workspace.ReadPageAsync(plan.PagePath, ct);
                if (current.ModifiedTicks != plan.ModifiedTicks)
                {
                    result.Error = $"{plan.PagePath} changed since the change was proposed.";
                    return result;
                }
                await Workspace.DeleteAsync(plan.PagePath, ct);
            }
            else
            {
                var save = await Workspace.SavePageAsync(plan.PagePath, markdown ?? "", kind == KbEditKind.Create ? 0 : plan.ModifiedTicks, ct);
                if (save.Status is SaveStatus.Conflict or SaveStatus.AlreadyExists)
                {
                    result.Error = save.Status == SaveStatus.Conflict ? $"{plan.PagePath} changed since the change was proposed." : save.Message ?? $"{plan.PagePath} already exists.";
                    return result;
                }
            }
            result.Success = true;
        }
        catch (KnowledgeNotFoundException) { result.Error = $"No page at {plan.PagePath}"; }
        catch (KnowledgeException ex) { result.Error = ex.Message; }
        return result;
    }

    const int MaxPageBytes = 1_048_576;
}

public enum KbEditKind { Create, Update, Delete }

/// <summary>A checked page edit: what the user sees, the page it targets, and the page's ticks when it was checked (0 = new).</summary>
public sealed record KbEditPlan(MutationResult Preview, string PagePath, long ModifiedTicks);
