using System.Text.Json;
using AIStoryBuildersV2.Shared.Graph;
using AIStoryBuildersV2.Shared.Knowledge;
using AIStoryBuildersV2.Shared.KnowledgeAssist;
using AIStoryBuildersV2.Shared.Models;
using AIStoryBuildersV2.Shared.Services;
using AIStoryBuildersV2.Shared.Settings;
using AIStoryBuildersV2.Shared.Stories;
using AIStoryBuildersV2.Shared.StoryChat;
using Microsoft.Extensions.AI;

namespace AIStoryBuildersV2.Tests;

/// <summary>Plan §10: kb_ tools, KnowledgeContextRetriever, and "Search knowledgebase" in chat and paragraph writing (D14-D16).</summary>
public class SearchKnowledgeBaseTests : IDisposable
{
    const string Title = FixtureFactory.Title;
    readonly ServiceFixture _f;
    readonly ScriptedChatClient _chat = new();
    readonly StoryKnowledgeBaseProvider _kbs;

    public SearchKnowledgeBaseTests()
    {
        _f = new ServiceFixture(chat: _chat);
        _kbs = new StoryKnowledgeBaseProvider(new TestHost(_f.Dir.Path), new PermanentFileDeleter(), _f.Persistence);
    }

    public void Dispose() => _f.Dispose();

    async Task<IStoryKnowledgeBase> SeedAsync()
    {
        var kb = await _kbs.OpenAsync(Title);
        await kb.Workspace.SavePageAsync("Lore/Necklace.md", "---\ntitle: The Necklace\n---\nThe Necklace was forged by Hrafnkel from nine silver beads.\n", 0);
        await kb.Workspace.SavePageAsync("Hrafnkel.md", "# Hrafnkel\nA smith in the fjord village.\n", 0);
        return kb;
    }

    KnowledgeContextRetriever Retriever(IChatClientProvider? clients = null) =>
        new(_kbs, clients ?? new FakeChatClientProvider(_chat), _f.Log);

    static string Arg(FunctionCallContent call, string name) => call.Arguments![name]!.ToString()!;

    // ── KnowledgeBaseTools ──────────────────────────────────────────

    [Fact]
    public async Task Tools_are_read_only_and_prefixed()
    {
        var tools = new KnowledgeBaseTools(await SeedAsync()).CreateReadOnly();

        Assert.Equal(["kb_get_links", "kb_list_folder", "kb_read_page", "kb_search_pages"], tools.Select(t => t.Name).Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task Read_page_truncates_long_pages()
    {
        var kb = await SeedAsync();
        await kb.Workspace.SavePageAsync("Long.md", new string('x', 500), 0);

        var json = JsonDocument.Parse(await new KnowledgeBaseTools(kb, maxToolResultChars: 100).ReadPageAsync("Long.md")).RootElement;

        Assert.True(json.GetProperty("truncated").GetBoolean());
        Assert.Equal(100, json.GetProperty("markdown").GetString()!.Length);
    }

    [Theory]
    [InlineData("../AIStoryBuildersStories.csv")]
    [InlineData("../../secret.md")]
    [InlineData("C:/Windows/win.ini")]
    public async Task Paths_stay_inside_the_story_knowledge_base(string path)
    {
        var tools = new KnowledgeBaseTools(await SeedAsync());

        Assert.Contains("\"error\"", await tools.ReadPageAsync(path));
        Assert.Contains("\"error\"", await tools.ListFolderAsync(path));
    }

    [Fact]
    public async Task Search_finds_pages_with_snippets()
    {
        var json = await new KnowledgeBaseTools(await SeedAsync()).SearchPagesAsync("forged");

        Assert.Contains("Lore/Necklace.md", json);
    }

    // ── KnowledgeContextRetriever ───────────────────────────────────

    [Fact]
    public async Task Retriever_returns_cited_notes()
    {
        await SeedAsync();
        _chat.EnqueueCalls(ScriptedChatClient.Call("c1", "kb_search_pages", ("query", "necklace")))
             .EnqueueCalls(ScriptedChatClient.Call("c2", "kb_read_page", ("path", "Lore/Necklace.md")))
             .EnqueueText("- The Necklace has nine silver beads. [[Lore/Necklace]]");

        var ctx = await Retriever().RetrieveAsync(Title, "Freya's necklace", "test-model", CancellationToken.None);

        Assert.Null(ctx.Warning);
        Assert.Contains("nine silver beads", ctx.Notes);
        Assert.Equal(["Lore/Necklace"], ctx.CitedPages);
        var read = _chat.Requests[2].SelectMany(m => m.Contents).OfType<FunctionResultContent>().Last();
        Assert.Contains("forged by Hrafnkel", read.Result?.ToString());
    }

    [Fact]
    public async Task No_relevant_pages_gives_empty_notes()
    {
        await SeedAsync();
        _chat.EnqueueText(KnowledgeAssistConstants.NoRelevantPages);

        var ctx = await Retriever().RetrieveAsync(Title, "dragons", "test-model", CancellationToken.None);

        Assert.Equal("", ctx.Notes);
        Assert.Null(ctx.Warning);
    }

    [Fact]
    public async Task A_provider_error_gives_a_warning_and_no_exception()
    {
        await SeedAsync();
        _chat.EnqueueFailure(new HttpRequestException("503 Service Unavailable"));

        var ctx = await Retriever().RetrieveAsync(Title, "necklace", "test-model", CancellationToken.None);

        Assert.Equal("", ctx.Notes);
        Assert.Contains("503", ctx.Warning);
    }

    [Fact]
    public async Task An_unverified_model_gives_a_warning()
    {
        await SeedAsync();
        var ctx = await Retriever(new FakeChatClientProvider(null, "Model 'x' has not passed the capability check. Verify it in Settings."))
            .RetrieveAsync(Title, "necklace", "x", CancellationToken.None);

        Assert.Contains("capability check", ctx.Warning);
        Assert.Empty(_chat.Requests);
    }

    [Fact]
    public async Task The_loop_stops_after_four_model_calls_and_the_last_has_no_tools()
    {
        await SeedAsync();
        for (var i = 0; i < 6; i++) _chat.EnqueueCalls(ScriptedChatClient.Call("c" + i, "kb_search_pages", ("query", "x")));

        var ctx = await Retriever().RetrieveAsync(Title, "necklace", "test-model", CancellationToken.None);

        Assert.Equal(KnowledgeAssistConstants.MaxIterations, _chat.Requests.Count);
        Assert.NotNull(_chat.Options[0]?.Tools);
        Assert.Null(_chat.Options[^1]?.Tools);
        Assert.Equal("", ctx.Notes);
        Assert.NotNull(ctx.Warning);
    }

    [Fact]
    public async Task Notes_are_capped_at_4000_characters()
    {
        await SeedAsync();
        _chat.EnqueueText("[[Lore/Necklace]] " + new string('n', 5000));

        var ctx = await Retriever().RetrieveAsync(Title, "necklace", "test-model", CancellationToken.None);

        Assert.Equal(KnowledgeAssistConstants.MaxKnowledgeNotesChars, ctx.Notes.Length);
        Assert.True(ctx.Truncated);
    }

    [Fact]
    public async Task An_empty_knowledge_base_needs_no_model_call()
    {
        var ctx = await Retriever().RetrieveAsync(Title, "necklace", "test-model", CancellationToken.None);

        Assert.Equal(KnowledgeContext.Empty, ctx);
        Assert.Empty(_chat.Requests);
    }

    // ── Chat and paragraph surfaces ─────────────────────────────────

    StoryChatService Chat() => new(new GraphQueryService(_f.GraphCache), new GraphMutationService(_f.Service, new GraphBuilder(), _f.GraphCache),
        new FakeChatClientProvider(_chat), new StubSettings(), _f.Service, _f.GraphCache, _f.Log, _kbs);

    static async Task SendAsync(IStoryChatService chat, bool kb)
    {
        await foreach (var _ in chat.SendMessageAsync(new StoryChatRequest(Title, "What is the Necklace?", "test-model", kb))) { }
    }

    [Fact]
    public async Task Chat_offers_kb_tools_and_the_addendum_only_when_checked()
    {
        await SeedAsync();
        _chat.EnqueueText("unchecked").EnqueueText("checked");
        var chat = Chat();

        await SendAsync(chat, kb: false);
        await SendAsync(chat, kb: true);

        Assert.DoesNotContain(_chat.Options[0]!.Tools!, t => t.Name.StartsWith("kb_", StringComparison.Ordinal));
        Assert.DoesNotContain("knowledge base", _chat.Requests[0][0].Text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(_chat.Options[1]!.Tools!, t => t.Name == "kb_read_page");
        Assert.Contains("The story has a knowledge base.", _chat.Requests[1][0].Text);
    }

    [Fact]
    public async Task Chat_reads_the_story_knowledge_base_through_kb_tools()
    {
        await SeedAsync();
        _chat.EnqueueCalls(ScriptedChatClient.Call("c1", "kb_read_page", ("path", "Lore/Necklace.md")))
             .EnqueueText("Hrafnkel forged it. [[Lore/Necklace]]");

        await SendAsync(Chat(), kb: true);

        var result = _chat.Requests[1].SelectMany(m => m.Contents).OfType<FunctionResultContent>().Single();
        Assert.Contains("nine silver beads", result.Result?.ToString());
    }

    [Fact]
    public async Task Chat_with_an_empty_knowledge_base_offers_no_kb_tools()
    {
        _chat.EnqueueText("ok");

        await SendAsync(Chat(), kb: true);

        Assert.DoesNotContain(_chat.Options[0]!.Tools!, t => t.Name.StartsWith("kb_", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Checkbox_defaults_follow_the_knowledge_base_and_are_remembered_per_surface()
    {
        var choice = new KnowledgeSearchChoice(new AppSession(), _kbs);
        Assert.Equal((true, false), await choice.DefaultAsync(Title, KnowledgeSearchChoice.Chat));

        await SeedAsync();
        Assert.Equal((false, true), await choice.DefaultAsync(Title, KnowledgeSearchChoice.Chat));

        choice.Remember(Title, KnowledgeSearchChoice.Chat, false);
        Assert.Equal((false, false), await choice.DefaultAsync(Title, KnowledgeSearchChoice.Chat));
        Assert.Equal((false, true), await choice.DefaultAsync(Title, KnowledgeSearchChoice.Paragraph));
    }

    ParagraphAiHelper Helper() =>
        new(_f.Service, _f.Orchestrator, new KnowledgeSearchChoice(new AppSession(), _kbs), Retriever());

    async Task<(Story Story, Chapter Chapter, Paragraph Paragraph, List<Character> Characters)> FixtureParagraphAsync()
    {
        var story = (await _f.Service.GetStorysAsync()).Single();
        var chapter = _f.Service.GetChapters(story).First();
        var paragraph = _f.Service.GetParagraphs(chapter).First();
        return (story, chapter, paragraph, _f.Service.GetCharacters(story));
    }

    [Fact]
    public async Task A_checked_generate_puts_knowledge_base_notes_in_the_prompt()
    {
        await SeedAsync();
        var (story, chapter, paragraph, characters) = await FixtureParagraphAsync();
        _chat.EnqueueText("- The Necklace has nine silver beads. [[Lore/Necklace]]")
             .EnqueueText("{\"paragraph_content\":\"Freya counted nine beads.\"}");
        var prompt = new AIPrompt { AIPromptText = "Freya looks at her necklace", NumberOfWords = "50", ModelId = "test-model", SearchKnowledgeBase = true };
        var warnings = new List<string>();

        var text = await Helper().GenerateAsync(story, chapter, paragraph, characters, prompt, warnings.Add);

        Assert.Equal("Freya counted nine beads.", text);
        Assert.Empty(warnings);
        Assert.Contains("Freya looks at her necklace", _chat.Requests[0][^1].Text);
        var write = _chat.Requests[^1][^1].Text;
        Assert.Contains("<knowledge_base_notes>", write);
        Assert.Contains("nine silver beads", write);
    }

    [Fact]
    public async Task An_unchecked_generate_never_touches_the_knowledge_base()
    {
        await SeedAsync();
        var (story, chapter, paragraph, characters) = await FixtureParagraphAsync();
        _chat.EnqueueText("{\"paragraph_content\":\"Freya waited.\"}");
        var prompt = new AIPrompt { AIPromptText = "Freya waits", NumberOfWords = "50", ModelId = "test-model", SearchKnowledgeBase = false };

        await Helper().GenerateAsync(story, chapter, paragraph, characters, prompt, _ => { });

        Assert.Single(_chat.Requests);
        Assert.DoesNotContain("knowledge_base_notes", _chat.Requests[0][^1].Text);
        Assert.Null(_chat.Options[0]?.Tools);
    }

    [Fact]
    public async Task A_failed_search_warns_and_still_writes_the_paragraph()
    {
        await SeedAsync();
        var (story, chapter, paragraph, characters) = await FixtureParagraphAsync();
        _chat.EnqueueFailure(new HttpRequestException("timeout"))
             .EnqueueText("{\"paragraph_content\":\"Freya waited.\"}");
        var prompt = new AIPrompt { AIPromptText = "Freya waits", NumberOfWords = "50", ModelId = "test-model", SearchKnowledgeBase = true };
        var warnings = new List<string>();

        var text = await Helper().GenerateAsync(story, chapter, paragraph, characters, prompt, warnings.Add);

        Assert.Equal("Freya waited.", text);
        Assert.Single(warnings);
        Assert.DoesNotContain("knowledge_base_notes", _chat.Requests[^1][^1].Text);
    }
}
