using AIStoryBuildersV2.Shared.Models;
using AIStoryBuildersV2.Shared.Stories;

namespace AIStoryBuildersV2.Tests;

/// <summary>
/// Plan §6.2: every method in the MarkDirty list calls <see cref="IStoryPersistence.MarkDirty"/> once with the story's title.
/// Add a case here for every new method that writes under {Title}/.
/// </summary>
public class MarkDirtyCoverageTests
{
    static readonly Dictionary<string, Func<ServiceFixture, Task>> Methods = new()
    {
        ["AddTimeline"] = f => { f.Service.AddTimeline(new Timeline { Story = f.Story, TimelineName = "T", TimelineDescription = "", StartDate = DateTime.Now }); return Task.CompletedTask; },
        ["UpdateTimeline"] = f => { f.Service.UpdateTimeline(Timeline(f), "Present Day"); return Task.CompletedTask; },
        ["UpdateTimelineAndTimelineNameAsync"] = f => f.Service.UpdateTimelineAndTimelineNameAsync(Timeline(f, "Now"), "Present Day"),
        ["DeleteTimelineAndTimelineNameAsync"] = f => f.Service.DeleteTimelineAndTimelineNameAsync(Timeline(f), "Present Day"),

        ["AddLocationAsync"] = f => f.Service.AddLocationAsync(new Location { Story = f.Story, LocationName = "Harbor", LocationDescription = [new LocationDescription { Description = "x" }] }),
        ["UpdateLocationDescriptions"] = f => f.Service.UpdateLocationDescriptions(new Location { Story = f.Story, LocationName = "Ridge", LocationDescription = [new LocationDescription { Description = "x" }] }),
        ["UpdateLocationName"] = f => { f.Service.UpdateLocationName(new Location { Story = f.Story, LocationName = "Peak" }, "Ridge"); return Task.CompletedTask; },
        ["DeleteLocation"] = f => { f.Service.DeleteLocation(new Location { Story = f.Story, LocationName = "Ridge" }); return Task.CompletedTask; },

        ["AddUpdateCharacterAsync"] = f => f.Service.AddUpdateCharacterAsync(new Character { Story = f.Story, CharacterName = "Odin", CharacterBackground = [] }, "Odin"),
        ["UpdateCharacterNameAsync"] = f => f.Service.UpdateCharacterNameAsync(new Character { Story = f.Story, CharacterName = "Freyja" }, "Freya"),
        ["DeleteCharacter"] = f => { f.Service.DeleteCharacter(new Character { Story = f.Story, CharacterName = "Father" }, "Father"); return Task.CompletedTask; },
    };

    static Timeline Timeline(ServiceFixture f, string? rename = null)
    {
        var t = f.Service.GetTimelines(f.Story).Single(x => x.TimelineName == "Present Day");
        t.Story = f.Story;
        if (rename is not null) t.TimelineName = rename;
        return t;
    }

    public static TheoryData<string> Names => new(Methods.Keys);

    [Theory]
    [MemberData(nameof(Names))]
    public async Task Method_marks_the_story_dirty_once(string method)
    {
        using var culture = new CultureScope("en-US");
        using var f = new ServiceFixture();
        await Methods[method](f);
        Assert.Equal([FixtureFactory.Title], f.Persistence.Dirty);
    }
}
