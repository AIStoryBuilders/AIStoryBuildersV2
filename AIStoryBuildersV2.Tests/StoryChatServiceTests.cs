using System.ComponentModel;
using AIStoryBuildersV2.Shared.Graph;
using AIStoryBuildersV2.Shared.Settings;
using AIStoryBuildersV2.Shared.StoryChat;
using Microsoft.Extensions.AI;

namespace AIStoryBuildersV2.Tests;

/// <summary>Plan §11: Story Chat on the shared client, graph tools per turn, kb_ tools only on request.</summary>
public class StoryChatServiceTests : IDisposable
{
    readonly ServiceFixture _f = new();
    readonly ScriptedChatClient _chat = new();

    public void Dispose() => _f.Dispose();

    /// <summary>Supplies one kb_ tool so the tests can see when the service offers it.</summary>
    sealed class KbStubChatService(ServiceFixture f, IChatClientProvider clients)
        : StoryChatService(new GraphQueryService(f.GraphCache), new GraphMutationService(f.Service, new GraphBuilder(), f.GraphCache),
            clients, new StubSettings(), f.Service, f.GraphCache, f.Log)
    {
        protected override Task<IList<AITool>> KnowledgeBaseToolsAsync(string storyTitle, CancellationToken ct) =>
            Task.FromResult<IList<AITool>>([AIFunctionFactory.Create([Description("stub")] () => "[]", "kb_search_pages")]);
    }

    StoryChatService Create(IChatClientProvider? clients = null) => new KbStubChatService(_f, clients ?? new FakeChatClientProvider(_chat));

    static async Task<string> SendAsync(IStoryChatService chat, StoryChatRequest request)
    {
        var text = "";
        await foreach (var chunk in chat.SendMessageAsync(request)) text += chunk;
        return text;
    }

    static StoryChatRequest Ask(string message, bool kb = false) => new(FixtureFactory.Title, message, "test-model", kb);

    static IEnumerable<string> ToolNames(ChatOptions? options) => options?.Tools?.Select(t => t.Name) ?? [];

    [Fact]
    public async Task Graph_tools_are_always_offered_and_kb_tools_only_when_requested()
    {
        _chat.EnqueueText("one").EnqueueText("two");
        var chat = Create();

        await SendAsync(chat, Ask("Who is Balt?"));
        await SendAsync(chat, Ask("Who is Balt?", kb: true));

        Assert.Contains("GetCharacters", ToolNames(_chat.Options[0]));
        Assert.DoesNotContain(ToolNames(_chat.Options[0]), n => n.StartsWith("kb_", StringComparison.Ordinal));
        Assert.Contains("GetCharacters", ToolNames(_chat.Options[1]));
        Assert.Contains("kb_search_pages", ToolNames(_chat.Options[1]));
    }

    [Fact]
    public async Task An_unverified_model_returns_the_provider_reason_without_calling_a_model()
    {
        const string reason = "Model 'x' has not passed the capability check. Verify it in Settings.";
        var chat = Create(new FakeChatClientProvider(null, reason));

        var answer = await SendAsync(chat, Ask("Hello"));

        Assert.Equal(reason, answer);
        Assert.Empty(_chat.Requests);
    }

    [Fact]
    public async Task Tool_calls_run_against_the_requested_story_graph()
    {
        _chat.EnqueueCalls(ScriptedChatClient.Call("c1", "GetCharacters"))
             .EnqueueText("Balt and Freya.");
        var chat = Create();

        var answer = await SendAsync(chat, Ask("Who are the characters?"));

        Assert.Equal("Balt and Freya.", answer);
        var toolResult = _chat.Requests[1].SelectMany(m => m.Contents).OfType<FunctionResultContent>().Single();
        Assert.Contains("Balt", toolResult.Result?.ToString());
        Assert.Null(StoryGraphContext.Current);
    }

    [Fact]
    public async Task A_mutation_preview_writes_nothing()
    {
        _chat.EnqueueCalls(ScriptedChatClient.Call("c1", "AddCharacter", ("name", "Mira"), ("type", "Supporting"),
                ("description", "A smith."), ("timelineName", null), ("confirmed", false)))
             .EnqueueText("Shall I add Mira?");
        var chat = Create();
        await _f.Service.EnsureGraphExistsAsync(FixtureFactory.Title);
        var before = Directory.GetFiles(_f.StoryDir, "*", SearchOption.AllDirectories).Select(File.ReadAllText).ToList();
        _f.Persistence.Dirty.Clear();

        var answer = await SendAsync(chat, Ask("Add a smith called Mira"));

        Assert.Equal("Shall I add Mira?", answer);
        var after = Directory.GetFiles(_f.StoryDir, "*", SearchOption.AllDirectories).Select(File.ReadAllText).ToList();
        Assert.Equal(before, after);
        Assert.False(File.Exists(Path.Combine(_f.StoryDir, "Characters", "Mira.csv")));
    }

    [Fact]
    public async Task History_is_kept_per_story_and_cleared_by_ClearSession()
    {
        _chat.EnqueueText("first").EnqueueText("second").EnqueueText("third");
        var chat = Create();

        await SendAsync(chat, Ask("Q1"));
        await SendAsync(chat, Ask("Q2"));
        Assert.Contains(_chat.Requests[1], m => m.Role == ChatRole.Assistant && m.Text == "first");

        chat.ClearSession(FixtureFactory.Title);
        await SendAsync(chat, Ask("Q3"));
        Assert.DoesNotContain(_chat.Requests[2], m => m.Role == ChatRole.Assistant);
    }

    [Fact]
    public async Task A_failed_turn_is_not_added_to_the_history()
    {
        _chat.EnqueueFailure(new HttpRequestException("timeout")).EnqueueText("ok");
        var chat = Create();

        await Assert.ThrowsAsync<HttpRequestException>(() => SendAsync(chat, Ask("Q1")));
        await SendAsync(chat, Ask("Q1"));

        Assert.Single(_chat.Requests[1], m => m.Role == ChatRole.User);
    }
}
