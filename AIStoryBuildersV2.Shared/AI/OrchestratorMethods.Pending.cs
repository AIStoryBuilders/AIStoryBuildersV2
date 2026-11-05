#nullable disable
using Microsoft.Extensions.AI;

namespace AIStoryBuildersV2.Shared.AI;

// AI methods the story service calls. They are ported in T7b; until then they fail with a clear message.
public partial class OrchestratorMethods
{
    public virtual Task<string> ParseNewStory(string paramStoryTitle, string paramStoryText, string GPTModel) =>
        throw new NotImplementedException("ParseNewStory is ported in T7b.");

    public virtual Task<ChatResponse> CreateNewChapters(string JSONNewStory, string ChapterCount, string GPTModel) =>
        throw new NotImplementedException("CreateNewChapters is ported in T7b.");
}
