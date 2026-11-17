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

    /// <summary>Register rules, exporters, commands and tool windows, and subscribe to events.</summary>
    void Initialize(IPluginHost host);
}

/// <summary>What WritingIDE offers an extension.</summary>
public interface IPluginHost {
    /// <summary>The SDK version of the running WritingIDE (semantic versioning).</summary>
    Version SdkVersion { get; }

    /// <summary>The open project (check <see cref="IProjectApi.IsOpen"/>).</summary>
    IProjectApi Project { get; }

    /// <summary>The chapter editor: the selection, the caret, and edits at the caret.</summary>
    IEditorApi Editor { get; }

    /// <summary>This extension's own settings and files, per user and per project.</summary>
    IPluginStorage Storage { get; }

    /// <summary>Adds a grammar rule: it runs on every chapter like a built-in rule, and its findings go to the Error List.</summary>
    void RegisterRule(GrammarRule rule);

    /// <summary>Adds an export format to File > Export.</summary>
    void RegisterExporter(IManuscriptExporter exporter);

    /// <summary>Adds a command to the Extensions menu.</summary>
    void RegisterCommand(PluginCommand command);

    /// <summary>
    /// Adds a tool window. It is listed in the Extensions menu and opens as a tab in the document well; the writer
    /// can also move it to the second pane.
    /// </summary>
    void RegisterToolWindow(PluginToolWindow window);

    /// <summary>Opens (or brings forward) a tool window this extension registered. Needs an open project.</summary>
    void ShowToolWindow(string id);

    /// <summary>Adds a command to the chapter editor's right-click menu.</summary>
    void RegisterEditorCommand(EditorCommand command);

    /// <summary>Shows a message in the status bar.</summary>
    void SetStatusText(string text);

    /// <summary>Writes a line to the Output window.</summary>
    void Log(string message);

    /// <summary>Shows a notification.</summary>
    void Notify(string message);

    event EventHandler? ProjectOpened;

    event EventHandler? ProjectClosed;

    event EventHandler? ProjectSaved;

    /// <summary>
    /// The writer switched to another chapter, or to a tab that is not a chapter (<see cref="ChapterEventArgs.ChapterId"/>
    /// is then null). Raised on the UI thread.
    /// </summary>
    event EventHandler<ChapterEventArgs>? ActiveChapterChanged;

    /// <summary>
    /// The text of an open chapter changed. Raised on the UI thread for every edit (each keystroke), so keep handlers
    /// quick or start a timer.
    /// </summary>
    event EventHandler<ChapterEventArgs>? ChapterTextChanged;

    /// <summary>The caret moved or the selection changed in a chapter editor. Raised on the UI thread; keep handlers quick.</summary>
    event EventHandler<EditorSelectionEventArgs>? SelectionChanged;

    /// <summary>A writing sprint ended (ran out, or was stopped early). Raised on the UI thread.</summary>
    event EventHandler<SprintEventArgs>? SprintFinished;

    /// <summary>Today's words reached the project's daily goal (once a day). Raised on the UI thread.</summary>
    event EventHandler? DailyGoalReached;
}

/// <summary>How a writing sprint went.</summary>
public sealed class SprintEventArgs(int words, double minutes, bool completed) : EventArgs {
    /// <summary>Words added during the sprint, net of deletions.</summary>
    public int Words { get; } = words;

    /// <summary>How long the sprint ran.</summary>
    public double Minutes { get; } = minutes;

    /// <summary>False when the writer stopped it before the clock ran out.</summary>
    public bool Completed { get; } = completed;
}

/// <summary>The writer's progress in the open project.</summary>
/// <param name="TotalWords">The manuscript's length.</param>
/// <param name="TargetWords">The length the writer is aiming for.</param>
/// <param name="WordsToday">Words added today, net of deletions.</param>
/// <param name="DailyGoal">Words to write each day; 0 when no goal is set.</param>
/// <param name="Streak">Days in a row that met the goal (any words when there is no goal).</param>
/// <param name="BestStreak">The longest such run in the writing log.</param>
/// <param name="DaysWritten">Days in the writing log with words added.</param>
public sealed record WritingStats(int TotalWords, int TargetWords, int WordsToday, int DailyGoal, int Streak, int BestStreak, int DaysWritten);

/// <summary>Names a chapter in an event (null when no chapter is active).</summary>
public sealed class ChapterEventArgs(Guid? chapterId) : EventArgs {
    public Guid? ChapterId { get; } = chapterId;
}

/// <summary>The caret and selection after they changed.</summary>
public sealed class EditorSelectionEventArgs(Guid chapterId, int caretOffset, int selectionStart, int selectionLength) : EventArgs {
    public Guid ChapterId { get; } = chapterId;

    public int CaretOffset { get; } = caretOffset;

    public int SelectionStart { get; } = selectionStart;

    public int SelectionLength { get; } = selectionLength;
}

/// <summary>
/// A tool window contributed by an extension. <paramref name="CreateContent"/> is called on the UI thread, once each
/// time the window is opened, and returns a WPF element (a UserControl, say). An extension with tool windows targets
/// net10.0-windows with UseWPF. The SDK itself does not depend on WPF, which is why the type is object.
/// </summary>
/// <param name="Id">Unique within the extension, such as "sprint".</param>
/// <param name="Title">The tab's title and the menu entry.</param>
/// <param name="CreateContent">Creates the window's content.</param>
public sealed record PluginToolWindow(string Id, string Title, Func<object> CreateContent) {
    public string Description { get; init; } = "";

    /// <summary>Called on the UI thread when the tab is closed (also when the project closes).</summary>
    public Action? Closed { get; init; }
}

/// <summary>Where the writer right-clicked in a chapter, and what is selected.</summary>
/// <param name="ChapterId">The chapter in the editor.</param>
/// <param name="Offset">The clicked position (the caret, when the menu was opened from the keyboard).</param>
/// <param name="SelectionStart">Where the selection starts.</param>
/// <param name="SelectionLength">0 when nothing is selected.</param>
/// <param name="SelectedText">The selected text, or "".</param>
/// <param name="Word">The word at <paramref name="Offset"/>, or "".</param>
/// <param name="EntityId">The story element whose name is at <paramref name="Offset"/>, if any.</param>
public sealed record EditorContext(Guid ChapterId, int Offset, int SelectionStart, int SelectionLength, string SelectedText, string Word, Guid? EntityId);

/// <summary>A command in the chapter editor's right-click menu.</summary>
public sealed record EditorCommand(string Title, Action<EditorContext> Execute) {
    public string Description { get; init; } = "";

    /// <summary>When set, the command is only listed where this returns true (for example, only with a selection).</summary>
    public Func<EditorContext, bool>? IsAvailable { get; init; }
}

/// <summary>
/// The chapter editor that is in front. Safe to call from any thread. Offsets count characters from the start of the
/// chapter's text.
/// </summary>
public interface IEditorApi {
    /// <summary>The chapter in the active tab, or null when the active tab is not a chapter.</summary>
    Guid? ActiveChapterId { get; }

    int CaretOffset { get; }

    int SelectionStart { get; }

    int SelectionLength { get; }

    string SelectedText { get; }

    /// <summary>
    /// Replaces the selection (or inserts at the caret when nothing is selected). The writer can undo it.
    /// False when no chapter is active.
    /// </summary>
    bool ReplaceSelection(string text);

    /// <summary>Opens the chapter, selects the range and scrolls it into view.</summary>
    void Select(Guid chapterId, int offset, int length);
}

/// <summary>An extension's own data. Keys are the extension's to choose; setting a value to null removes it.</summary>
public interface IPluginStorage {
    /// <summary>A per-user folder for this extension's files (created when first read).</summary>
    string DataDirectory { get; }

    /// <summary>A per-user setting (kept in <see cref="DataDirectory"/>), or null.</summary>
    string? GetSetting(string key);

    void SetSetting(string key, string? value);

    /// <summary>A value saved in the open project's file, so it travels with the project. Null when unset or no project is open.</summary>
    string? GetProjectValue(string key);

    /// <summary>Stores a value in the open project (written when the writer saves).</summary>
    void SetProjectValue(string key, string? value);
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

    /// <summary>The genre the writer gave the project ("Fantasy", "Cozy mystery"), or "".</summary>
    string Genre { get; }

    /// <summary>Words written, today's words against the goal, and streaks.</summary>
    WritingStats GetWritingStats();

    string DirectoryPath { get; }

    /// <summary>Chapters in reading order.</summary>
    IReadOnlyList<ChapterInfo> GetChapters();

    string GetChapterText(Guid chapterId);

    void SetChapterText(Guid chapterId, string text);

    /// <summary>Adds a chapter at the end of the manuscript (or of the book) and returns its id.</summary>
    Guid AddChapter(string title, string text, Guid? bookId = null);

    /// <summary>Changes a chapter's details; arguments left null stay as they are.</summary>
    void UpdateChapter(Guid chapterId, string? title = null, string? synopsis = null, string? pointOfView = null, string? storyDate = null);

    IReadOnlyList<EntityInfo> GetEntities();

    Guid AddEntity(EntityKind kind, string name, string summary = "");

    string GetEntityNotes(Guid entityId);

    /// <summary>Changes a story element's summary or notes; arguments left null stay as they are.</summary>
    void UpdateEntity(Guid entityId, string? summary = null, string? notes = null);

    /// <summary>Sets an attribute such as "Age" (added when missing); null removes it.</summary>
    void SetEntityAttribute(Guid entityId, string key, string? value);

    IReadOnlyList<CommentInfo> GetComments(Guid chapterId);

    Guid AddComment(Guid chapterId, int offset, int length, string text);

    /// <summary>Deletes a comment, bookmark or plot-thread marker. False when the chapter has none with that id.</summary>
    bool RemoveComment(Guid chapterId, Guid commentId);

    /// <summary>A story element's relationships: the other element's id and the label ("sister of").</summary>
    IReadOnlyList<(Guid TargetId, string Label)> GetEntityRelationships(Guid entityId);

    /// <summary>Opens a story element's page in the document well.</summary>
    void OpenEntity(Guid entityId);

    /// <summary>
    /// Checks the open chapters again, so that a rule whose settings changed (a pack switched on, a map edited)
    /// shows its new findings without the writer pressing F6.
    /// </summary>
    void Reanalyze();

    /// <summary>The current Error List (every chapter).</summary>
    IReadOnlyList<DiagnosticInfo> GetDiagnostics();

    void Save();
}