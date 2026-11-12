using WritingIDE.Core.Import;
using WritingIDE.Core.Model;

namespace WritingIDE.Core.Tests;

public sealed class ImportTests {
    private const string Sample = """
        Sure! Here is your material:

        ```
        === NOVEL: The Lighthouse Keeper ===
        Author: A. Writer
        Genre: Mystery
        Target words: 90,000

        === CHARACTER: Elena Marsh ===
        Aliases: Elena, Marsh
        Summary: Keeper of the light.
        - **Role:** Protagonist
        Age: 41
        Notes:
        Lost her brother.
        Still looks for him.

        === PLACE: Greyhaven Lighthouse ===
        Summary: A white tower.
        Type: Lighthouse

        ### ITEM: Brass Compass
        Owner: Tobias

        === CHAPTER: Chapter 1 - The Light ===
        Synopsis: Elena finds a compass.
        Status: Draft
        ---
        The wind had been rising since noon.

        "Who's there?" she whispered: nobody answered.
        ```
        """;

    [Fact]
    public void ParsesEveryBlockType() {
        ImportDocument document = ImportParser.Parse(Sample);

        Assert.False(document.IsPlainText);
        Assert.Equal("A. Writer", document.Novel!.Author);
        Assert.Equal(90000, document.Novel.TargetWordCount);

        ImportedEntity elena = Assert.Single(document.Entities, e => e.Kind == EntityKind.Character);
        Assert.Equal("Elena Marsh", elena.Name);
        Assert.Equal("Elena, Marsh", elena.Aliases);
        Assert.Equal("Keeper of the light.", elena.Summary);
        Assert.Contains(new KeyValuePair<string, string>("Role", "Protagonist"), elena.Attributes);
        Assert.Contains("Still looks for him.", elena.Notes);
        Assert.Contains(document.Entities, e => (e.Kind == EntityKind.Item) && (e.Name == "Brass Compass"));

        ImportedChapter chapter = Assert.Single(document.Chapters);
        Assert.Equal("Chapter 1 - The Light", chapter.Title);
        Assert.Equal(ChapterStatus.Draft, chapter.Status);
        Assert.StartsWith("The wind", chapter.Text);
        Assert.EndsWith("nobody answered.", chapter.Text);
        Assert.Contains(document.Warnings, w => w.Message.Contains("before the first block"));
    }

    [Fact]
    public void PlainTextIsSplitAtChapterHeadings() {
        ImportDocument document = ImportParser.Parse("Prologue\nIt began.\n\nChapter One\nShe woke.\n\n# Chapter 2: Night\nShe slept.");

        Assert.True(document.IsPlainText);
        Assert.Equal(["Prologue", "Chapter One", "Chapter 2: Night"], document.Chapters.Select(c => c.Title));
        Assert.Equal("She slept.", document.Chapters[2].Text);
    }

    [Fact]
    public void PlainTextWithoutHeadingsBecomesOneChapter() {
        ImportDocument document = ImportParser.Parse("Part of me wanted to stay.\nBut I left.", "My Draft");

        ImportedChapter chapter = Assert.Single(document.Chapters);
        Assert.Equal("My Draft", chapter.Title);
    }

    [Fact]
    public void UpdateModeMergesExistingElementsAndReplacesChapterText() {
        NovelProject project = new();
        StoryEntity existing = new(EntityKind.Character, "Elena Marsh") { Aliases = "Lena", Notes = "Old notes." };
        existing.Attributes.Add(new EntityAttribute("Age", "40"));
        project.Entities.Add(existing);
        project.Chapters.Add(new Chapter("Chapter 1 - The Light") { Text = "old" });

        ImportSummary summary = ImportApplier.Apply(project, ImportParser.Parse(Sample), ImportConflictMode.Update);

        Assert.Equal(1, summary.EntitiesUpdated);
        Assert.Equal(2, summary.EntitiesAdded);
        Assert.Equal(1, summary.ChaptersUpdated);
        Assert.Equal("Lena, Elena, Marsh", existing.Aliases);
        Assert.Equal("41", existing.Attributes.Single(a => a.Key == "Age").Value);
        Assert.StartsWith("Old notes.", existing.Notes);
        Assert.Contains("Lost her brother.", existing.Notes);
        Assert.StartsWith("The wind", project.Chapters.Single().Text);
        Assert.Equal("A. Writer", project.Author);
    }

    [Fact]
    public void SkipAndAddNewModesHandleDuplicates() {
        NovelProject skipProject = new();
        skipProject.Chapters.Add(new Chapter("Chapter 1 - The Light") { Text = "old" });
        ImportSummary skipped = ImportApplier.Apply(skipProject, ImportParser.Parse(Sample), ImportConflictMode.Skip);
        Assert.Equal(1, skipped.Skipped);
        Assert.Equal("old", skipProject.Chapters.Single().Text);

        NovelProject addProject = new();
        addProject.Chapters.Add(new Chapter("Chapter 1 - The Light") { Text = "old" });
        ImportApplier.Apply(addProject, ImportParser.Parse(Sample), ImportConflictMode.AddNew);
        Assert.Equal(["Chapter 1 - The Light", "Chapter 1 - The Light (imported)"], addProject.Chapters.Select(c => c.Title));
    }

    [Fact]
    public void PromptExampleRoundTripsThroughTheParser() {
        ImportPromptOptions options = new(ImportPromptMode.DevelopMyIdea, true, new HashSet<EntityKind>(Enum.GetValues<EntityKind>()), 3, "A lighthouse keeper.", []);
        string prompt = ImportPrompt.Build(options);
        string example = prompt[(prompt.IndexOf("EXAMPLE", StringComparison.Ordinal) + 7)..prompt.IndexOf("MY STORY IDEA", StringComparison.Ordinal)];

        ImportDocument document = ImportParser.Parse(example);

        Assert.Empty(document.Warnings);
        Assert.Equal(4, document.Entities.Count);
        Assert.Single(document.Chapters);
        Assert.Contains("write 3 chapters", prompt);
        Assert.EndsWith("A lighthouse keeper.", prompt);
    }
}