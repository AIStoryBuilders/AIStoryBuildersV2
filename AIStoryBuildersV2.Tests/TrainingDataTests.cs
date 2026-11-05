using System.Net;
using System.Text;
using AIStoryBuildersV2.Shared.AI;
using AIStoryBuildersV2.Shared.Models;
using AIStoryBuildersV2.Shared.Stories;
using Microsoft.Extensions.AI;
using RichardSzalay.MockHttp;

namespace AIStoryBuildersV2.Tests;

/// <summary>F18: training data, the Excel export, the JsonL utility and the OpenAI upload (UI-Behavior-Spec §8).</summary>
public class TrainingDataTests : IDisposable
{
    readonly ScriptedChatClient _chat = new() { Fallback = () => new ScriptStep([new ChatResponseUpdate(ChatRole.Assistant, "A beat.")]) };
    readonly ServiceFixture _f;

    public TrainingDataTests() => _f = new ServiceFixture(chat: _chat);

    public void Dispose() => _f.Dispose();

    static readonly TrainingData[] Rows =
    [
        new() { System = "S1", User = "Beat \"one\"", Assistant = "Freya ran.é" },
        new() { System = "S2", User = "Beat two", Assistant = "Balt waited." },
    ];

    [Fact]
    public void Jsonl_lines_match_V1_conversation_json()
    {
        var lines = TrainingDataJsonl.ToJsonl(Rows).Split('\n', StringSplitOptions.RemoveEmptyEntries);

        Assert.Equal(2, lines.Length);
        var expected = new Conversation([new Message(Role.System, "S1"), new Message(Role.User, "Beat \"one\""), new Message(Role.Assistant, "Freya ran.é")]).ToString();
        Assert.Equal(expected, lines[0]);
        using var doc = System.Text.Json.JsonDocument.Parse(lines[0]);
        var messages = doc.RootElement.GetProperty("messages");
        Assert.Equal(["system", "user", "assistant"], messages.EnumerateArray().Select(m => m.GetProperty("role").GetString()));
        Assert.Equal("Beat \"one\"", messages[1].GetProperty("content").GetString());
    }

    [Fact]
    public void Excel_export_reads_back_through_the_JsonL_utility()
    {
        var bytes = _f.Service.ExportTrainingData([.. Rows]);

        var read = TrainingDataJsonl.FromExcel(bytes);

        Assert.Equal(Rows.Select(r => (r.System, r.User, r.Assistant)), read.Select(r => (r.System, r.User, r.Assistant)));
    }

    [Fact]
    public void The_embedded_template_is_a_readable_workbook()
    {
        using var template = typeof(AIStoryBuildersService).Assembly.GetManifestResourceStream("FineTunedTemplate.xls");
        Assert.NotNull(template);
        using var copy = new MemoryStream();
        template!.CopyTo(copy);

        var rows = TrainingDataJsonl.FromExcel(copy.ToArray());

        Assert.All(rows, r => Assert.NotNull(r.Assistant));
    }

    [Fact]
    public async Task Training_data_has_one_row_per_paragraph_line_with_its_story_beats()
    {
        var story = (await _f.Service.GetStorysAsync()).Single();
        var lines = _f.Service.GetChapters(story).SelectMany(c => _f.Service.GetParagraphs(c)).Sum(p => p.ParagraphContent.Split('\n').Length);

        var rows = await _f.Service.CreateTrainingDataAsync(story);

        Assert.Equal(lines, rows.Count);
        Assert.All(rows, r => Assert.Equal(AIStoryBuildersService.TrainingSystemMessage, r.System));
        Assert.All(rows, r => Assert.Equal("A beat.", r.User));
    }

    [Fact]
    public async Task Upload_sends_jsonl_then_starts_a_fine_tune_on_the_V1_base_model()
    {
        var mock = new MockHttpMessageHandler();
        string? uploadBody = null, jobBody = null, auth = null;
        mock.When(HttpMethod.Post, "https://api.openai.com/v1/files").Respond(async req =>
        {
            auth = req.Headers.Authorization?.ToString();
            uploadBody = await req.Content!.ReadAsStringAsync();
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"id\":\"file-123\"}", Encoding.UTF8, "application/json") };
        });
        mock.When(HttpMethod.Post, "https://api.openai.com/v1/fine_tuning/jobs").Respond(async req =>
        {
            jobBody = await req.Content!.ReadAsStringAsync();
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"id\":\"ftjob-1\"}", Encoding.UTF8, "application/json") };
        });
        var settings = new StubSettings();
        var orchestrator = new OrchestratorMethods(_f.Log, new DatabaseService(_f.AppData), _f.Embeddings, new FakeChatClientProvider(_chat),
            settings, settings, new HttpClient(mock));

        var fileId = await orchestrator.UploadTrainingDataAsync([.. Rows]);
        var jobId = await orchestrator.StartFineTuneAsync(fileId);

        Assert.Equal("file-123", fileId);
        Assert.Equal("ftjob-1", jobId);
        Assert.Equal("Bearer sk-test", auth);
        Assert.Contains("fine-tune", uploadBody);
        Assert.Contains(TrainingDataJsonl.ToJsonl(Rows).Split('\n')[0], uploadBody);
        Assert.Equal("{\"training_file\":\"file-123\",\"model\":\"gpt-3.5-turbo\"}", jobBody);
    }

    [Fact]
    public async Task Fine_tuning_needs_the_OpenAI_provider()
    {
        var settings = new StubSettings(Shared.Llm.LlmProvider.Anthropic);
        var orchestrator = new OrchestratorMethods(_f.Log, new DatabaseService(_f.AppData), _f.Embeddings, new FakeChatClientProvider(_chat),
            settings, settings, new HttpClient());

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => orchestrator.UploadTrainingDataAsync([.. Rows]));
        Assert.Equal("Fine-tuning is available only when the AI provider is OpenAI.", ex.Message);
    }
}
