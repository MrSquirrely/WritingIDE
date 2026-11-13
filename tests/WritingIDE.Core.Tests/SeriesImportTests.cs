using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

using WritingIDE.Core.Import;
using WritingIDE.Core.Model;

using WordText = DocumentFormat.OpenXml.Wordprocessing.Text;

namespace WritingIDE.Core.Tests;

/// <summary>"Combine a whole series": several manuscripts and bibles merged by an AI and imported as one series.</summary>
public sealed class SeriesImportTests : IDisposable {
    private const string Part1 = """
        ```
        === SERIES: The Greyhaven Cycle ===
        Genre: Literary mystery
        Notes:
        Tone: quiet, salt-stained.
        World rule: the sea keeps what it is given.

        === CHARACTER: Elena Marsh ===
        Aliases: Elena, Keeper Marsh
        Summary: Keeper of the Greyhaven light.
        First appears: The Lighthouse Keeper
        Notes:
        Conflict: Book 1 bible says she is 41; Book 2 bible says 42 (a year later). Kept 42.

        === BOOK: The Lighthouse Keeper ===
        Synopsis: Book one.
        Notes:
        Reveal: the compass belonged to Tobias.

        === CHAPTER: Chapter 1 - The Light ===
        Status: Draft
        ---
        The wind had been rising since noon.

        === CONTINUE ===
        ```
        """;

    private const string Part2 = """
        === BOOK: The Lighthouse Keeper ===

        === CHAPTER: Chapter 2 - Port Ashby ===
        Status: Draft
        ---
        The harbor smelled of tar.

        === BOOK: The Drowned Bell ===
        Synopsis: Book two.
        Notes:
        Only planned so far.

        === CHAPTER: Chapter 1 - Low Tide ===
        Status: Outline
        ---
        Beat 1: Elena hears a bell.
        """;

    private readonly string _directory = Path.Combine(Path.GetTempPath(), "WritingIDE.Tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public void CombinePromptAsksForOneMergedSeries() {
        ImportPromptOptions options = new(ImportPromptMode.CombineSeries, false, new HashSet<EntityKind>(Enum.GetValues<EntityKind>()), 0, "", []) {
            Sources = [new ImportSource("Book 1.docx", "Chapter one text."), new ImportSource("Series bible.md", "Elena is 41.")]
        };

        string prompt = ImportPrompt.Build(options);

        Assert.Contains("Combine all of my series material", prompt);
        Assert.Contains("importing as a series", prompt);
        Assert.Contains("=== SERIES:", prompt);
        Assert.Contains("BOOK blocks start a book", prompt);
        Assert.Contains("what the bibles say about this book only", prompt);
        Assert.Contains("Merge, never repeat", prompt);
        Assert.Contains("\"Conflict:\"", prompt);
        Assert.Contains("\"First appears\"", prompt);
        Assert.Contains("exactly as written", prompt);
        Assert.Contains("=== CONTINUE ===", prompt);
        Assert.Contains("MY SERIES MATERIAL", prompt);
        Assert.Contains("----- SOURCE 1: Book 1.docx -----", prompt);
        Assert.EndsWith("----- SOURCE 2: Series bible.md -----" + Environment.NewLine + "Elena is 41.", prompt);
        Assert.DoesNotContain("Do not use BOOK blocks", prompt);

        // The example in the prompt is itself a valid series import with two books.
        int start = prompt.IndexOf("EXAMPLE", StringComparison.Ordinal) + 7;
        ImportDocument example = ImportParser.Parse(prompt[start..prompt.IndexOf("MY SERIES MATERIAL", StringComparison.Ordinal)]);
        Assert.Empty(example.Warnings);
        Assert.True(example.Novel!.IsSeries);
        Assert.Equal(2, example.Books.Count);
        Assert.NotEmpty(example.Books[1].Notes);
        Assert.Contains(example.Chapters, chapter => chapter.Status == ChapterStatus.Outline && chapter.BookTitle == "The Drowned Bell");
    }

    [Fact]
    public void CombinePromptWithoutFilesUsesTheChat() {
        ImportPromptOptions options = new(ImportPromptMode.CombineSeries, false, new HashSet<EntityKind> { EntityKind.Character }, 0, "", []);

        string prompt = ImportPrompt.Build(options);

        Assert.Contains("every manuscript and story bible in this conversation", prompt);
        Assert.EndsWith("from first page to last.", prompt);
        Assert.DoesNotContain("----- SOURCE", prompt);
    }

    [Fact]
    public void RepliesInPartsAreJoined() {
        Assert.True(ImportPrompt.AsksToContinue(Part1));
        Assert.False(ImportPrompt.AsksToContinue(Part2));
        Assert.True(ImportPrompt.AsksToContinue("text\n### CONTINUE\n"));

        string joined = ImportPrompt.JoinParts([Part1, Part2]);

        Assert.DoesNotContain("CONTINUE", joined);
        Assert.DoesNotContain("```", joined);
        ImportDocument document = ImportParser.Parse(joined);
        Assert.False(document.IsPartial);
        Assert.Empty(document.Warnings);
        Assert.Equal(["The Lighthouse Keeper", "The Drowned Bell"], document.Books.Select(static book => book.Title));
        Assert.Equal("Reveal: the compass belonged to Tobias.", document.Books[0].Notes);
        Assert.Equal("Book one.", document.Books[0].Synopsis);
        Assert.Equal(2, document.Chapters.Count(static chapter => chapter.BookTitle == "The Lighthouse Keeper"));
    }

    [Fact]
    public void CombinedReplyImportsANovelAsASeries() {
        NovelProject project = new() { Name = "Greyhaven" };
        project.Entities.Add(StoryEntity.CreateWithDefaults(EntityKind.Character, "Elena Marsh"));
        ImportDocument document = ImportParser.Parse(ImportPrompt.JoinParts([Part1, Part2]));

        ImportSummary summary = ImportApplier.Apply(project, document, new ImportOptions(ImportConflictMode.Update) { ConvertToSeries = true });

        Assert.True(project.IsSeries);
        Assert.Equal(["The Lighthouse Keeper", "The Drowned Bell"], project.Books.Select(static book => book.Title));
        Assert.Equal(["Chapter 1 - The Light", "Chapter 2 - Port Ashby"], project.GetChapters(project.Books[0]).Select(static chapter => chapter.Title));
        Chapter planned = Assert.Single(project.GetChapters(project.Books[1]));
        Assert.Equal(ChapterStatus.Outline, planned.Status);
        // The existing character is merged, not duplicated.
        StoryEntity elena = Assert.Single(project.Entities, static entity => entity.Kind == EntityKind.Character);
        Assert.Contains("Keeper Marsh", elena.AliasList);
        Assert.Equal("The Lighthouse Keeper", elena.Attributes.Single(static attribute => attribute.Key == "First appears").Value);
        Assert.Contains("Conflict:", elena.Notes);
        // Series-wide notes, then each book's own notes under its title.
        Assert.Contains("World rule: the sea keeps what it is given.", project.Notes);
        Assert.Contains($"Book: The Lighthouse Keeper{Environment.NewLine}Reveal: the compass belonged to Tobias.", project.Notes);
        Assert.Contains($"Book: The Drowned Bell{Environment.NewLine}Only planned so far.", project.Notes);
        // Converting the novel made the first book; the import added the second.
        Assert.Equal(1, summary.BooksAdded);

        // Importing the same reply again changes nothing in the notes.
        string notes = project.Notes;
        ImportApplier.Apply(project, document, new ImportOptions(ImportConflictMode.Update));
        Assert.Equal(notes, project.Notes);
    }

    [Fact]
    public void WordDocumentsAreReadWithTheirHeadings() {
        Directory.CreateDirectory(_directory);
        string path = Path.Combine(_directory, "Book 1.docx");
        using (WordprocessingDocument document = WordprocessingDocument.Create(path, WordprocessingDocumentType.Document)) {
            MainDocumentPart main = document.AddMainDocumentPart();
            main.Document = new Document(new Body(
                Paragraph("Heading1", "The Lighthouse Keeper"),
                Paragraph("Heading2", "Chapter 1"),
                Paragraph(null, "The wind had been rising since noon."),
                Paragraph(null, ""),
                Paragraph(null, "\"Who's there?\" she whispered.")));
        }

        ImportSource source = ImportSourceReader.Read(path);

        Assert.Equal("Book 1.docx", source.Name);
        Assert.Equal("# The Lighthouse Keeper\n\n## Chapter 1\n\nThe wind had been rising since noon.\n\n\"Who's there?\" she whispered.", source.Text);
    }

    private static Paragraph Paragraph(string? style, string text) {
        Paragraph paragraph = new();
        if (style is not null) {
            paragraph.Append(new ParagraphProperties(new ParagraphStyleId { Val = style }));
        }
        paragraph.Append(new Run(new WordText(text) { Space = SpaceProcessingModeValues.Preserve }));
        return paragraph;
    }

    public void Dispose() {
        if (Directory.Exists(_directory)) {
            Directory.Delete(_directory, recursive: true);
        }
    }
}