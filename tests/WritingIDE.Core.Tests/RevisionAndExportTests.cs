using System.IO.Compression;
using System.Net;
using System.Text;

using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Validation;

using WritingIDE.Core.Ai;
using WritingIDE.Core.Analysis;
using WritingIDE.Core.Export;
using WritingIDE.Core.Grammar;
using WritingIDE.Core.Model;
using WritingIDE.Core.Persistence;

namespace WritingIDE.Core.Tests;

public sealed class SceneAndChapterTests {
    private const string Text = "Kael walked in.\nThe hall was cold.\n\n* * *\n\nWren waited.\n\n*** At the gate\nRowan left.";

    [Fact]
    public void SplitsScenesAtBreaksWithLabels() {
        IReadOnlyList<Scene> scenes = SceneParser.Split(Text);
        Assert.Equal(3, scenes.Count);
        Assert.Equal("Kael walked in.\nThe hall was cold.\n\n", Text[scenes[0].Start..scenes[0].End]);
        Assert.Equal("Wren waited.", Text[scenes[1].Start..scenes[1].End].Trim());
        Assert.Equal("At the gate", scenes[2].Label);
        Assert.Equal("Rowan left.", Text[scenes[2].Start..scenes[2].End]);
        Assert.Equal("At the gate", SceneParser.Describe(Text, scenes[2]));
        Assert.Equal("Wren waited.", SceneParser.Describe(Text, scenes[1]));
        Assert.Equal(1, SceneParser.SceneAt(Text, Text.IndexOf("Wren", StringComparison.Ordinal)).Index);
        // Windows line endings: "$" in multiline mode does not match before "\r".
        IReadOnlyList<Scene> crlf = SceneParser.Split("One.\r\n\r\n* * *\r\n\r\nTwo.\r\n*** Later\r\nThree.");
        Assert.Equal(3, crlf.Count);
        Assert.Equal("Later", crlf[2].Label);
        Assert.Equal("Three.", "One.\r\n\r\n* * *\r\n\r\nTwo.\r\n*** Later\r\nThree."[crlf[2].Start..crlf[2].End]);
        Assert.True(SceneParser.IsBreakLine("#"));
        Assert.False(SceneParser.IsBreakLine("# Heading"));
        Assert.False(SceneParser.IsBreakLine("Not *** a break"));
    }

    [Fact]
    public void SplitAndMergeCarryCommentsAlong() {
        NovelProject project = new();
        Chapter chapter = new("One") { Text = Text };
        project.Chapters.Add(chapter);
        chapter.Comments.Add(new ChapterComment { Offset = 0, Length = 4, AnchorText = "Kael", Text = "first" });
        int rowan = Text.IndexOf("Rowan", StringComparison.Ordinal);
        chapter.Comments.Add(new ChapterComment { Offset = rowan, Length = 5, AnchorText = "Rowan", Text = "second" });

        int at = Text.IndexOf("*** At the gate", StringComparison.Ordinal);
        Chapter second = ChapterOperations.Split(project, chapter, Text, at, "Two");

        Assert.Equal(["One", "Two"], project.Chapters.Select(static c => c.Title));
        Assert.Equal("Rowan left.", second.Text);
        Assert.Equal(0, Assert.Single(second.Comments).Offset);
        Assert.EndsWith("Wren waited.", chapter.Text);
        Assert.Single(chapter.Comments);

        string merged = ChapterOperations.Merge(project, chapter, chapter.Text, second, second.Text);
        Assert.Single(project.Chapters);
        Assert.Contains($"Wren waited.{Environment.NewLine}{Environment.NewLine}* * *{Environment.NewLine}{Environment.NewLine}Rowan left.", merged);
        ChapterComment moved = chapter.Comments.Single(comment => comment.Text == "second");
        Assert.Equal("Rowan", merged.Substring(moved.Offset, 5));
    }

    [Fact]
    public void FindsTodoFixmeAndTk() {
        Chapter chapter = new("One");
        IReadOnlyList<ManuscriptTask> tasks = TaskScanner.Scan(chapter, "He rode to [TK] city. TODO: check the horse's name\nFIXME tense\nA todo list is fine.");
        Assert.Equal(["TK", "TODO", "FIXME"], tasks.Select(static task => task.Kind));
        Assert.Equal("check the horse's name", tasks[1].Text);
    }
}

public sealed class RevisionTests {
    [Fact]
    public void ComparesAcceptsAndRejects() {
        const string baseline = "The wolf ran fast.\r\nShe waited by the gate.";
        const string current = "The grey wolf ran.\r\nShe waited by the gate.";
        IReadOnlyList<RevisionChange> changes = RevisionTracker.Compare(baseline, current);
        Assert.Equal(2, changes.Count);
        RevisionChange insertion = changes[0];
        Assert.True(insertion.IsInsertion);
        Assert.Equal("grey ", current.Substring(insertion.Offset, insertion.Length));
        RevisionChange deletion = changes[1];
        Assert.True(deletion.IsDeletion);
        Assert.Equal(" fast", deletion.Deleted);

        string accepted = RevisionTracker.Accept(baseline, insertion);
        Assert.Single(RevisionTracker.Compare(accepted, current));

        string rejected = RevisionTracker.Reject(current, deletion);
        Assert.Equal("The grey wolf ran fast.\r\nShe waited by the gate.", rejected);
        Assert.Empty(RevisionTracker.Compare(RevisionTracker.Accept(accepted, RevisionTracker.Compare(accepted, current)[0]), current));
    }

    [Fact]
    public void ReportsPlotThreadProblems() {
        NovelProject project = new();
        PlotThread compass = new("The compass");
        PlotThread letter = new("The letter");
        PlotThread empty = new("Unused");
        project.PlotThreads.Add(compass);
        project.PlotThreads.Add(letter);
        project.PlotThreads.Add(empty);
        Chapter one = new("One");
        Chapter two = new("Two");
        one.Comments.Add(new ChapterComment { Kind = MarkerKind.PlotThread, ThreadId = compass.Id, ThreadRole = ThreadRole.Setup, Offset = 0, Length = 4 });
        one.Comments.Add(new ChapterComment { Kind = MarkerKind.PlotThread, ThreadId = letter.Id, ThreadRole = ThreadRole.Payoff, Offset = 5, Length = 3 });
        two.Comments.Add(new ChapterComment { Kind = MarkerKind.PlotThread, ThreadId = letter.Id, ThreadRole = ThreadRole.Setup, Offset = 0, Length = 3 });

        IReadOnlyList<ThreadReport> reports = PlotThreadAnalyzer.Analyze(project, [new ChapterText(one, "Kael met the end."), new ChapterText(two, "The letter.")]);

        Assert.Contains("never paid off", Assert.Single(reports[0].Problems));
        Assert.Equal("Kael", reports[0].Markers[0].Excerpt);
        Assert.Contains("comes before the first setup", Assert.Single(reports[1].Problems));
        Assert.Contains("Nothing in the manuscript", Assert.Single(reports[2].Problems));
    }
}

public sealed class StoryWorldRuleTests {
    private readonly GrammarEngine _engine = new();

    [Fact]
    public void ChecksItemOwnershipBirthYearAndRelationships() {
        StoryEntity kael = new(EntityKind.Character, "Kael");
        StoryEntity wren = new(EntityKind.Character, "Wren");
        wren.Relationships.Add(new EntityRelationship(kael.Id, "daughter of"));
        wren.Attributes.Add(new EntityAttribute("Born", "1180"));
        StoryEntity compass = new(EntityKind.Item, "Brass Compass") { Aliases = "compass" };
        compass.Attributes.Add(new EntityAttribute("Owner", "Kael"));
        GrammarOptions options = new GrammarOptions(new EntityIndex([kael, wren, compass]), new HashSet<string>(), new HashSet<string>()) with { StoryDate = "Spring 1204" };

        string text = "Wren's old compass spun. Kael's sister Wren laughed. Wren, forty, smiled. Wren was 24 years old. Kael's daughter Wren slept. Kael's compass shone.";
        IReadOnlyList<GrammarDiagnostic> diagnostics = _engine.Check(text, options);

        Assert.Equal(0, Assert.Single(diagnostics, d => d.RuleId == "NW4007").Offset);
        GrammarDiagnostic age = Assert.Single(diagnostics, d => d.RuleId == "NW4008");
        Assert.Equal("forty", text.Substring(age.Offset, age.Length));
        GrammarDiagnostic family = Assert.Single(diagnostics, d => d.RuleId == "NW4009");
        Assert.Equal("sister", text.Substring(family.Offset, family.Length));
        Assert.Contains("daughter of", family.Message);
    }
}

public sealed class ExportTests : IDisposable {
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "WritingIDE.Tests", Guid.NewGuid().ToString("N"));

    public ExportTests() {
        Directory.CreateDirectory(_directory);
    }

    private static (NovelProject Project, ExportOptions Options) Sample(ExportFormat format, ExportStyle style = ExportStyle.Manuscript) {
        NovelProject project = new() { Name = "The Lighthouse Keeper", Author = "Ada Lovelace" };
        Chapter one = new("Chapter 1") { Text = "The wind rose.\nShe climbed *slowly* up the **stairs**.\n\n* * *\n\nNight fell." };
        Chapter two = new("Chapter 2") { Text = "Morning came & went." };
        project.Chapters.Add(one);
        project.Chapters.Add(two);
        return (project, new ExportOptions { Format = format, Style = style, Chapters = [one, two], Contact = "Ada Lovelace\nada@example.com" });
    }

    [Fact]
    public void ParsesParagraphsSceneBreaksAndEmphasis() {
        IReadOnlyList<ExportParagraph> paragraphs = ManuscriptDocumentBuilder.ParseParagraphs("* * *\nOne *two* **three** _four_.\n\n#\n\n#\nEnd.\n***");
        Assert.Equal(3, paragraphs.Count);
        Assert.True(paragraphs[1].IsSceneBreak);
        Assert.Equal([new InlineRun("One ", false, false), new InlineRun("two", true, false), new InlineRun(" ", false, false), new InlineRun("three", false, true), new InlineRun(" ", false, false), new InlineRun("four", true, false), new InlineRun(".", false, false)], paragraphs[0].Runs);
    }

    [Theory]
    [InlineData(ExportStyle.Manuscript)]
    [InlineData(ExportStyle.Book)]
    public void WritesValidDocx(ExportStyle style) {
        (NovelProject project, ExportOptions options) = Sample(ExportFormat.Docx, style);
        string path = Path.Combine(_directory, $"book-{style}.docx");
        Exporter.Export(path, project, options, static chapter => chapter.Text);

        using WordprocessingDocument document = WordprocessingDocument.Open(path, false);
        List<ValidationErrorInfo> errors = new OpenXmlValidator().Validate(document).ToList();
        Assert.True(errors.Count == 0, string.Join("\n", errors.Select(static error => $"{error.Path?.XPath}: {error.Description}")));
        string text = document.MainDocumentPart!.Document!.Body!.InnerText;
        Assert.Contains("Chapter 1", text);
        Assert.Contains("She climbed slowly up the stairs.", text);
        Assert.Contains("Morning came & went.", text);
    }

    [Fact]
    public void WritesEpubWithMimetypeFirstAndChapters() {
        (NovelProject project, ExportOptions options) = Sample(ExportFormat.Epub, ExportStyle.Book);
        string path = Path.Combine(_directory, "book.epub");
        Exporter.Export(path, project, options with { SceneBreak = "* * *" }, static chapter => chapter.Text);

        using ZipArchive zip = ZipFile.OpenRead(path);
        Assert.Equal("mimetype", zip.Entries[0].FullName);
        Assert.Equal(CompressionLevel.NoCompression == 0 ? zip.Entries[0].Length : zip.Entries[0].Length, zip.Entries[0].CompressedLength);
        string chapter = new StreamReader(zip.GetEntry("OEBPS/chapter001.xhtml")!.Open()).ReadToEnd();
        Assert.Contains("<em>slowly</em>", chapter);
        Assert.Contains("<strong>stairs</strong>", chapter);
        Assert.Contains("<p class=\"break\">* * *</p>", chapter);
        Assert.Contains("Morning came &amp; went.", new StreamReader(zip.GetEntry("OEBPS/chapter002.xhtml")!.Open()).ReadToEnd());
        Assert.Contains("properties=\"nav\"", new StreamReader(zip.GetEntry("OEBPS/content.opf")!.Open()).ReadToEnd());
    }

    [Theory]
    [InlineData(ExportStyle.Manuscript)]
    [InlineData(ExportStyle.Book)]
    public void WritesPdf(ExportStyle style) {
        (NovelProject project, ExportOptions options) = Sample(ExportFormat.Pdf, style);
        string path = Path.Combine(_directory, $"book-{style}.pdf");
        Exporter.Export(path, project, options, static chapter => chapter.Text);
        byte[] bytes = File.ReadAllBytes(path);
        Assert.Equal("%PDF", Encoding.ASCII.GetString(bytes, 0, 4));
        Assert.True(bytes.Length > 2000);
    }

    [Fact]
    public void WritesLuluInteriorAtTrimSizePlusBleed() {
        (NovelProject project, ExportOptions options) = Sample(ExportFormat.Lulu);
        string path = Path.Combine(_directory, "lulu.pdf");
        TrimSize digest = TrimSize.Lulu.Single(static size => size.Name == "Digest");
        IReadOnlyList<string> notes = Exporter.Export(path, project, options with { TrimSize = digest }, static chapter => chapter.Text);

        using PdfSharp.Pdf.PdfDocument pdf = PdfSharp.Pdf.IO.PdfReader.Open(path, PdfSharp.Pdf.IO.PdfDocumentOpenMode.Import);
        // Title page, copyright page, then one page per chapter.
        Assert.Equal(4, pdf.PageCount);
        Assert.All(pdf.Pages.Cast<PdfSharp.Pdf.PdfPage>(), static page => {
            Assert.Equal((5.5 + 0.25) * 72, page.Width.Point, 3);
            Assert.Equal((8.5 + 0.25) * 72, page.Height.Point, 3);
        });
        Assert.Contains(notes, static note => note.Contains("4 pages at Digest (5.5 x 8.5 in)", StringComparison.Ordinal));
        Assert.Contains(notes, static note => note.Contains("at least 32 pages", StringComparison.Ordinal));
    }

    [Fact]
    public void LuluGutterGrowsWithThePageCount() {
        Assert.Equal(0, PdfExporter.LuluGutter(150));
        Assert.True(PdfExporter.LuluGutter(151) > 0);
        Assert.True(PdfExporter.LuluGutter(700) > PdfExporter.LuluGutter(300));
    }

    [Fact]
    public void AiContextIncludesBibleElementsAndText() {
        (NovelProject project, ExportOptions options) = Sample(ExportFormat.AiContext);
        project.Notes = "Magic costs memories.";
        StoryEntity kael = new(EntityKind.Character, "Kael") { Summary = "A keeper." };
        kael.Attributes.Add(new EntityAttribute("Age", "41"));
        project.Entities.Add(kael);
        project.PlotThreads.Add(new PlotThread("The compass"));
        string context = AiContextExporter.Build(project, options with { AiInstructions = "Check pacing." }, static chapter => chapter.Text);
        Assert.Contains("Magic costs memories.", context);
        Assert.Contains("- Age: 41", context);
        Assert.Contains("The compass", context);
        Assert.Contains("Check pacing.", context);
        Assert.Contains("Night fell.", context);
        Assert.DoesNotContain("Night fell.", AiContextExporter.Build(project, options with { IncludeFullText = false }, static chapter => chapter.Text));
    }

    public void Dispose() {
        if (Directory.Exists(_directory)) {
            Directory.Delete(_directory, recursive: true);
        }
    }
}

public sealed class AiTests {
    /// <summary>Answers requests from memory: no network.</summary>
    private sealed class FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler {
        public List<(HttpRequestMessage Request, string Body)> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) {
            Requests.Add((request, request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken)));
            return respond(request);
        }
    }

    private static HttpResponseMessage Json(string json, HttpStatusCode status = HttpStatusCode.OK) {
        return new HttpResponseMessage(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
    }

    [Fact]
    public async Task OpenAiCompatibleClientSendsBearerKeyAndReadsTheReply() {
        FakeHandler handler = new(_ => Json("""{"choices":[{"message":{"role":"assistant","content":"A synopsis."}}]}"""));
        IAiClient client = AiClientFactory.Create(new AiConnection(AiProviderKind.Groq, "https://api.example/v1", "model-x", "secret"), new HttpClient(handler));
        string reply = await client.CompleteAsync(AiTools.Summarize("One", "Text."));
        Assert.Equal("A synopsis.", reply);
        (HttpRequestMessage request, string body) = Assert.Single(handler.Requests);
        Assert.Equal("https://api.example/v1/chat/completions", request.RequestUri!.ToString());
        Assert.Equal("Bearer secret", request.Headers.Authorization!.ToString());
        Assert.Contains("\"model\":\"model-x\"", body);
        Assert.Contains("\"role\":\"system\"", body);
    }

    [Fact]
    public async Task GeminiClientPutsKeyInHeaderAndSystemPromptSeparately() {
        FakeHandler handler = new(_ => Json("""{"candidates":[{"content":{"parts":[{"text":"Hello "},{"text":"there"}]}}]}"""));
        IAiClient client = AiClientFactory.Create(new AiConnection(AiProviderKind.Gemini, "https://gemini.example/v1beta", "gemini-x", "k3y"), new HttpClient(handler));
        Assert.Equal("Hello there", await client.CompleteAsync([AiMessage.System("Be nice."), AiMessage.User("Hi"), AiMessage.Assistant("Yes?"), AiMessage.User("Go")]));
        (HttpRequestMessage request, string body) = Assert.Single(handler.Requests);
        Assert.Equal("https://gemini.example/v1beta/models/gemini-x:generateContent", request.RequestUri!.ToString());
        Assert.DoesNotContain("k3y", request.RequestUri.ToString());
        Assert.Equal("k3y", request.Headers.GetValues("x-goog-api-key").Single());
        Assert.Contains("\"systemInstruction\"", body);
        Assert.Contains("\"role\":\"model\"", body);
    }

    [Fact]
    public async Task ErrorsBecomeReadableMessages() {
        FakeHandler handler = new(_ => Json("""{"error":{"message":"Rate limit reached"}}""", HttpStatusCode.TooManyRequests));
        IAiClient client = AiClientFactory.Create(new AiConnection(AiProviderKind.OpenRouter, "https://x/v1", "m", "key"), new HttpClient(handler));
        AiException error = await Assert.ThrowsAsync<AiException>(() => client.CompleteAsync([AiMessage.User("hi")]));
        Assert.Contains("free-tier limit", error.Message);
        Assert.Contains("Rate limit reached", error.Message);
        Assert.Throws<AiException>(() => AiClientFactory.Create(new AiConnection(AiProviderKind.Gemini, "https://x", "m", ""), new HttpClient()));
    }

    [Fact]
    public async Task ListsModels() {
        FakeHandler handler = new(_ => Json("""{"data":[{"id":"b-model"},{"id":"a-model"}]}"""));
        IAiClient client = AiClientFactory.Create(new AiConnection(AiProviderKind.Ollama, "http://localhost:11434/v1", "m", ""), new HttpClient(handler));
        Assert.Equal(["a-model", "b-model"], await client.ListModelsAsync());
    }

    [Fact]
    public void ParsesAndAnchorsNotes() {
        string reply = "Here you go:\n```json\n[{\"quote\": \"the iron railings\", \"comment\": \"Nice image.\"}, {\"quote\": \"missing words\", \"comment\": \"Unclear.\"}, {\"quote\": \"x\"}]\n```";
        IReadOnlyList<AiNote> notes = AiTools.ParseNotes(reply);
        Assert.Equal(2, notes.Count);
        const string text = "She could hear it singing in the iron  railings of the light.";
        IReadOnlyList<AnchoredNote> anchored = AiTools.Anchor(text, [notes[0], notes[1], new AiNote("\u201CThe Iron Railings\u201D", "case")]);
        Assert.Equal("the iron  railings", text.Substring(anchored[0].Offset, anchored[0].Length));
        Assert.Equal((0, 0), (anchored[1].Offset, anchored[1].Length));
        Assert.Equal("iron  railings", text.Substring(anchored[2].Offset, anchored[2].Length)[4..]);
        Assert.Empty(AiTools.ParseNotes("No problems found."));
        Assert.Equal("Tighter text.", AiTools.CleanRewrite("```\nTighter text.\n```"));
        Assert.Equal("Tighter text.", AiTools.CleanRewrite("\"Tighter text.\""));
    }
}