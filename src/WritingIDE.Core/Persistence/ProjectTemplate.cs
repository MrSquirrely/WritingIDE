using WritingIDE.Core.Model;

namespace WritingIDE.Core.Persistence;

/// <summary>
/// A "New Project" template, like the project templates in Visual Studio. <paramref name="afterCreate"/> runs once the
/// project has been saved to its folder, for content that lives in files (snapshots, revision baselines).
/// </summary>
public sealed class ProjectTemplate(string title, string description, string glyph, Func<string, string, NovelProject> factory, Action<NovelProject>? afterCreate = null) {
    public string Title { get; } = title;

    public string Description { get; } = description;

    public string Glyph { get; } = glyph;

    public static IReadOnlyList<ProjectTemplate> All { get; } = [
        new("Empty Novel", "A blank manuscript with a single chapter. Add characters, places and chapters as you go.", "\uE8A5", CreateEmpty),
        new("Sample Novel", "\"The Lighthouse Keeper\": a short mystery that shows off every feature: deliberate grammar and continuity mistakes, scenes, comments, plot threads, a flashback on the timeline, relationships, snapshots and tracked changes.", "\uE82D", SampleNovel.Create, SampleNovel.Initialize),
        new("Short Story", "A single-document short story with a protagonist and a setting ready to fill in.", "\uE70B", CreateShortStory),
        new("Book Series", "Several books that share one cast, world and story bible, like a solution with several projects. Starts with Book 1; add books as the series grows.", "\uE8F1", CreateSeries)
    ];

    public static ProjectTemplate Series => All.Single(static template => template.Title == "Book Series");

    private static NovelProject CreateSeries(string name, string author) {
        NovelProject project = new() { Name = name, Author = author, Kind = ProjectKind.Series, TargetWordCount = 300000 };
        Book first = new($"{name}, Book 1");
        project.Books.Add(first);
        project.Chapters.Add(new Chapter("Chapter 1") { BookId = first.Id });
        return project;
    }

    public NovelProject Create(string name, string author) {
        NovelProject project = factory(name, author);
        project.IsDirty = false;
        return project;
    }

    /// <summary>Called by <see cref="ProjectSerializer.Create"/> after the first save.</summary>
    public void AfterCreate(NovelProject project) {
        afterCreate?.Invoke(project);
    }

    private static NovelProject CreateEmpty(string name, string author) {
        NovelProject project = new() { Name = name, Author = author };
        project.Chapters.Add(new Chapter("Chapter 1"));
        return project;
    }

    private static NovelProject CreateShortStory(string name, string author) {
        NovelProject project = new() { Name = name, Author = author, TargetWordCount = 7500, Genre = "Short fiction" };
        project.Chapters.Add(new Chapter(name) { Synopsis = "The whole story." });
        project.Entities.Add(StoryEntity.CreateWithDefaults(EntityKind.Character, "Protagonist"));
        project.Entities.Add(StoryEntity.CreateWithDefaults(EntityKind.Place, "Setting"));
        return project;
    }
}