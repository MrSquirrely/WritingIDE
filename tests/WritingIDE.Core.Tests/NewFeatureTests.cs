using System.Text.RegularExpressions;

using WritingIDE.Core.Analysis;
using WritingIDE.Core.Grammar;
using WritingIDE.Core.Model;
using WritingIDE.Core.Persistence;
using WritingIDE.Core.Search;

namespace WritingIDE.Core.Tests;

public sealed class SearchTests {
    [Theory]
    [InlineData("Rowan", false, false, false, 3)]
    [InlineData("rowan", true, false, false, 0)]
    [InlineData("Row", false, true, false, 0)]
    [InlineData(@"Row\w+", false, false, true, 3)]
    public void FindsWithVisualStudioOptions(string pattern, bool matchCase, bool wholeWord, bool regex, int expected) {
        SearchQuery query = new(pattern, matchCase, wholeWord, regex);
        Assert.True(query.TryCreateRegex(out Regex? compiled, out _));
        Assert.Equal(expected, ManuscriptSearch.Find("Rowan met Rowan. Rowan's ribbon.", compiled!).Count);
    }

    [Fact]
    public void ReplaceIsLiteralUnlessRegex() {
        SearchQuery literal = new("Kael", MatchCase: true, WholeWord: true);
        Assert.True(literal.TryCreateRegex(out Regex? regex, out _));
        IReadOnlyList<SearchMatch> plan = ManuscriptSearch.PlanReplacements("Kael and Kaela", regex!, "K$1", useRegex: false);
        Assert.Equal("K$1 and Kaela", ManuscriptSearch.Apply("Kael and Kaela", plan));

        SearchQuery pattern = new(@"(\w+) Duskmantle", UseRegex: true);
        Assert.True(pattern.TryCreateRegex(out Regex? regex2, out _));
        IReadOnlyList<SearchMatch> plan2 = ManuscriptSearch.PlanReplacements("Kael Duskmantle and Wren Duskmantle", regex2!, "$1 D.", useRegex: true);
        Assert.Equal("Kael D. and Wren D.", ManuscriptSearch.Apply("Kael Duskmantle and Wren Duskmantle", plan2));
    }

    [Fact]
    public void InvalidRegexReportsAnError() {
        Assert.False(new SearchQuery("(unclosed", UseRegex: true).TryCreateRegex(out _, out string? error));
        Assert.Contains("Invalid regular expression", error);
    }
}

public sealed class ContinuityTests {
    private readonly GrammarEngine _engine = new();

    private static GrammarOptions Options(params StoryEntity[] entities) {
        return new GrammarOptions(new EntityIndex(entities), new HashSet<string>(), new HashSet<string>());
    }

    private static StoryEntity Kael() {
        StoryEntity kael = new(EntityKind.Character, "Kael Duskmantle") { Aliases = "Kael", Summary = "A grey wolf with amber eyes." };
        kael.Attributes.Add(new EntityAttribute("Age", "35"));
        kael.Attributes.Add(new EntityAttribute("Pronouns", "he/him"));
        kael.Attributes.Add(new EntityAttribute("Hair", "black"));
        return kael;
    }

    [Fact]
    public void FlagsMisspelledNamesWithFix() {
        GrammarDiagnostic diagnostic = Assert.Single(_engine.Check("Then Keal smiled at Rowen's door.", Options(Kael(), new StoryEntity(EntityKind.Character, "Rowan"))), d => d.RuleId == "NW4002" && d.Offset == 5);
        Assert.Equal("Kael", diagnostic.Fixes[0].Replacement);
        Assert.Contains(_engine.Check("Then Keal smiled at Rowen's door.", Options(Kael(), new StoryEntity(EntityKind.Character, "Rowan"))),
            d => (d.RuleId == "NW4002") && (d.Fixes[0].Replacement == "Rowan's"));
    }

    [Fact]
    public void FlagsEyeAndHairColorContradictions() {
        IReadOnlyList<GrammarDiagnostic> diagnostics = _engine.Check("Kael's bright blue eyes narrowed. Kael's hair was silver. Kael's amber eyes softened.", Options(Kael()));
        Assert.Equal(["blue", "silver"], diagnostics.Where(d => d.RuleId == "NW4003").Select(d => "Kael's bright blue eyes narrowed. Kael's hair was silver. Kael's amber eyes softened."[d.Offset..d.End]));
        Assert.Equal("amber", diagnostics.First(d => d.RuleId == "NW4003").Fixes[0].Replacement);
    }

    [Theory]
    [InlineData("Kael, thirty-nine, said nothing.", true)]
    [InlineData("the 41-year-old Kael sighed.", true)]
    [InlineData("Kael was 35 years old.", false)]
    [InlineData("Kael was thirty feet away.", false)]
    [InlineData("Kael, thirty-five, said nothing.", false)]
    public void ChecksAgeAgainstAttribute(string text, bool flagged) {
        Assert.Equal(flagged, _engine.Check(text, Options(Kael())).Any(d => d.RuleId == "NW4004"));
    }

    [Fact]
    public void ChecksPossessivePronounOnSelfGestures() {
        IReadOnlyList<GrammarDiagnostic> diagnostics = _engine.Check("Kael flicked her ears. Kael took her hand. Kael nodded his head.", Options(Kael()));
        GrammarDiagnostic diagnostic = Assert.Single(diagnostics, d => d.RuleId == "NW4005");
        Assert.Equal("his", diagnostic.Fixes[0].Replacement);
    }

    [Fact]
    public void FlagsDeadCharacterActingAfterDeathChapterButNotInDialogue() {
        StoryEntity tobias = new(EntityKind.Character, "Tobias") { };
        tobias.Attributes.Add(new EntityAttribute("Died", "Chapter 2"));
        ManuscriptPosition later = new(2, ["Chapter 1: Start", "Chapter 2: The Storm", "Chapter 3: After"]);
        ManuscriptPosition earlier = later with { Index = 1 };
        const string text = "Tobias smiled at her. \"Tobias said he'd come back,\" Elena whispered.";

        Assert.Single(_engine.Check(text, Options(tobias) with { Position = later }), d => d.RuleId == "NW4006");
        Assert.DoesNotContain(_engine.Check(text, Options(tobias) with { Position = earlier }), d => d.RuleId == "NW4006");
    }

    [Theory]
    [InlineData("Chapter 12", 1)]
    [InlineData("12", 1)]
    [InlineData("The Stag", 1)]
    [InlineData("Epilogue: The Ivy Ribbon", 2)]
    [InlineData("Chapter 99", null)]
    public void ResolvesChapterReferences(string reference, int? expected) {
        ManuscriptPosition position = new(0, ["Chapter 11: Lanternfall", "Chapter 12: The Stag", "Epilogue: The Ivy Ribbon"]);
        Assert.Equal(expected, position.Resolve(reference));
    }
}

public sealed class RecoveryTests : IDisposable {
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "WritingIDE.Tests", Guid.NewGuid().ToString("N"));

    public RecoveryTests() {
        RecoveryStore.RootDirectory = Path.Combine(_directory, "Recovery");
    }

    [Fact]
    public void SnapshotRestoresUnsavedTextAndElements() {
        NovelProject project = ProjectSerializer.Create(_directory, "Crashy", "", ProjectTemplate.Series);
        Chapter chapter = project.Chapters[0];
        project.Entities.Add(new StoryEntity(EntityKind.Character, "Unsaved Hero"));
        RecoveryStore.Write(project, c => c == chapter ? "Words typed before the crash." : c.Text);

        RecoverySnapshot snapshot = RecoveryStore.Find(project.FilePath)!;
        NovelProject restored = RecoveryStore.Restore(snapshot);

        Assert.Equal("Words typed before the crash.", restored.Chapters[0].Text);
        Assert.Contains(restored.Entities, e => e.Name == "Unsaved Hero");
        Assert.True(restored.IsSeries);
        Assert.True(restored.IsDirty);

        // On disk the chapter is still empty: recovery has not been saved yet.
        Assert.Equal("", ProjectSerializer.Load(project.FilePath).Chapters[0].Text);
    }

    [Fact]
    public void SnapshotOlderThanTheProjectFileIsIgnoredAndDeleteRemovesIt() {
        NovelProject project = ProjectSerializer.Create(_directory, "Saved", "", ProjectTemplate.All[0]);
        RecoveryStore.Write(project, c => "old");
        File.SetLastWriteTimeUtc(project.FilePath, DateTime.UtcNow.AddMinutes(5));
        Assert.Null(RecoveryStore.Find(project.FilePath));

        RecoveryStore.Delete(project.FilePath);
        Assert.False(File.Exists(RecoveryStore.PathFor(project.FilePath)));
    }

    public void Dispose() {
        if (Directory.Exists(_directory)) {
            Directory.Delete(_directory, recursive: true);
        }
    }
}