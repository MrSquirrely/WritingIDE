using WritingIDE.Core.Grammar;
using WritingIDE.Core.Model;

namespace WritingIDE.Sdk;

/// <summary>
/// The entry point of a WritingIDE extension. Build a class library that references WritingIDE.Sdk, implement this
/// interface (with a public parameterless constructor), and copy the DLL into
/// %LocalAppData%\WritingIDE\Extensions\&lt;YourExtension&gt;\. WritingIDE calls <see cref="Initialize"/> once at start-up.
/// </summary>
public interface IWritingIdePlugin {
    /// <summary>A unique id, such as "com.example.wordfrequency".</summary>
    string Id { get; }

    string Name { get; }

    string Version { get; }

    string Description { get; }

    /// <summary>Register rules, exporters and commands, and subscribe to events.</summary>
    void Initialize(IPluginHost host);
}

/// <summary>What WritingIDE offers an extension.</summary>
public interface IPluginHost {
    /// <summary>The SDK version of the running WritingIDE (semantic versioning).</summary>
    Version SdkVersion { get; }

    /// <summary>The open project (check <see cref="IProjectApi.IsOpen"/>).</summary>
    IProjectApi Project { get; }

    /// <summary>Adds a grammar rule: it runs on every chapter like a built-in rule, and its findings go to the Error List.</summary>
    void RegisterRule(GrammarRule rule);

    /// <summary>Adds an export format to File > Export.</summary>
    void RegisterExporter(IManuscriptExporter exporter);

    /// <summary>Adds a command to the Extensions menu.</summary>
    void RegisterCommand(PluginCommand command);

    /// <summary>Writes a line to the Output window.</summary>
    void Log(string message);

    /// <summary>Shows a notification.</summary>
    void Notify(string message);

    event EventHandler? ProjectOpened;

    event EventHandler? ProjectClosed;

    event EventHandler? ProjectSaved;
}

/// <summary>A command shown in the Extensions menu.</summary>
public sealed record PluginCommand(string Title, Action<IProjectApi> Execute) {
    public string Description { get; init; } = "";
}

/// <summary>An export format contributed by an extension.</summary>
public interface IManuscriptExporter {
    /// <summary>Shown in the Export dialog, such as "Plain text".</summary>
    string Name { get; }

    /// <summary>Such as ".txt".</summary>
    string FileExtension { get; }

    void Export(string path, IProjectApi project);
}

/// <summary>A chapter, as seen through the API.</summary>
public sealed record ChapterInfo(Guid Id, string Title, Guid? BookId, string? BookTitle, string Status, string Synopsis, string PointOfView, string StoryDate, int Words);

/// <summary>A story element, as seen through the API.</summary>
public sealed record EntityInfo(Guid Id, EntityKind Kind, string Name, IReadOnlyList<string> Aliases, string Summary, IReadOnlyDictionary<string, string> Attributes);

/// <summary>An Error List entry.</summary>
public sealed record DiagnosticInfo(Guid ChapterId, string Chapter, int Line, int Column, string Code, string Severity, string Category, string Message);

/// <summary>A comment, bookmark or plot-thread marker.</summary>
public sealed record CommentInfo(Guid Id, Guid ChapterId, string Kind, int Offset, int Length, string AnchorText, string Text, bool Resolved);

/// <summary>
/// Reading and editing the open project. Safe to call from any thread: calls are run on WritingIDE's UI thread.
/// Text edits to open chapters go through the editor, so the writer can undo them.
/// </summary>
public interface IProjectApi {
    bool IsOpen { get; }

    string Name { get; }

    string Author { get; }

    string DirectoryPath { get; }

    /// <summary>Chapters in reading order.</summary>
    IReadOnlyList<ChapterInfo> GetChapters();

    string GetChapterText(Guid chapterId);

    void SetChapterText(Guid chapterId, string text);

    /// <summary>Adds a chapter at the end of the manuscript (or of the book) and returns its id.</summary>
    Guid AddChapter(string title, string text, Guid? bookId = null);

    IReadOnlyList<EntityInfo> GetEntities();

    Guid AddEntity(EntityKind kind, string name, string summary = "");

    IReadOnlyList<CommentInfo> GetComments(Guid chapterId);

    Guid AddComment(Guid chapterId, int offset, int length, string text);

    /// <summary>The current Error List (every chapter).</summary>
    IReadOnlyList<DiagnosticInfo> GetDiagnostics();

    void Save();
}