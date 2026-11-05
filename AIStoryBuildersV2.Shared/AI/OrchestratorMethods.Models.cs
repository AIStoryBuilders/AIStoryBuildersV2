#nullable disable
using System.ClientModel;
using System.ClientModel.Primitives;
using System.Net.Http.Headers;
using OpenAI;

namespace AIStoryBuildersV2.Shared.AI;

// Fine-tuned models (F18, D23). OpenAI only: the key comes from the saved settings when the provider is OpenAI.
// V1's Organization setting is dropped (D31). Aliases live in AIStoryBuildersDatabase.json / asb2:db.
public partial class OrchestratorMethods
{
    async Task<string> RequireOpenAIKeyAsync()
    {
        var key = await _openAI.GetOpenAIKeyAsync();
        if (string.IsNullOrWhiteSpace(key)) throw new InvalidOperationException("Fine-tuning is available only when the AI provider is OpenAI.");
        return key;
    }

    public async Task<List<AIStoryBuilderModel>> ListFineTunedModelsAsync()
    {
        var client = new OpenAIClient(new ApiKeyCredential(await RequireOpenAIKeyAsync()), new OpenAIClientOptions { Transport = new HttpClientPipelineTransport(_http) });
        var response = await client.GetOpenAIModelClient().GetModelsAsync();
        var aliases = await DatabaseService.LoadDatabaseAsync();

        // Models owned by "openai" or "system", and checkpoints, are not fine-tunes of this account.
        return response.Value
            .Where(m => !m.OwnedBy.Contains("openai") && !m.OwnedBy.Contains("system") && !m.Id.Contains("-step-"))
            .Select(m => new AIStoryBuilderModel { ModelId = m.Id, ModelName = aliases.GetValueOrDefault(m.Id) ?? m.Id })
            .ToList();
    }

    public async Task UpdateModelNameAsync(AIStoryBuilderModel paramaModel)
    {
        var aliases = await DatabaseService.LoadDatabaseAsync();
        aliases[paramaModel.ModelId] = paramaModel.ModelName;
        await DatabaseService.SaveDatabase(aliases);
    }

    public async Task DeleteFineTuneModelAsync(AIStoryBuilderModel paramaModel)
    {
        using var request = new HttpRequestMessage(HttpMethod.Delete, $"https://api.openai.com/v1/models/{Uri.EscapeDataString(paramaModel.ModelId)}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await RequireOpenAIKeyAsync());
        using var response = await _http.SendAsync(request);
        response.EnsureSuccessStatusCode();

        var aliases = await DatabaseService.LoadDatabaseAsync();
        if (aliases.Remove(paramaModel.ModelId)) await DatabaseService.SaveDatabase(aliases);
    }
}
