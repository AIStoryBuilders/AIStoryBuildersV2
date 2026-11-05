using AIStoryBuildersV2.Shared.AI;
using AIStoryBuildersV2.Shared.Services;
using Character = AIStoryBuildersV2.Shared.Models.Character;

namespace AIStoryBuildersV2.Shared.Stories;

/// <summary>
/// The paragraph editor's Generate pipeline (UI-Behavior-Spec §15.2, plan §7.5): CreateMasterStory with V1's arguments,
/// then WriteParagraph with the chosen model. Knowledge-base retrieval (D15) is added by T16.
/// </summary>
public class ParagraphAiHelper(AIStoryBuildersService svc, OrchestratorMethods orchestrator, AppSession session)
{
    public const string Surface = "paragraph";

    /// <summary>D16: (empty, checked). Checked when the KB has pages, otherwise unchecked and disabled; remembered per session.</summary>
    public virtual Task<(bool Empty, bool Checked)> KnowledgeBaseDefaultAsync(string storyTitle) => Task.FromResult((true, false));

    public void RememberKnowledgeBaseChoice(string storyTitle, bool value) => session.SearchKnowledgeBase[(storyTitle, Surface)] = value;

    protected bool? RememberedChoice(string storyTitle) =>
        session.SearchKnowledgeBase.TryGetValue((storyTitle, Surface), out var v) ? v : null;

    /// <summary>Writes the paragraph with AI and returns the new text; the caller keeps the old text for Undo.</summary>
    public virtual async Task<string> GenerateAsync(Story story, Chapter chapter, Paragraph paragraph, IReadOnlyList<Character> storyCharacters,
        AIPrompt prompt, Action<string> warn, CancellationToken ct = default)
    {
        var before = svc.GetParagraphs(chapter).Where(p => p.Sequence < paragraph.Sequence).ToList();
        var names = paragraph.Characters.Select(c => c.CharacterName).ToHashSet(StringComparer.Ordinal);
        var characters = storyCharacters.Where(c => names.Contains(c.CharacterName)).ToList();
        chapter.Story = story;

        var master = await svc.CreateMasterStory(chapter, paragraph, characters, before, prompt);
        master.KnowledgeBaseNotes = await KnowledgeNotesAsync(story, chapter, paragraph, prompt, warn, ct);
        return await orchestrator.WriteParagraph(master, prompt, prompt.ModelId);
    }

    /// <summary>T16 adds retrieval here; without it the notes are empty.</summary>
    protected virtual Task<string?> KnowledgeNotesAsync(Story story, Chapter chapter, Paragraph paragraph, AIPrompt prompt, Action<string> warn, CancellationToken ct) =>
        Task.FromResult<string?>(null);
}
