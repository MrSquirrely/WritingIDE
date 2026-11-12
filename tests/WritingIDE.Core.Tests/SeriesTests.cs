using WritingIDE.Core.Grammar;
using WritingIDE.Core.Import;
using WritingIDE.Core.Model;
using WritingIDE.Core.Persistence;

namespace WritingIDE.Core.Tests;

public sealed class SeriesTests : IDisposable {
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "WritingIDE.Tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public void SeriesRoundTripsWithBooksAndPerBookFolders() {
        NovelProject series = ProjectSerializer.Create(_directory, "Saga", "Me", ProjectTemplate.Series);
        Book second = new("Book Two") { Synopsis = "The sequel." };
        series.Books.Add(second);
        series.Chapters.Add(new Chapter("Chapter 1") { BookId = second.Id, Text = "Sequel text." });
        series.Notes = "Tone: cozy.";
        ProjectSerializer.Save(series);

        NovelProject loaded = ProjectSerializer.Load(series.FilePath);

        Assert.True(loaded.IsSeries);
        Assert.Equal(["Saga, Book 1", "Book Two"], loaded.Books.Select(b => b.Title));
        Assert.Equal("Tone: cozy.", loaded.Notes);
        Chapter sequelChapter = Assert.Single(loaded.GetChapters(loaded.Books[1]));
        Assert.Equal("Sequel text.", sequelChapter.Text);
        Assert.Equal(Path.Combine("Manuscript", "Book Two", "Chapter 1.txt"), sequelChapter.FileName);
        Assert.Single(loaded.GetChapters(loaded.Books[0]));
        Assert.Equal("Book Two \u203A Chapter 1", loaded.GetDisplayName(sequelChapter));
    }

    [Fact]
    public void ConvertToSeriesMovesChaptersIntoTheFirstBook() {
        NovelProject project = new() { Name = "Solo" };
        project.Chapters.Add(new Chapter("One"));
        project.Chapters.Add(new Chapter("Two"));

        Book book = project.ConvertToSeries("Solo, Book 1");

        Assert.True(project.IsSeries);
        Assert.All(project.Chapters, chapter => Assert.Equal(book.Id, chapter.BookId));
    }

    [Fact]
    public void ParsesSeriesBooksLoreAndContinue() {
        const string text = """
            === SERIES: Ranks & Antlers ===
            Genre: Cozy romantasy
            Tone: warm, funny, slow-burn
            Notes:
            Premise: a secretly S-rank wolf.

            Themes: hiding vs. being seen.

            === LORE: Aether ===
            Summary: Magic drawn from one's own stamina.
            Category: Magic

            === BOOK: Ranks & Antlers ===
            Synopsis: Book one.

            === CHAPTER: Chapter 1: Too Pretty ===
            ---
            Kael choked on his cider.

            === CONTINUE ===
            """;

        ImportDocument document = ImportParser.Parse(text);

        Assert.True(document.Novel!.IsSeries);
        Assert.Contains("Tone: warm, funny, slow-burn", document.Novel.Notes);
        Assert.Contains("Themes: hiding vs. being seen.", document.Novel.Notes);
        Assert.Equal(EntityKind.Lore, Assert.Single(document.Entities).Kind);
        Assert.Equal("Ranks & Antlers", Assert.Single(document.Books).Title);
        Assert.Equal("Ranks & Antlers", Assert.Single(document.Chapters).BookTitle);
        Assert.True(document.IsPartial);
    }

    [Fact]
    public void MarkdownManuscriptBecomesOneBookWithChapters() {
        const string text = """
            # Ranks & Antlers

            Nov 7, 2026 \u00B7 @Author

            ## Foreword

            Welcome to the Guild.

            ## Prologue: The Color of Silver

            Silver light.

            ### A scene break heading

            More prologue.

            ## Chapter 1: Too Pretty

            "You look too pretty to be thirty-nine."
            """;

        ImportDocument document = ImportParser.Parse(text);

        Assert.True(document.IsPlainText);
        Assert.Equal("Ranks & Antlers", Assert.Single(document.Books).Title);
        Assert.Equal(["Foreword", "Prologue: The Color of Silver", "Chapter 1: Too Pretty"], document.Chapters.Select(c => c.Title));
        Assert.Contains("### A scene break heading", document.Chapters[1].Text);
        Assert.All(document.Chapters, chapter => Assert.Equal("Ranks & Antlers", chapter.BookTitle));
        Assert.Contains(document.Warnings, w => w.Message.Contains("not imported"));
    }

    [Fact]
    public void ImportIntoSeriesPutsChaptersInTheirBooks() {
        NovelProject series = ProjectTemplate.Series.Create("Saga", "");
        Book first = series.Books[0];
        ImportDocument document = ImportParser.Parse("""
            === BOOK: Book Two ===
            === CHAPTER: Chapter 1 ===
            ---
            Second book.
            """);
        document.Chapters.Add(new ImportedChapter("Loose Chapter", "", null, "No book named."));

        ImportSummary summary = ImportApplier.Apply(series, document, new ImportOptions(ImportConflictMode.Update) { DefaultBook = first });

        Assert.Equal(1, summary.BooksAdded);
        Book second = series.Books.Single(b => b.Title == "Book Two");
        Assert.Equal("Second book.", series.GetChapters(second).Single().Text);
        Assert.Contains(series.GetChapters(first), c => c.Title == "Loose Chapter");
        // "Chapter 1" exists in Book 1 too, but matching is per book, so nothing was overwritten.
        Assert.Equal("", series.GetChapters(first).First().Text);
    }

    [Fact]
    public void OutlineChaptersCanGoToTheBibleNotes() {
        NovelProject series = ProjectTemplate.Series.Create("Saga", "");
        ImportDocument document = ImportParser.Parse("""
            === BOOK: Ranks & Antlers ===
            === CHAPTER: Chapter 21 to End - Choosing Each Other ===
            Synopsis: Kael chooses Rowan openly.
            Status: Outline

            Beat 1: The Crown summons Kael.

            Beat 2: Sera returns.

            === CHAPTER: Chapter 13 - Sweet Rolls ===
            Status: Draft
            ---
            Kael had been outside the guild hall since before the bells.
            """);

        ImportSummary summary = ImportApplier.Apply(series, document, new ImportOptions(ImportConflictMode.Update) { OutlineChaptersToNotes = true });

        Assert.Equal(1, summary.PlanEntries);
        Assert.Equal(1, summary.ChaptersAdded);
        Assert.DoesNotContain(series.Chapters, c => c.Title.StartsWith("Chapter 21", StringComparison.Ordinal));
        Assert.Contains("Chapter plan: Chapter 21 to End - Choosing Each Other (Ranks & Antlers)", series.Notes);
        Assert.Contains($"Kael chooses Rowan openly.{Environment.NewLine}Beat 1: The Crown summons Kael.{Environment.NewLine}Beat 2: Sera returns.", series.Notes);

        // Importing the same reply again does not duplicate the plan.
        ImportApplier.Apply(series, document, new ImportOptions(ImportConflictMode.Update) { OutlineChaptersToNotes = true });
        Assert.Equal(2, series.Notes.Split("Chapter plan:").Length);
    }

    [Fact]
    public void NovelProjectCanBeConvertedDuringImport() {
        NovelProject novel = new() { Name = "Solo" };
        novel.Chapters.Add(new Chapter("Old"));
        ImportDocument document = ImportParser.Parse("# Book A\n## One\nText A.\n# Book B\n## One\nText B.");

        ImportApplier.Apply(novel, document, new ImportOptions(ImportConflictMode.Update) { ConvertToSeries = true });

        Assert.True(novel.IsSeries);
        Assert.Equal(["Book A", "Book B"], novel.Books.Select(b => b.Title));
        Assert.Equal("Text B.", novel.GetChapters(novel.Books[1]).Single().Text);
    }

    [Fact]
    public void BiblePromptAsksForNotesLoreAndOutlines() {
        ImportPromptOptions options = new(ImportPromptMode.ConvertBible, true, new HashSet<EntityKind>(Enum.GetValues<EntityKind>()), 0, "", []) { IsSeries = true };

        string prompt = ImportPrompt.Build(options);

        Assert.Contains("story bible we created earlier in this conversation", prompt);
        Assert.Contains("=== SERIES:", prompt);
        Assert.Contains("LORE blocks", prompt);
        Assert.Contains("Status: Outline", prompt);
        Assert.Contains("=== CONTINUE ===", prompt);
        Assert.EndsWith("Use the story bible from earlier in this conversation.", prompt);
        ImportDocument example = ImportParser.Parse(prompt[(prompt.IndexOf("EXAMPLE", StringComparison.Ordinal) + 7)..prompt.LastIndexOf("Use the", StringComparison.Ordinal)]);
        Assert.Empty(example.Warnings);
        Assert.True(example.Novel!.IsSeries);
    }

    [Fact]
    public void ManuscriptPromptKeepsTextVerbatim() {
        ImportPromptOptions options = new(ImportPromptMode.ConvertManuscript, false, new HashSet<EntityKind> { EntityKind.Character }, 0, "", []);

        string prompt = ImportPrompt.Build(options);

        Assert.Contains("exactly as written", prompt);
        Assert.Contains("=== CHAPTER", prompt);
        Assert.Contains("Do not use BOOK blocks.", prompt);
        Assert.EndsWith("from its first page to its last.", prompt);
    }

    [Fact]
    public void PluginFindingsOverlappingBuiltInFindingsAreDropped() {
        GrammarEngine engine = new();
        engine.SetPluginRules([new FakePluginRule()]);

        IReadOnlyList<GrammarDiagnostic> diagnostics = engine.Check("He ate a apple. Fine words here.");

        Assert.Contains(diagnostics, d => d.RuleId == "NW1003");
        Assert.DoesNotContain(diagnostics, d => (d.RuleId == "FAKE") && (d.Offset == 7));
        Assert.Contains(diagnostics, d => (d.RuleId == "FAKE") && (d.Offset == 16));
    }

    public void Dispose() {
        if (Directory.Exists(_directory)) {
            Directory.Delete(_directory, recursive: true);
        }
    }

    private sealed class FakePluginRule() : GrammarRule("FAKE", "Fake", "Plugin", DiagnosticSeverity.Warning, "Test plugin") {
        public override bool IsPlugin => true;

        public override IEnumerable<GrammarDiagnostic> Analyze(AnalysisContext context) {
            yield return Report(7, 1, "Duplicate of the a/an finding.");
            yield return Report(16, 4, "Unique plugin finding.");
        }
    }
}