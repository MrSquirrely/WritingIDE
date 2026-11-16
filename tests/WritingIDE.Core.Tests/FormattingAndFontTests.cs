using System.IO.Compression;
using System.Text;

using DocumentFormat.OpenXml.Packaging;

using WritingIDE.Core.Export;
using WritingIDE.Core.Model;
using WritingIDE.Core.Text;

namespace WritingIDE.Core.Tests;

public sealed class FormattingAndFontTests : IDisposable {
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "WritingIDE.Tests", Guid.NewGuid().ToString("N"));

    public FormattingAndFontTests() {
        Directory.CreateDirectory(_directory);
    }

    [Fact]
    public void EmphasisIsFoundButSceneBreaksAreNot() {
        IReadOnlyList<EmphasisSpan> spans = Emphasis.Find("A *soft* and **loud** and ***both*** and _under_ word.");

        Assert.Equal([(false, true), (true, false), (true, true), (false, true)], spans.Select(static span => (span.Bold, span.Italic)));
        Assert.Equal("both", "A *soft* and **loud** and ***both*** and _under_ word.".Substring(spans[2].ContentStart, spans[2].ContentLength));
        Assert.Empty(Emphasis.Find("***"));
        Assert.Empty(Emphasis.Find("* * *"));
        Assert.Empty(Emphasis.Find("5 * 3 = 15"));
    }

    [Fact]
    public void ExportRunsCarryBoldItalic() {
        IReadOnlyList<InlineRun> runs = ManuscriptDocumentBuilder.ParseInline("It was ***never*** **ever** *quiet*.");

        Assert.Contains(new InlineRun("never", true, true), runs);
        Assert.Contains(new InlineRun("ever", false, true), runs);
        Assert.Contains(new InlineRun("quiet", true, false), runs);
        Assert.Equal("It was never ever quiet.", string.Concat(runs.Select(static run => run.Text)));
    }

    [Theory]
    // Selection, command, result, selected text afterwards.
    [InlineData("The light was out.", "light", FormatCommand.Bold, "The **light** was out.", "light")]
    [InlineData("The **light** was out.", "light", FormatCommand.Bold, "The light was out.", "light")]
    [InlineData("The **light** was out.", "light", FormatCommand.Italic, "The ***light*** was out.", "light")]
    [InlineData("The ***light*** was out.", "light", FormatCommand.Bold, "The *light* was out.", "light")]
    [InlineData("The **light** was out.", "**light**", FormatCommand.Bold, "The light was out.", "light")]
    [InlineData("The light was out.", "igh", FormatCommand.Italic, "The *light* was out.", "light")]
    [InlineData("The light was out.", "light ", FormatCommand.Italic, "The *light* was out.", "light")]
    [InlineData("The light was out.", "light was", FormatCommand.Italic, "The *light was* out.", "light was")]
    [InlineData("A **b** and *c* d.", "A **b** and *c* d.", FormatCommand.Clear, "A b and c d.", "b and c")]
    public void FormattingTogglesMarkers(string text, string selected, FormatCommand command, string expected, string expectedSelection) {
        int start = text.IndexOf(selected, StringComparison.Ordinal);

        FormatEdit edit = TextFormatting.Apply(text, start, selected.Length, command);
        string result = Replace(text, edit);

        Assert.Equal(expected, result);
        Assert.Equal(expectedSelection, result.Substring(edit.SelectionStart, edit.SelectionLength));
    }

    [Fact]
    public void CaretFormatsTheWordOrInsertsMarkers() {
        string text = "The light was out.";

        FormatEdit word = TextFormatting.Apply(text, text.IndexOf("ght", StringComparison.Ordinal), 0, FormatCommand.Italic);
        Assert.Equal("The *light* was out.", Replace(text, word));

        FormatEdit between = TextFormatting.Apply("Out. ", 5, 0, FormatCommand.Bold);
        Assert.Equal("Out. ****", Replace("Out. ", between));
        Assert.Equal((7, 0), (between.SelectionStart, between.SelectionLength));

        FormatEdit clear = TextFormatting.Apply("A **bold** word", 5, 0, FormatCommand.Clear);
        Assert.Equal("A bold word", Replace("A **bold** word", clear));
    }

    [Fact]
    public void SeveralParagraphsAreFormattedEachOnItsOwn() {
        string text = "One two.\r\n\r\n***\r\n\r\nThree **four**.\r\n";

        FormatEdit edit = TextFormatting.Apply(text, 0, text.Length, FormatCommand.Italic);

        Assert.Equal("*One two.*\r\n\r\n***\r\n\r\n*Three **four**.*\r\n", Replace(text, edit));
        // Toggling again with the same target removes it from both.
        string twice = Replace(Replace(text, edit), TextFormatting.Apply(Replace(text, edit), 0, Replace(text, edit).Length, FormatCommand.Italic));
        Assert.Equal(text, twice);
    }

    [Fact]
    public void ChangesOnlyInsertOrRemoveMarkers() {
        string text = "She said *no* and **left** the room.";
        FormatEdit edit = TextFormatting.Apply(text, 0, text.Length, FormatCommand.Clear);

        IReadOnlyList<(int Offset, int RemoveLength, string Insert)> changes = TextFormatting.ToChanges(text, edit);

        Assert.All(changes, static change => Assert.True((change.Insert.Length == 0) && (change.RemoveLength is 1 or 2)));
        StringBuilder applied = new(text);
        foreach ((int offset, int remove, string insert) in changes) {
            applied.Remove(offset, remove).Insert(offset, insert);
        }
        Assert.Equal("She said no and left the room.", applied.ToString());

        FormatEdit bold = TextFormatting.Apply(text, text.IndexOf("room", StringComparison.Ordinal), 4, FormatCommand.Bold);
        Assert.Equal([(text.IndexOf("room", StringComparison.Ordinal) + 4, 0, "**"), (text.IndexOf("room", StringComparison.Ordinal), 0, "**")], TextFormatting.ToChanges(text, bold));
    }

    [Fact]
    public void FontCatalogReadsFontsAndCollections() {
        string fonts = Environment.GetFolderPath(Environment.SpecialFolder.Fonts);
        if (!File.Exists(Path.Combine(fonts, "georgia.ttf")) || !File.Exists(Path.Combine(fonts, "cambria.ttc"))) {
            return;
        }
        FontCatalog catalog = FontCatalog.Scan([fonts]);

        FontFamilyFaces georgia = catalog.Find("Georgia")!;
        Assert.True(georgia.Regular.HasTrueTypeOutlines);
        Assert.False(georgia.Regular.Bold || georgia.Regular.Italic);
        Assert.True(georgia.Bold!.Bold);
        Assert.True(georgia.Italic!.Italic && !georgia.Italic.Bold);
        Assert.True(georgia.BoldItalic!.Bold && georgia.BoldItalic.Italic);

        // Cambria lives in a .ttc collection: one face is extracted as a standalone font.
        FontFamilyFaces cambria = catalog.Find("Cambria")!;
        Assert.True(cambria.Regular.IsCollection);
        string extracted = Path.Combine(_directory, "cambria.ttf");
        File.WriteAllBytes(extracted, FontCatalog.ExtractFace(cambria.Regular));
        FontFace face = Assert.Single(FontCatalog.Read(extracted));
        Assert.Equal("Cambria", face.Family);
        Assert.False(face.Bold || face.Italic);
        Assert.Null(catalog.Find("No Such Font Family"));
        Assert.Empty(FontCatalog.Read(Path.Combine(fonts, "desktop.ini")));
    }

    [Fact]
    public void ExportsUseTheChosenFont() {
        if (FontCatalog.Installed.Find("Cambria") is null) {
            return;
        }
        NovelProject project = new() { Name = "Fonts", Author = "A. Writer" };
        Chapter chapter = new("One") { Text = "Plain, *italic*, **bold** and ***both***." };
        project.Chapters.Add(chapter);
        ExportOptions options = new() { Chapters = [chapter], FontFamily = "Cambria" };

        string pdf = Path.Combine(_directory, "book.pdf");
        Assert.Empty(Exporter.Export(pdf, project, options with { Format = ExportFormat.Pdf }, static c => c.Text));
        Assert.Contains("Cambria", Encoding.Latin1.GetString(File.ReadAllBytes(pdf)));

        string docx = Path.Combine(_directory, "book.docx");
        Exporter.Export(docx, project, options with { Format = ExportFormat.Docx }, static c => c.Text);
        using (WordprocessingDocument word = WordprocessingDocument.Open(docx, false)) {
            Assert.Contains("w:ascii=\"Cambria\"", word.MainDocumentPart!.StyleDefinitionsPart!.Styles!.OuterXml);
        }

        string epub = Path.Combine(_directory, "book.epub");
        IReadOnlyList<string> epubWarnings = Exporter.Export(epub, project, options with { Format = ExportFormat.Epub }, static c => c.Text);
        using (ZipArchive zip = ZipFile.OpenRead(epub)) {
            string css = new StreamReader(zip.GetEntry("OEBPS/style.css")!.Open()).ReadToEnd();
            Assert.Contains("font-family: \"Cambria\"", css);
            string chapterHtml = new StreamReader(zip.GetEntry("OEBPS/chapter001.xhtml")!.Open()).ReadToEnd();
            Assert.Contains("<strong><em>both</em></strong>", chapterHtml);
            if (FontCatalog.Installed.Find("Cambria")!.All.Any(static face => face.IsEmbeddingRestricted)) {
                Assert.Single(epubWarnings);
            }
            else {
                Assert.Empty(epubWarnings);
                Assert.NotNull(zip.GetEntry("OEBPS/fonts/regular.ttf"));
                Assert.Contains("@font-face", css);
                Assert.Contains("fonts/regular.ttf", new StreamReader(zip.GetEntry("OEBPS/content.opf")!.Open()).ReadToEnd());
            }
        }
    }

    [Fact]
    public void AnUnknownFontFallsBackWithAWarning() {
        NovelProject project = new() { Name = "Fonts" };
        Chapter chapter = new("One") { Text = "Text." };
        project.Chapters.Add(chapter);
        ExportOptions options = new() { Format = ExportFormat.Pdf, Chapters = [chapter], FontFamily = "No Such Font Family" };

        IReadOnlyList<string> warnings = Exporter.Export(Path.Combine(_directory, "fallback.pdf"), project, options, static c => c.Text);

        Assert.Contains(warnings, static warning => warning.Contains("was not found", StringComparison.Ordinal) && warning.Contains("Times New Roman", StringComparison.Ordinal));
        Assert.Equal("Times New Roman", (options with { FontFamily = "" }).EffectiveFontFamily);
        Assert.Equal("Georgia", (options with { FontFamily = null, Style = ExportStyle.Book }).EffectiveFontFamily);
    }

    private static string Replace(string text, FormatEdit edit) {
        return text[..edit.Start] + edit.Text + text[(edit.Start + edit.Length)..];
    }

    public void Dispose() {
        if (Directory.Exists(_directory)) {
            Directory.Delete(_directory, recursive: true);
        }
    }
}