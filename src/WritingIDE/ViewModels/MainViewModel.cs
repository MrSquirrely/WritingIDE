using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;

using HandyControl.Controls;
using HandyControl.Data;

using Microsoft.Win32;

using WritingIDE.Core;
using WritingIDE.Core.Analysis;
using WritingIDE.Core.Grammar;
using WritingIDE.Core.Import;
using WritingIDE.Core.Model;
using WritingIDE.Core.Persistence;
using WritingIDE.Core.Search;
using WritingIDE.Core.Text;
using WritingIDE.Editor;
using WritingIDE.Infrastructure;
using WritingIDE.Plugins;
using WritingIDE.Services;
using WritingIDE.Views;

using HcMessageBox = HandyControl.Controls.MessageBox;

namespace WritingIDE.ViewModels;

/// <summary>The IDE shell: project, solution explorer, document well, error list and output.</summary>
public sealed partial class MainViewModel : ObservableObject {
    public const int ErrorListTab = 0;
    public const int OutputTab = 1;
    public const int FindResultsTab = 2;
    public const int GitTab = 3;
    public const int BookmarksTab = 4;
    public const int TaskListTab = 5;

    private readonly WindowsSpellingProvider _spelling = new();
    private readonly Dictionary<Guid, List<ErrorItem>> _diagnostics = new();
    private readonly Dictionary<Guid, int> _wordCounts = new();

    /// <summary>Per chapter: how often each story element is mentioned (from the latest analysis). Feeds the name lenses.</summary>
    private readonly Dictionary<Guid, Dictionary<Guid, int>> _mentionCounts = new();
    private readonly DispatcherTimer _optionsRefreshTimer;
    private readonly StringBuilder _output = new();
    private GrammarOptions? _grammarOptions;
    private StartPageViewModel? _startPage;

    public MainViewModel() {
        Grammar = new GrammarEngine(_spelling);
        Grammar.RuleFailed += (_, failure) => Application.Current?.Dispatcher.BeginInvoke(() => Log($"warning: rule {failure.Rule.Id} failed: {failure.Exception.Message}"));
        _optionsRefreshTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
        _optionsRefreshTimer.Tick += (_, _) => {
            _optionsRefreshTimer.Stop();
            ReanalyzeOpenDocuments();
        };

        NewProjectCommand = new RelayCommand(NewProject);
        OpenProjectCommand = new RelayCommand(parameter => OpenProject(parameter as string));
        SaveAllCommand = new RelayCommand(SaveAll, () => HasProject);
        CloseProjectCommand = new RelayCommand(() => CloseProject(), () => HasProject);
        ExportCommand = new RelayCommand(ShowExportDialog, () => HasProject);
        ExitCommand = new RelayCommand(() => Application.Current.MainWindow?.Close());
        AddChapterCommand = new RelayCommand(() => AddNewItem(null, chapter: true), () => HasProject);
        AddEntityCommand = new RelayCommand(parameter => AddNewItem(ParseKind(parameter), chapter: false), _ => HasProject);
        AddNewItemCommand = new RelayCommand(() => AddNewItem(null, chapter: false), () => HasProject);
        AnalyzeAllCommand = new RelayCommand(() => _ = AnalyzeAllAsync(quiet: false), () => HasProject && !IsAnalyzing);
        CloseDocumentCommand = new RelayCommand(parameter => CloseDocument(parameter as DocumentViewModel ?? ActiveDocument));
        NavigateToErrorCommand = new RelayCommand(parameter => {
            if (parameter is ErrorItem item) {
                NavigateTo(item.Chapter, item.Diagnostic.Offset, item.Diagnostic.Length);
            }
        });
        NavigateToReferenceCommand = new RelayCommand(parameter => {
            if (parameter is ReferenceItem item) {
                NavigateTo(item.Chapter, item.Offset, item.Length);
            }
        });
        ShowStartPageCommand = new RelayCommand(ShowStartPage);
        OptionsCommand = new RelayCommand(ShowOptions, () => HasProject);
        AboutCommand = new RelayCommand(ShowAbout);
        ImportCommand = new RelayCommand(Import, () => HasProject);
        ClearOutputCommand = new RelayCommand(() => {
            _output.Clear();
            OnPropertyChanged(nameof(OutputText));
        });
        AddBookCommand = new RelayCommand(() => AddBook(), () => Project?.IsSeries ?? false);
        ConvertToSeriesCommand = new RelayCommand(ConvertToSeries, () => (Project is not null) && !Project.IsSeries);
        OpenStoryBibleCommand = new RelayCommand(OpenStoryBible, () => HasProject);
        PluginsCommand = new RelayCommand(ShowPlugins);
        SetThemeCommand = new RelayCommand(parameter => Theme = AppTheme.Parse(parameter as string));
        FindInFilesCommand = new RelayCommand(() => ShowFindAndReplace(replace: false), () => HasProject);
        ReplaceInFilesCommand = new RelayCommand(() => ShowFindAndReplace(replace: true), () => HasProject);
        ToggleDistractionFreeCommand = new RelayCommand(() => IsDistractionFree = !IsDistractionFree, () => IsDistractionFree || (ActiveChapter is not null));
        ExitDistractionFreeCommand = new RelayCommand(() => IsDistractionFree = false);
        TakeSnapshotCommand = new RelayCommand(() => ActiveChapter?.TakeSnapshot(null), () => ActiveChapter is not null);
        AddCommentCommand = new RelayCommand(() => ActiveChapter?.AddCommentCommand.Execute(null), () => ActiveChapter is not null);
        ListCommentsCommand = new RelayCommand(ListComments, () => HasProject);
        OpenCharacterStatisticsCommand = new RelayCommand(() => OpenToolDocument(() => new CharacterStatisticsDocumentViewModel(this)), () => HasProject);
        OpenTimelineCommand = new RelayCommand(() => OpenToolDocument(() => new TimelineDocumentViewModel(this)), () => HasProject);
        OpenRelationshipMapCommand = new RelayCommand(() => OpenToolDocument(() => new RelationshipMapDocumentViewModel(this)), () => HasProject);
        AppTheme.Changed += () => {
            OnPropertyChanged(nameof(Theme));
            OnPropertyChanged(nameof(IsSystemTheme));
            OnPropertyChanged(nameof(IsLightTheme));
            OnPropertyChanged(nameof(IsDarkTheme));
        };

        Settings = AppSettings.Load();
        Harper = new HarperPlugin(Settings.Harper);
        Harper.Log += line => Application.Current?.Dispatcher.BeginInvoke(() => Log(line));
        Harper.StateChanged += () => Application.Current?.Dispatcher.BeginInvoke(OnPluginStateChanged);

        _recoveryTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(RecoveryIntervalSeconds) };
        _recoveryTimer.Tick += (_, _) => WriteRecoverySnapshot();
        _recoveryTimer.Start();
        _autosaveTimer = new DispatcherTimer();
        _autosaveTimer.Tick += (_, _) => {
            if ((Project is not null) && IsProjectDirty) {
                SaveAll(automatic: true);
            }
        };
        ApplyAutosaveInterval();
        SetAutosaveCommand = new RelayCommand(parameter => AutosaveMinutes = int.TryParse(parameter as string, out int minutes) ? minutes : 0);
        Git = new GitViewModel(this);
        Ai = new AiService(this);
        InitializeWritingTools();

        Log("WritingIDE ready.");
        if (Settings.Harper.Enabled) {
            _ = Harper.StartAsync();
        }
    }

    public AppSettings Settings { get; }

    public HarperPlugin Harper { get; }

    public ICommand AddBookCommand { get; }

    public ICommand ConvertToSeriesCommand { get; }

    public ICommand OpenStoryBibleCommand { get; }

    public ICommand PluginsCommand { get; }

    public ICommand SetThemeCommand { get; }

    public ICommand FindInFilesCommand { get; }

    public ICommand ReplaceInFilesCommand { get; }

    private FindReplaceDialog? _findDialog;

    // =====================================================================
    // Writing tools: focus mode, comments, snapshots, lenses, statistics, timeline, map
    // =====================================================================

    public ICommand ToggleDistractionFreeCommand { get; }

    public ICommand ExitDistractionFreeCommand { get; }

    public ICommand TakeSnapshotCommand { get; }

    public ICommand AddCommentCommand { get; }

    public ICommand ListCommentsCommand { get; }

    public ICommand OpenCharacterStatisticsCommand { get; }

    public ICommand OpenTimelineCommand { get; }

    public ICommand OpenRelationshipMapCommand { get; }

    /// <summary>View > Distraction-Free Writing (F11): full screen, only the chapter text.</summary>
    public bool IsDistractionFree {
        get;
        set {
            if (value && (ActiveChapter is null)) {
                Notify("Open a chapter first: distraction-free mode is for writing.", NotificationKind.Warning);
                return;
            }
            if (SetProperty(ref field, value)) {
                ApplyViewOptions();
                StatusText = value ? "Distraction-free writing. Press Esc or F11 to return." : "Ready";
            }
        }
    }

    /// <summary>View > Name Lenses.</summary>
    public bool ShowNameLenses {
        get => Settings.ShowNameLenses;
        set {
            Settings.ShowNameLenses = value;
            Settings.Save();
            OnPropertyChanged();
            ApplyViewOptions();
        }
    }

    /// <summary>View > Comments.</summary>
    public bool ShowCommentMargin {
        get => Settings.ShowCommentMargin;
        set {
            Settings.ShowCommentMargin = value;
            Settings.Save();
            OnPropertyChanged();
            ApplyViewOptions();
        }
    }

    private void ApplyViewOptions() {
        foreach (ChapterDocumentViewModel document in Documents.OfType<ChapterDocumentViewModel>()) {
            document.ApplyViewOptions();
        }
    }

    /// <summary>Opens a single-instance tool document (statistics, timeline, map), or brings it forward and refreshes it.</summary>
    private void OpenToolDocument<T>(Func<T> create) where T : DocumentViewModel {
        if (Project is null) {
            return;
        }
        T? document = Documents.OfType<T>().FirstOrDefault();
        if (document is null) {
            document = create();
            Documents.Add(document);
        }
        if (ActiveDocument == document) {
            document.OnActivated();
        }
        ActiveDocument = document;
    }

    /// <summary>Edit > List All Comments: every open comment in the Find Results window, in reading order.</summary>
    private void ListComments() {
        if (Project is null) {
            return;
        }
        References.Clear();
        int open = 0;
        int resolved = 0;
        foreach (Chapter chapter in OrderedChapters) {
            ChapterDocumentViewModel? document = Documents.OfType<ChapterDocumentViewModel>().FirstOrDefault(d => d.Chapter == chapter);
            document?.SyncComments();
            string text = GetChapterText(chapter);
            LineIndex lines = new(text);
            foreach (ChapterComment comment in chapter.Comments.Where(static comment => comment.Kind == MarkerKind.Comment).OrderBy(static comment => comment.Offset)) {
                if (comment.IsResolved) {
                    resolved++;
                    continue;
                }
                open++;
                int offset = Math.Clamp(comment.Offset, 0, text.Length);
                int length = Math.Clamp(comment.Length, 0, text.Length - offset);
                (int line, int column) = lines.GetLocation(offset);
                string quoted = text.Substring(offset, length).Replace('\n', ' ');
                string anchor = comment.IsOrphaned || (quoted.Length == 0)
                    ? "(deleted text)"
                    : $"\u201C{(quoted.Length > 60 ? quoted[..57] + "..." : quoted)}\u201D";
                References.Add(new ReferenceItem(chapter, offset, length, line, column, $"{comment.Text.Replace('\n', ' ')}   \u2014 on {anchor}") { Document = Project.GetDisplayName(chapter) });
            }
        }
        ReferencesHeader = open == 0
            ? $"No open comments{(resolved > 0 ? $" ({resolved} resolved)" : "")}. Add one with Ctrl+Alt+M in a chapter."
            : $"{open} open comment(s){(resolved > 0 ? $", {resolved} resolved" : "")}. Double-click a row to go there.";
        SelectedBottomTab = FindResultsTab;
        StatusText = ReferencesHeader;
    }

    /// <summary>Every chapter's current text in reading order (open chapters from their editors).</summary>
    public IReadOnlyList<ChapterText> GetManuscript() {
        return OrderedChapters.Select(chapter => new ChapterText(chapter, GetChapterText(chapter))).ToList();
    }

    // =====================================================================
    // Autosave and crash recovery
    // =====================================================================

    private const int RecoveryIntervalSeconds = 30;

    private readonly DispatcherTimer _recoveryTimer;
    private readonly DispatcherTimer _autosaveTimer;

    public ICommand SetAutosaveCommand { get; }

    /// <summary>Git Changes tool window.</summary>
    public GitViewModel Git { get; }

    /// <summary>Tools > Autosave: minutes between automatic saves to disk, 0 for off. Recovery snapshots run regardless.</summary>
    public int AutosaveMinutes {
        get => Settings.AutosaveMinutes;
        set {
            Settings.AutosaveMinutes = Math.Max(0, value);
            Settings.Save();
            ApplyAutosaveInterval();
            OnPropertyChanged();
            OnPropertyChanged(nameof(AutosaveLabel));
            StatusText = value > 0 ? $"Autosave every {value} minute(s)" : "Autosave off (crash recovery stays on)";
        }
    }

    public string AutosaveLabel => Settings.AutosaveMinutes > 0 ? $"every {Settings.AutosaveMinutes} min" : "off";

    private void ApplyAutosaveInterval() {
        _autosaveTimer.Stop();
        if (Settings.AutosaveMinutes > 0) {
            _autosaveTimer.Interval = TimeSpan.FromMinutes(Settings.AutosaveMinutes);
            _autosaveTimer.Start();
        }
    }

    /// <summary>
    /// Writes a crash-recovery snapshot of unsaved work (project plus every chapter's current text). Called every
    /// 30 seconds, on unhandled exceptions and when Windows shuts down. Never throws.
    /// </summary>
    public void WriteRecoverySnapshot() {
        if ((Project is null) || !IsProjectDirty) {
            return;
        }
        try {
            foreach (ChapterDocumentViewModel document in Documents.OfType<ChapterDocumentViewModel>()) {
                document.SyncComments();
            }
            RecoveryStore.Write(Project, GetChapterText);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException) {
            Log($"warning: could not write the recovery snapshot: {exception.Message}");
        }
    }

    /// <summary>Loads a project, first offering to recover unsaved work if WritingIDE did not close cleanly last time.</summary>
    private NovelProject LoadWithRecovery(string path) {
        RecoverySnapshot? snapshot = RecoveryStore.Find(path);
        if (snapshot is not null) {
            string when = snapshot.SavedAtUtc.ToLocalTime().ToString("f");
            MessageBoxResult answer = HcMessageBox.Show(Application.Current.MainWindow!,
                $"WritingIDE did not close normally while '{Path.GetFileNameWithoutExtension(path)}' had unsaved changes.\n\n" +
                $"Recover the unsaved work from {when}?\n\n" +
                "Yes: open the recovered version (review it, then Save to keep it).\nNo: discard it and open the last saved version.",
                "Recover unsaved work", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (answer == MessageBoxResult.Yes) {
                NovelProject recovered = RecoveryStore.Restore(snapshot);
                Log($"Recovered unsaved work from {when}. Save to keep it.");
                Notify("Recovered your unsaved work. Review it, then Save (Ctrl+S) to keep it.", NotificationKind.Success);
                return recovered;
            }
            RecoveryStore.Delete(path);
            Log("Discarded the recovery snapshot.");
        }
        return ProjectSerializer.Load(path);
    }

    /// <summary>View > Theme: follow Windows, light or dark. Saved in the user settings and applied immediately.</summary>
    public ThemePreference Theme {
        get => AppTheme.Parse(Settings.Theme);
        set {
            Settings.Theme = value.ToString();
            Settings.Save();
            AppTheme.Apply(value);
        }
    }

    public bool IsSystemTheme => Theme == ThemePreference.System;

    public bool IsLightTheme => Theme == ThemePreference.Light;

    public bool IsDarkTheme => Theme == ThemePreference.Dark;

    // =====================================================================
    // Plugins
    // =====================================================================

    /// <summary>Harper started or stopped: swap the engine's plugin rules and re-analyze.</summary>
    private void OnPluginStateChanged() {
        ApplyPluginRules();
        Log(Harper.IsRunning ? $"Harper plugin enabled: {Harper.Status}." : "Harper plugin disabled.");
        ReanalyzeOpenDocuments();
        if (Project is not null) {
            _ = AnalyzeAllAsync(quiet: true);
        }
    }

    /// <summary>Applies changed plugin settings: starts, restarts or stops Harper.</summary>
    public async Task ApplyPluginSettingsAsync() {
        Settings.Save();
        if (Settings.Harper.Enabled) {
            if (!await Harper.StartAsync()) {
                Notify(Harper.Status, NotificationKind.Warning);
            }
        }
        else {
            await Harper.StopAsync();
        }
    }

    private void ShowPlugins() {
        PluginsDialog dialog = new(this) { Owner = Application.Current.MainWindow };
        dialog.ShowDialog();
    }

    public void Shutdown() {
        Harper.Dispose();
        LocalApi.Stop();
    }

    // =====================================================================
    // Series and story bible
    // =====================================================================

    public Book? AddBook(string? title = null) {
        if ((Project is null) || !Project.IsSeries) {
            return null;
        }
        title ??= PromptForText("Add Book", "Book title:", $"{Project.Name}, Book {Project.Books.Count + 1}");
        if (string.IsNullOrWhiteSpace(title)) {
            return null;
        }
        Book book = new(title.Trim());
        Project.Books.Add(book);
        Chapter first = new("Chapter 1") { BookId = book.Id };
        Project.Chapters.Add(first);
        OpenChapter(first);
        Log($"Added book '{book.Title}'.");
        Notify($"Added book '{book.Title}' with its first chapter.", NotificationKind.Success);
        return book;
    }

    public void RenameBook(Book book) {
        string? title = PromptForText("Rename Book", "Book title:", book.Title);
        if (!string.IsNullOrWhiteSpace(title)) {
            book.Title = title.Trim();
            RefreshSolutionTree();
        }
    }

    public void MoveBook(Book book, int delta) {
        if (Project is null) {
            return;
        }
        int index = Project.Books.IndexOf(book);
        int target = index + delta;
        if ((index >= 0) && (target >= 0) && (target < Project.Books.Count)) {
            Project.Books.Move(index, target);
        }
    }

    public void DeleteBook(Book book) {
        if (Project is null) {
            return;
        }
        List<Chapter> chapters = Project.GetChapters(book).ToList();
        if (!Confirm($"Delete '{book.Title}' and its {chapters.Count} chapter(s)? Chapter files are deleted when you save. Story elements are shared by the series and are kept.")) {
            return;
        }
        foreach (Chapter chapter in chapters) {
            foreach (ChapterDocumentViewModel document in Documents.OfType<ChapterDocumentViewModel>().Where(d => d.Chapter == chapter).ToList()) {
                RemoveDocument(document);
            }
            if (chapter.FileName.Length > 0) {
                Project.RemovedChapterFiles.Add(chapter.FileName);
            }
            Project.Chapters.Remove(chapter);
            _diagnostics.Remove(chapter.Id);
            _wordCounts.Remove(chapter.Id);
            _mentionCounts.Remove(chapter.Id);
        }
        Project.Books.Remove(book);
        RefreshErrorList();
        Log($"Deleted book '{book.Title}'.");
    }

    public void MoveChapterToBook(Chapter chapter, Book book) {
        chapter.BookId = book.Id;
        RefreshErrorList();
    }

    /// <summary>Turns a planned (outline) chapter into a chapter-plan note in the Story/Series Bible and removes the chapter.</summary>
    public void MoveChapterToBible(Chapter chapter) {
        if (Project is null) {
            return;
        }
        NovelProject project = Project;
        string plan = ImportApplier.FormatChapterPlan(chapter.Title, project.FindBook(chapter.BookId)?.Title, chapter.Synopsis, GetChapterText(chapter));
        if (!project.Notes.Contains(plan, StringComparison.Ordinal)) {
            project.Notes = project.Notes.Length == 0 ? plan : project.Notes.TrimEnd() + Environment.NewLine + Environment.NewLine + plan;
        }
        foreach (ChapterDocumentViewModel document in Documents.OfType<ChapterDocumentViewModel>().Where(d => d.Chapter == chapter).ToList()) {
            RemoveDocument(document);
        }
        if (chapter.FileName.Length > 0) {
            project.RemovedChapterFiles.Add(chapter.FileName);
        }
        project.Chapters.Remove(chapter);
        _diagnostics.Remove(chapter.Id);
        _wordCounts.Remove(chapter.Id);
        _mentionCounts.Remove(chapter.Id);
        RefreshErrorList();
        string bible = project.IsSeries ? "Series Bible" : "Story Bible";
        Log($"Moved '{chapter.Title}' into the {bible} as a chapter plan.");
        Notify($"Moved '{chapter.Title}' into the {bible} notes.", NotificationKind.Success);
    }

    private void ConvertToSeries() {
        if ((Project is null) || Project.IsSeries) {
            return;
        }
        if (!Confirm($"Convert '{Project.Name}' into a series? Its chapters become Book 1, and you can then add more books that share the same characters, places and lore.")) {
            return;
        }
        Book book = Project.ConvertToSeries(Project.Name);
        RefreshSolutionTree();
        Log($"Converted '{Project.Name}' into a series; existing chapters are now in '{book.Title}'.");
        Notify("Converted to a series. Add books from the Project menu or by right-clicking the series.", NotificationKind.Success);
    }

    public void OpenStoryBible() {
        if (Project is null) {
            return;
        }
        StoryBibleDocumentViewModel? document = Documents.OfType<StoryBibleDocumentViewModel>().FirstOrDefault();
        if (document is null) {
            document = new StoryBibleDocumentViewModel(this, Project);
            Documents.Add(document);
        }
        ActiveDocument = document;
    }

    /// <summary>The book new chapters go into: the active chapter's book, else the selected book, else the last book.</summary>
    public Book? GetTargetBook() {
        if ((Project is null) || !Project.IsSeries) {
            return null;
        }
        return Project.FindBook(ActiveChapter?.Chapter.BookId)
            ?? SolutionItems.OfType<ProjectNode>().SelectMany(static root => root.Descendants()).OfType<BookNode>().FirstOrDefault(static node => node.IsSelected)?.Book
            ?? Project.Books.LastOrDefault();
    }

    private void RefreshSolutionTree() {
        SolutionItems.OfType<ProjectNode>().FirstOrDefault()?.RefreshManuscript();
        CommandManager.InvalidateRequerySuggested();
    }

    private static string? PromptForText(string title, string prompt, string value) {
        InputDialog dialog = new(title, prompt, value, null) { Owner = Application.Current.MainWindow };
        return dialog.ShowDialog() == true ? dialog.Value : null;
    }

    public GrammarEngine Grammar { get; }

    public NovelProject? Project { get; private set => SetProperty(ref field, value); }

    public bool HasProject => Project is not null;

    public ObservableCollection<TreeNode> SolutionItems { get; } = [];

    public ObservableCollection<DocumentViewModel> Documents { get; } = [];

    public DocumentViewModel? ActiveDocument {
        get;
        set {
            if (SetProperty(ref field, value)) {
                OnPropertyChanged(nameof(ActiveChapter));
                value?.OnActivated();
                if (CurrentDocumentOnly) {
                    RefreshErrorList();
                }
                if (value is not ChapterDocumentViewModel) {
                    CaretLine = 0;
                    CaretColumn = 0;
                }
            }
        }
    }

    public ChapterDocumentViewModel? ActiveChapter => ActiveDocument as ChapterDocumentViewModel;

    public bool IsAnalyzing { get; private set => SetProperty(ref field, value); }

    public bool AutoCompleteNames { get; set => SetProperty(ref field, value); } = true;

    public bool HighlightStoryElements {
        get;
        set {
            if (SetProperty(ref field, value)) {
                foreach (ChapterDocumentViewModel document in Documents.OfType<ChapterDocumentViewModel>()) {
                    document.Editor?.RefreshHighlighting();
                }
            }
        }
    } = true;

    // ---- Error List ----

    public IReadOnlyList<ErrorItem> ErrorItems { get; private set => SetProperty(ref field, value); } = [];

    public bool ShowErrors { get; set { if (SetProperty(ref field, value)) { RefreshErrorList(); } } } = true;

    public bool ShowWarnings { get; set { if (SetProperty(ref field, value)) { RefreshErrorList(); } } } = true;

    public bool ShowMessages { get; set { if (SetProperty(ref field, value)) { RefreshErrorList(); } } } = true;

    public bool CurrentDocumentOnly { get; set { if (SetProperty(ref field, value)) { RefreshErrorList(); } } }

    public int ErrorCount { get; private set => SetProperty(ref field, value); }

    public int WarningCount { get; private set => SetProperty(ref field, value); }

    public int MessageCount { get; private set => SetProperty(ref field, value); }

    // ---- Find Results / Output / Status ----

    public ObservableCollection<ReferenceItem> References { get; } = [];

    public string ReferencesHeader { get; private set => SetProperty(ref field, value); } = "Use Find All References (Shift+F12) on a name in the manuscript.";

    public string OutputText => _output.ToString();

    public int SelectedBottomTab { get; set => SetProperty(ref field, value); }

    public string StatusText { get; set => SetProperty(ref field, value); } = "Ready";

    public int CaretLine { get; set => SetProperty(ref field, value); }

    public int CaretColumn { get; set => SetProperty(ref field, value); }

    // ---- Project overview ----

    public int TotalWords { get; private set => SetProperty(ref field, value); }

    public double ProgressPercent { get; private set => SetProperty(ref field, value); }

    public string ProgressText { get; private set => SetProperty(ref field, value); } = "";

    public string ElementSummary { get; private set => SetProperty(ref field, value); } = "";

    public string WindowTitle => Project is null ? "WritingIDE" : $"{Project.Name}{(IsProjectDirty ? " *" : "")} - WritingIDE";

    /// <summary>The project badge in the title bar.</summary>
    public string ProjectTitle => Project is null ? "" : $"{Project.Name}{(IsProjectDirty ? " *" : "")}";

    /// <summary>Solution Explorer search text.</summary>
    public string SolutionFilter {
        get;
        set {
            if (SetProperty(ref field, value)) {
                ApplySolutionFilter();
            }
        }
    } = "";

    public bool NoFilterMatches { get; private set => SetProperty(ref field, value); }

    public bool IsProjectDirty => (Project?.IsDirty ?? false) || Documents.OfType<ChapterDocumentViewModel>().Any(static document => document.IsDirty);

    /// <summary>Snapshot of names, dictionary and disabled rules, shared by every analysis until something changes.</summary>
    public GrammarOptions GrammarOptions {
        get {
            if (_grammarOptions is null) {
                if (Project is null) {
                    return GrammarOptions.Default;
                }
                _grammarOptions = new GrammarOptions(
                    new EntityIndex(Project.Entities.ToList()),
                    new HashSet<string>(Project.CustomDictionary, StringComparer.OrdinalIgnoreCase),
                    new HashSet<string>(Project.DisabledRules, StringComparer.OrdinalIgnoreCase));
            }
            return _grammarOptions;
        }
    }

    public ICommand NewProjectCommand { get; }

    public ICommand OpenProjectCommand { get; }

    public ICommand SaveAllCommand { get; }

    public ICommand CloseProjectCommand { get; }

    public ICommand ExportCommand { get; }

    public ICommand ExitCommand { get; }

    public ICommand AddChapterCommand { get; }

    public ICommand AddEntityCommand { get; }

    public ICommand AddNewItemCommand { get; }

    public ICommand AnalyzeAllCommand { get; }

    public ICommand CloseDocumentCommand { get; }

    public ICommand NavigateToErrorCommand { get; }

    public ICommand NavigateToReferenceCommand { get; }

    public ICommand ShowStartPageCommand { get; }

    public ICommand OptionsCommand { get; }

    public ICommand AboutCommand { get; }

    public ICommand ClearOutputCommand { get; }

    public ICommand ImportCommand { get; }

    // =====================================================================
    // Project lifecycle
    // =====================================================================

    public void NewProject() {
        NewProjectDialog dialog = new() { Owner = Application.Current.MainWindow };
        if (dialog.ShowDialog() != true || !ConfirmCloseProject()) {
            return;
        }
        try {
            NovelProject project = ProjectSerializer.Create(dialog.ProjectLocation, dialog.ProjectName, dialog.Author, dialog.SelectedTemplate);
            Log($"Created project '{project.Name}' from template '{dialog.SelectedTemplate.Title}' at {project.FilePath}");
            LoadProject(project);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) {
            ShowError($"Could not create the project.\n\n{exception.Message}");
        }
    }

    public void OpenProject(string? path = null) {
        if (path is null) {
            OpenFileDialog dialog = new() {
                Title = "Open Project",
                Filter = $"Novel projects (*{ProjectSerializer.Extension})|*{ProjectSerializer.Extension}|All files (*.*)|*.*"
            };
            if (dialog.ShowDialog(Application.Current.MainWindow) != true) {
                return;
            }
            path = dialog.FileName;
        }
        if (!ConfirmCloseProject()) {
            return;
        }
        try {
            LoadProject(LoadWithRecovery(path));
        }
        catch (Exception exception) {
            ShowError($"Could not open '{path}'.\n\n{exception.Message}");
        }
    }

    public bool CloseProject() {
        if (!ConfirmCloseProject()) {
            return false;
        }
        foreach (DocumentViewModel document in Documents.ToList()) {
            if (document is not StartPageViewModel) {
                RemoveDocument(document);
            }
        }
        Project = null;
        _grammarOptions = null;
        _diagnostics.Clear();
        _wordCounts.Clear();
        _mentionCounts.Clear();
        SolutionItems.Clear();
        References.Clear();
        RefreshErrorList();
        UpdateProjectStatistics();
        RaiseProjectChanged();
        CloseSplitPane();
        BookmarkItems.Clear();
        TaskItems.Clear();
        _tasks.Clear();
        ShowStartPage();
        Extensions.RaiseProjectClosed();
        Git.RequestRefresh();
        return true;
    }

    /// <summary>Asks to save unsaved work. Returns false if the user cancelled.</summary>
    public bool ConfirmCloseProject() {
        if ((Project is null) || !IsProjectDirty) {
            return true;
        }
        MessageBoxResult result = HcMessageBox.Show(Application.Current.MainWindow!, $"Save changes to '{Project.Name}'?", "WritingIDE",
            MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
        if (result == MessageBoxResult.Cancel) {
            return false;
        }
        if (result == MessageBoxResult.Yes) {
            SaveAll();
        }
        if (Project is not null) {
            // Saved, or the user chose not to: either way there is nothing left to recover.
            RecoveryStore.Delete(Project.FilePath);
            Project.IsDirty = false;
        }
        foreach (ChapterDocumentViewModel document in Documents.OfType<ChapterDocumentViewModel>()) {
            document.MarkSaved();
        }
        return true;
    }

    public void SaveAll() {
        SaveAll(automatic: false);
    }

    /// <summary>Saves the project and every chapter. Autosave passes <paramref name="automatic"/> to stay quiet.</summary>
    public void SaveAll(bool automatic) {
        if (Project is null) {
            return;
        }
        foreach (ChapterDocumentViewModel document in Documents.OfType<ChapterDocumentViewModel>()) {
            document.SyncToChapter();
        }
        try {
            ProjectSerializer.Save(Project);
            foreach (ChapterDocumentViewModel document in Documents.OfType<ChapterDocumentViewModel>()) {
                document.MarkSaved();
            }
            RecoveryStore.Delete(Project.FilePath);
            if (automatic) {
                StatusText = $"Autosaved at {DateTime.Now:t}";
            }
            else {
                StatusText = "Item(s) Saved";
                Notify($"Saved '{Project.Name}'.", NotificationKind.Success);
                Log($"Saved '{Project.Name}' ({Project.Chapters.Count} chapters, {Project.Entities.Count} story elements).");
            }
            Git.RequestRefresh();
            Extensions.RaiseProjectSaved();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) {
            ShowError($"Could not save the project.\n\n{exception.Message}");
        }
        RaiseTitleChanged();
    }

    private void LoadProject(NovelProject project) {
        foreach (DocumentViewModel document in Documents.ToList()) {
            RemoveDocument(document);
        }
        _diagnostics.Clear();
        _wordCounts.Clear();
        _mentionCounts.Clear();
        References.Clear();
        _grammarOptions = null;
        Project = project;

        SolutionItems.Clear();
        SolutionItems.Add(new ProjectNode(project));
        SolutionFilter = "";
        ApplySolutionFilter();

        project.PropertyChanged += OnProjectPropertyChanged;
        project.EntitiesChanged += (_, _) => InvalidateGrammarOptions();
        project.CustomDictionary.CollectionChanged += (_, _) => InvalidateGrammarOptions();
        project.DisabledRules.CollectionChanged += (_, _) => InvalidateGrammarOptions();
        project.Chapters.CollectionChanged += (_, _) => {
            UpdateProjectStatistics();
            RefreshErrorList();
        };
        project.Entities.CollectionChanged += (_, _) => UpdateProjectStatistics();

        RecentProjects.Add(project.FilePath);
        Git.RequestRefresh();
        Log($"Opened project '{project.Name}': {project.Chapters.Count} chapters, {project.Entities.Count} story elements.");
        RaiseProjectChanged();
        UpdateProjectStatistics();

        Chapter? first = project.Chapters.FirstOrDefault();
        if (first is not null) {
            OpenChapter(first);
        }
        _ = AnalyzeAllAsync(quiet: true);
        _tasks.Clear();
        RefreshBookmarks();
        Extensions.RaiseProjectOpened();
    }

    private void OnProjectPropertyChanged(object? sender, PropertyChangedEventArgs e) {
        if (e.PropertyName is nameof(NovelProject.IsDirty) or nameof(NovelProject.Name)) {
            RaiseTitleChanged();
        }
        if (e.PropertyName == nameof(NovelProject.TargetWordCount)) {
            UpdateProjectStatistics();
        }
    }

    private void RaiseProjectChanged() {
        OnPropertyChanged(nameof(HasProject));
        RaiseTitleChanged();
        CommandManager.InvalidateRequerySuggested();
    }

    public void OnDocumentDirtyChanged() {
        RaiseTitleChanged();
    }

    // =====================================================================
    // Documents
    // =====================================================================

    public void ShowStartPage() {
        _startPage ??= new StartPageViewModel(this);
        if (!Documents.Contains(_startPage)) {
            Documents.Insert(0, _startPage);
        }
        ActiveDocument = _startPage;
        _startPage.OnActivated();
    }

    public ChapterDocumentViewModel OpenChapter(Chapter chapter) {
        ChapterDocumentViewModel? document = Documents.OfType<ChapterDocumentViewModel>().FirstOrDefault(d => d.Chapter == chapter);
        if (document is null) {
            document = new ChapterDocumentViewModel(this, chapter);
            Documents.Add(document);
            _ = document.AnalyzeNowAsync();
        }
        ActiveDocument = document;
        return document;
    }

    public EntityDocumentViewModel OpenEntity(StoryEntity entity) {
        EntityDocumentViewModel? document = Documents.OfType<EntityDocumentViewModel>().FirstOrDefault(d => d.Entity == entity);
        if (document is null) {
            document = new EntityDocumentViewModel(this, entity);
            Documents.Add(document);
        }
        ActiveDocument = document;
        return document;
    }

    /// <summary>The shared grammar options plus the chapter's place in reading order (for continuity checks).</summary>
    public GrammarOptions GetGrammarOptions(Chapter chapter) {
        return WithPosition(GrammarOptions, chapter);
    }

    private GrammarOptions WithPosition(GrammarOptions options, Chapter chapter) {
        List<Chapter> ordered = OrderedChapters.ToList();
        int index = ordered.IndexOf(chapter);
        return index < 0 ? options : options with { Position = new ManuscriptPosition(index, ordered.Select(static c => c.Title).ToList()), StoryDate = chapter.StoryDate };
    }

    /// <summary>Opens (or re-opens) a word diff in a document tab.</summary>
    public void OpenDiff(string fileTitle, string description, string diff, string comparedWith = "last commit") {
        foreach (DiffDocumentViewModel old in Documents.OfType<DiffDocumentViewModel>().Where(d => d.FileTitle == fileTitle).ToList()) {
            RemoveDocument(old);
        }
        DiffDocumentViewModel document = new(this, fileTitle, description, diff, comparedWith);
        Documents.Add(document);
        ActiveDocument = document;
    }

    /// <summary>Opens a commit from Git History, or switches to it when it is already open.</summary>
    public void OpenCommit(CommitDetails commit, string repositoryRoot) {
        CommitDocumentViewModel? document = Documents.OfType<CommitDocumentViewModel>().FirstOrDefault(d => d.Commit.Hash == commit.Hash);
        if (document is null) {
            document = new CommitDocumentViewModel(this, commit, repositoryRoot);
            Documents.Add(document);
        }
        ActiveDocument = document;
    }

    public void CloseDocument(DocumentViewModel? document) {
        if (document is null) {
            return;
        }
        if (document is ChapterDocumentViewModel chapterDocument) {
            chapterDocument.SyncToChapter();
            if (chapterDocument.IsDirty) {
                Project?.MarkDirty();
            }
        }
        int index = Documents.IndexOf(document);
        RemoveDocument(document);
        if ((ActiveDocument == document) || (ActiveDocument is null)) {
            ActiveDocument = Documents.Count == 0 ? null : Documents[Math.Clamp(index, 0, Documents.Count - 1)];
        }
        RaiseTitleChanged();
    }

    private void RemoveDocument(DocumentViewModel document) {
        if (SecondaryDocument == document) {
            CloseSplitPane();
        }
        document.OnClosed();
        Documents.Remove(document);
        if (ActiveDocument == document) {
            ActiveDocument = null;
        }
    }

    public string GetChapterText(Chapter chapter) {
        ChapterDocumentViewModel? document = Documents.OfType<ChapterDocumentViewModel>().FirstOrDefault(d => d.Chapter == chapter);
        return document?.Document.Text ?? chapter.Text;
    }

    public void NavigateTo(Chapter chapter, int offset, int length) {
        OpenChapter(chapter).Select(offset, length);
    }

    // =====================================================================
    // Adding, renaming and deleting items
    // =====================================================================

    public void AddNewItem(EntityKind? kind, bool chapter) {
        if (Project is null) {
            return;
        }
        AddNewItemDialog dialog = new(Project, kind, chapter, GetTargetBook()) { Owner = Application.Current.MainWindow };
        if (dialog.ShowDialog() != true) {
            return;
        }
        if (dialog.IsBook) {
            AddBook(dialog.ItemName);
        }
        else if (dialog.SelectedKind is null) {
            AddChapter(dialog.ItemName, dialog.TargetBook);
        }
        else {
            CreateEntity(dialog.SelectedKind.Value, dialog.ItemName, open: true);
        }
    }

    /// <summary>Adds a chapter after the active chapter (or at the end of its book, in a series).</summary>
    public Chapter AddChapter(string title, Book? book = null) {
        NovelProject project = Project!;
        book ??= GetTargetBook();
        Chapter chapter = new(title) { BookId = book?.Id };
        int insertAt;
        if ((ActiveChapter is not null) && (ActiveChapter.Chapter.BookId == chapter.BookId)) {
            insertAt = project.Chapters.IndexOf(ActiveChapter.Chapter) + 1;
        }
        else {
            Chapter? last = project.GetChapters(book).LastOrDefault();
            insertAt = last is null ? project.Chapters.Count : project.Chapters.IndexOf(last) + 1;
        }
        project.Chapters.Insert(insertAt, chapter);
        Log($"Added chapter '{title}'.");
        OpenChapter(chapter);
        return chapter;
    }

    public StoryEntity CreateEntity(EntityKind kind, string name, bool open) {
        StoryEntity entity = StoryEntity.CreateWithDefaults(kind, name);
        Project!.Entities.Add(entity);
        string kindName = entity.KindInfo.DisplayName.ToLowerInvariant();
        StatusText = $"Created {kindName} '{name}'";
        Notify($"Created {kindName} '{name}'.", NotificationKind.Success);
        Log($"Added {kindName} '{name}'.");
        if (open) {
            OpenEntity(entity);
        }
        return entity;
    }

    public void RenameEntity(StoryEntity entity) {
        if (Project is null) {
            return;
        }
        string oldName = entity.Name;
        int referenceCount = Project.Chapters.Sum(chapter => ManuscriptTools.FindWholeWord(GetChapterText(chapter), oldName).Count);
        InputDialog dialog = new(
            $"Rename {entity.KindInfo.DisplayName}",
            $"New name for '{oldName}':",
            oldName,
            referenceCount > 0 ? $"Update {referenceCount} reference{(referenceCount == 1 ? "" : "s")} in the manuscript" : null) {
            Owner = Application.Current.MainWindow
        };
        if ((dialog.ShowDialog() != true) || string.IsNullOrWhiteSpace(dialog.Value) || (dialog.Value == oldName)) {
            return;
        }
        string newName = dialog.Value.Trim();
        int updated = 0;
        if (dialog.IsChecked) {
            foreach (Chapter chapter in Project.Chapters) {
                updated += ReplaceInChapter(chapter, oldName, newName);
            }
        }
        entity.Name = newName;
        Log($"Renamed '{oldName}' to '{newName}'{(updated > 0 ? $" and updated {updated} reference(s)" : "")}.");
        StatusText = $"Renamed '{oldName}' to '{newName}'";
        Notify($"Renamed '{oldName}' to '{newName}'{(updated > 0 ? $" ({updated} reference(s) updated)" : "")}.", NotificationKind.Success);
    }

    private int ReplaceInChapter(Chapter chapter, string oldName, string newName) {
        ChapterDocumentViewModel? document = Documents.OfType<ChapterDocumentViewModel>().FirstOrDefault(d => d.Chapter == chapter);
        string text = document?.Document.Text ?? chapter.Text;
        IReadOnlyList<(int Offset, int Length)> matches = ManuscriptTools.FindWholeWord(text, oldName);
        if (matches.Count == 0) {
            return 0;
        }
        if (document is not null) {
            document.Document.BeginUpdate();
            try {
                foreach ((int offset, int length) in matches.Reverse()) {
                    document.Document.Replace(offset, length, newName);
                }
            }
            finally {
                document.Document.EndUpdate();
            }
        }
        else {
            chapter.Text = ManuscriptTools.ReplaceWholeWord(text, oldName, newName);
            Project!.MarkDirty();
        }
        return matches.Count;
    }

    public void RenameChapter(Chapter chapter) {
        InputDialog dialog = new("Rename Chapter", "Chapter title:", chapter.Title, null) { Owner = Application.Current.MainWindow };
        if ((dialog.ShowDialog() == true) && !string.IsNullOrWhiteSpace(dialog.Value)) {
            chapter.Title = dialog.Value.Trim();
        }
    }

    public void MoveChapter(Chapter chapter, int delta) {
        if (Project is null) {
            return;
        }
        int index = Project.Chapters.IndexOf(chapter);
        int target = index + delta;
        if ((index >= 0) && (target >= 0) && (target < Project.Chapters.Count)) {
            Project.Chapters.Move(index, target);
        }
    }

    public void DeleteChapter(Chapter chapter) {
        if (Project is null || !Confirm($"'{chapter.Title}' will be removed from the project. Its file is deleted when you save.")) {
            return;
        }
        foreach (ChapterDocumentViewModel document in Documents.OfType<ChapterDocumentViewModel>().Where(d => d.Chapter == chapter).ToList()) {
            CloseDocument(document);
        }
        if (chapter.FileName.Length > 0) {
            Project.RemovedChapterFiles.Add(chapter.FileName);
        }
        Project.Chapters.Remove(chapter);
        _diagnostics.Remove(chapter.Id);
        _wordCounts.Remove(chapter.Id);
        _mentionCounts.Remove(chapter.Id);
        RefreshErrorList();
        Log($"Removed chapter '{chapter.Title}'.");
    }

    public void DeleteEntity(StoryEntity entity) {
        if (Project is null || !Confirm($"Delete {entity.KindInfo.DisplayName.ToLowerInvariant()} '{entity.Name}'? Mentions in the manuscript are not changed.")) {
            return;
        }
        foreach (EntityDocumentViewModel document in Documents.OfType<EntityDocumentViewModel>().Where(d => d.Entity == entity).ToList()) {
            CloseDocument(document);
        }
        Project.Entities.Remove(entity);
        Log($"Deleted {entity.KindInfo.DisplayName.ToLowerInvariant()} '{entity.Name}'.");
    }

    public void AddToDictionary(string word) {
        if ((Project is not null) && !Project.CustomDictionary.Contains(word, StringComparer.OrdinalIgnoreCase)) {
            Project.CustomDictionary.Add(word);
            Log($"Added '{word}' to the project dictionary.");
        }
    }

    public void DisableRule(string ruleId) {
        if ((Project is not null) && !Project.DisabledRules.Contains(ruleId)) {
            Project.DisabledRules.Add(ruleId);
            Log($"Disabled rule {ruleId} ({Grammar.FindRule(ruleId)?.Title}) for this project. Re-enable it in Tools > Options.");
        }
    }

    // =====================================================================
    // Analysis ("Build"), Error List and references
    // =====================================================================

    private void InvalidateGrammarOptions() {
        _grammarOptions = null;
        _optionsRefreshTimer.Stop();
        _optionsRefreshTimer.Start();
    }

    public void ReanalyzeOpenDocuments() {
        foreach (ChapterDocumentViewModel document in Documents.OfType<ChapterDocumentViewModel>()) {
            _ = document.AnalyzeNowAsync();
        }
        if (ActiveDocument is EntityDocumentViewModel entityDocument) {
            entityDocument.RefreshAppearances();
        }
    }

    /// <summary>Analyzes every chapter, like Build Solution, and writes a build log to the Output window.</summary>
    public async Task AnalyzeAllAsync(bool quiet) {
        if ((Project is null) || IsAnalyzing) {
            return;
        }
        IsAnalyzing = true;
        NovelProject project = Project;
        Stopwatch stopwatch = Stopwatch.StartNew();
        if (!quiet) {
            SelectedBottomTab = OutputTab;
            Log($"------ Analysis started: Project: {project.Name} ------");
        }
        StatusText = "Analyzing manuscript...";
        // Background (quiet) analyses skip plugins such as Harper, which are slower; open editors and F6 run them.
        GrammarOptions options = quiet ? GrammarOptions with { RunPlugins = false } : GrammarOptions;
        int errors = 0;
        int warnings = 0;
        int messages = 0;
        try {
            foreach ((Chapter chapter, string text) in OrderedChapters.Select(chapter => (chapter, GetChapterText(chapter))).ToList()) {
                if (quiet && Documents.OfType<ChapterDocumentViewModel>().Any(document => document.Chapter == chapter)) {
                    // Open chapters are analyzed live, with plugins; don't overwrite their results.
                    continue;
                }
                GrammarOptions chapterOptions = WithPosition(options, chapter);
                AnalysisResult result = await Task.Run(() => Grammar.Analyze(text, chapterOptions));
                if (Project != project) {
                    return;
                }
                ReportAnalysis(chapter, text, result);
                int chapterErrors = result.Diagnostics.Count(static d => d.Severity == DiagnosticSeverity.Error);
                int chapterWarnings = result.Diagnostics.Count(static d => d.Severity == DiagnosticSeverity.Warning);
                int chapterMessages = result.Diagnostics.Count(static d => d.Severity == DiagnosticSeverity.Message);
                errors += chapterErrors;
                warnings += chapterWarnings;
                messages += chapterMessages;
                if (!quiet) {
                    Log($"  {project.GetDisplayName(chapter)}: {result.Statistics.Words} words, {chapterErrors} error(s), {chapterWarnings} warning(s), {chapterMessages} message(s)");
                }
            }
            if (!_spelling.IsAvailable && !quiet) {
                Log($"warning: spell checking is unavailable ({_spelling.InitializationError}).");
            }
            if (!quiet) {
                Log($"========== Analysis: {project.Chapters.Count} chapter(s), {errors} error(s), {warnings} warning(s), {messages} message(s), {TotalWords:N0} words in {stopwatch.Elapsed.TotalSeconds:0.00}s ==========");
                if (errors + warnings > 0) {
                    SelectedBottomTab = ErrorListTab;
                }
            }
            StatusText = errors > 0 ? $"Analysis found {errors} error(s)" : "Analysis succeeded";
        }
        finally {
            IsAnalyzing = false;
            CommandManager.InvalidateRequerySuggested();
        }
    }

    /// <summary>Receives analysis results for a chapter (from the live editor or a full analysis).</summary>
    public void ReportAnalysis(Chapter chapter, string text, AnalysisResult result) {
        if ((Project is null) || !Project.Chapters.Contains(chapter)) {
            return;
        }
        LineIndex lines = new(text);
        string displayName = Project.GetDisplayName(chapter);
        _diagnostics[chapter.Id] = result.Diagnostics.Select(diagnostic => {
            (int line, int column) = lines.GetLocation(diagnostic.Offset);
            return new ErrorItem(diagnostic, chapter, line, column) { Document = displayName };
        }).ToList();
        _wordCounts[chapter.Id] = result.Statistics.Words;
        _mentionCounts[chapter.Id] = result.Mentions.GroupBy(static mention => mention.Entity.Id).ToDictionary(static group => group.Key, static group => group.Count());
        _tasks[chapter.Id] = TaskScanner.Scan(chapter, text);
        RefreshTaskList();
        RefreshErrorList();
        UpdateProjectStatistics();
        foreach (ChapterDocumentViewModel document in Documents.OfType<ChapterDocumentViewModel>()) {
            if (document.Chapter != chapter) {
                foreach (Editor.ManuscriptEditor editor in document.AllEditors) {
                    editor.RefreshLenses();
                }
            }
        }
    }

    /// <summary>The name lens for a story element seen from <paramref name="chapter"/>: totals, and the last chapter before it that mentions it.</summary>
    public NameLensInfo GetNameLens(StoryEntity entity, Chapter chapter) {
        int references = 0;
        int chapters = 0;
        Chapter? lastSeen = null;
        int lastSeenIndex = -1;
        int currentIndex = -1;
        int index = 0;
        foreach (Chapter candidate in OrderedChapters) {
            if (candidate == chapter) {
                currentIndex = index;
            }
            if (_mentionCounts.TryGetValue(candidate.Id, out Dictionary<Guid, int>? counts) && counts.TryGetValue(entity.Id, out int count) && (count > 0)) {
                references += count;
                chapters++;
                if (currentIndex < 0) {
                    lastSeen = candidate;
                    lastSeenIndex = index;
                }
            }
            index++;
        }
        return new NameLensInfo(entity, references, chapters, lastSeen, lastSeen is null ? 0 : currentIndex - lastSeenIndex);
    }

    /// <summary>Chapters in reading order: book by book in a series.</summary>
    public IEnumerable<Chapter> OrderedChapters {
        get {
            if (Project is null) {
                return [];
            }
            if (!Project.IsSeries) {
                return Project.Chapters;
            }
            NovelProject project = Project;
            return project.Books.SelectMany(project.GetChapters)
                .Concat(project.Chapters.Where(chapter => project.FindBook(chapter.BookId) is null))
                .ToList();
        }
    }

    public void RefreshErrorList() {
        IEnumerable<ErrorItem> scoped;
        if (Project is null) {
            scoped = [];
        }
        else if (CurrentDocumentOnly) {
            scoped = ActiveChapter is not null && _diagnostics.TryGetValue(ActiveChapter.Chapter.Id, out List<ErrorItem>? items) ? items : [];
        }
        else {
            scoped = OrderedChapters.SelectMany(chapter => _diagnostics.TryGetValue(chapter.Id, out List<ErrorItem>? items) ? items : []);
        }
        List<ErrorItem> all = scoped.ToList();
        ErrorCount = all.Count(static item => item.Severity == DiagnosticSeverity.Error);
        WarningCount = all.Count(static item => item.Severity == DiagnosticSeverity.Warning);
        MessageCount = all.Count(static item => item.Severity == DiagnosticSeverity.Message);
        ErrorItems = all.Where(item => item.Severity switch {
            DiagnosticSeverity.Error => ShowErrors,
            DiagnosticSeverity.Warning => ShowWarnings,
            _ => ShowMessages
        }).ToList();
    }

    // =====================================================================
    // Find and Replace in Files
    // =====================================================================

    public enum SearchScope {
        EntireProject,
        CurrentBook,
        CurrentChapter
    }

    /// <summary>Chapters a search covers, in reading order.</summary>
    public IReadOnlyList<Chapter> GetChaptersInScope(SearchScope scope) {
        if (Project is null) {
            return [];
        }
        Chapter? current = ActiveChapter?.Chapter;
        return scope switch {
            SearchScope.CurrentChapter => current is null ? [] : [current],
            SearchScope.CurrentBook when Project.IsSeries && (current is not null) => OrderedChapters.Where(chapter => chapter.BookId == current.BookId).ToList(),
            _ => OrderedChapters.ToList()
        };
    }

    /// <summary>Find All: every match in scope goes to the Find Results window. Returns the number of matches.</summary>
    public int FindInFiles(Regex regex, string pattern, SearchScope scope) {
        References.Clear();
        int chapters = 0;
        foreach (Chapter chapter in GetChaptersInScope(scope)) {
            string text = GetChapterText(chapter);
            IReadOnlyList<SearchMatch> matches = ManuscriptSearch.Find(text, regex);
            if (matches.Count == 0) {
                continue;
            }
            chapters++;
            LineIndex lines = new(text);
            string displayName = Project!.GetDisplayName(chapter);
            foreach (SearchMatch match in matches) {
                (int line, int column) = lines.GetLocation(match.Offset);
                References.Add(new ReferenceItem(chapter, match.Offset, match.Length, line, column, ManuscriptSearch.CreatePreview(text, match.Offset, match.Length)) { Document = displayName });
            }
        }
        ReferencesHeader = References.Count == 0
            ? $"Find '{pattern}': no matches."
            : $"Find '{pattern}': {References.Count} match(es) in {chapters} chapter(s). Double-click a row to go there.";
        SelectedBottomTab = FindResultsTab;
        StatusText = ReferencesHeader;
        return References.Count;
    }

    /// <summary>Plans Replace All without changing anything: (chapter, replacements) for every chapter with matches.</summary>
    public IReadOnlyList<(Chapter Chapter, IReadOnlyList<SearchMatch> Plan)> PlanReplaceInFiles(Regex regex, string replacement, bool useRegex, SearchScope scope) {
        List<(Chapter, IReadOnlyList<SearchMatch>)> plans = [];
        foreach (Chapter chapter in GetChaptersInScope(scope)) {
            IReadOnlyList<SearchMatch> plan = ManuscriptSearch.PlanReplacements(GetChapterText(chapter), regex, replacement, useRegex);
            if (plan.Count > 0) {
                plans.Add((chapter, plan));
            }
        }
        return plans;
    }

    /// <summary>
    /// Replace All. Open chapters are edited through their editor in one undo step (Ctrl+Z reverts it there);
    /// closed chapters are updated in memory and saved with the project.
    /// </summary>
    public int ApplyReplaceInFiles(IReadOnlyList<(Chapter Chapter, IReadOnlyList<SearchMatch> Plan)> plans, string pattern, string replacement) {
        int total = 0;
        foreach ((Chapter chapter, IReadOnlyList<SearchMatch> plan) in plans) {
            ChapterDocumentViewModel? document = Documents.OfType<ChapterDocumentViewModel>().FirstOrDefault(d => d.Chapter == chapter);
            if (document is not null) {
                document.Document.BeginUpdate();
                try {
                    foreach (SearchMatch match in plan.OrderByDescending(static match => match.Offset)) {
                        document.Document.Replace(match.Offset, match.Length, match.Replacement);
                    }
                }
                finally {
                    document.Document.EndUpdate();
                }
            }
            else {
                chapter.Text = ManuscriptSearch.Apply(chapter.Text, plan);
                Project!.MarkDirty();
            }
            total += plan.Count;
        }
        References.Clear();
        ReferencesHeader = $"Replaced '{pattern}' with '{replacement}': {total} occurrence(s) in {plans.Count} chapter(s).";
        Log(ReferencesHeader);
        Notify(ReferencesHeader, NotificationKind.Success);
        StatusText = ReferencesHeader;
        RaiseTitleChanged();
        ReanalyzeOpenDocuments();
        return total;
    }

    public void ShowFindAndReplace(bool replace) {
        if (Project is null) {
            return;
        }
        string? selection = ActiveChapter?.Editor?.SelectedText;
        if ((selection is not null) && (selection.Contains('\n') || (selection.Length > 100))) {
            selection = null;
        }
        if ((_findDialog is null) || !_findDialog.IsLoaded) {
            _findDialog = new FindReplaceDialog(this) { Owner = Application.Current.MainWindow };
            _findDialog.Show();
        }
        _findDialog.Prepare(selection, replace);
    }

    public void FindReferences(StoryEntity entity) {
        if (Project is null) {
            return;
        }
        References.Clear();
        int chapters = 0;
        foreach (Chapter chapter in OrderedChapters) {
            string text = GetChapterText(chapter);
            IReadOnlyList<EntityMention> mentions = EntityIndex.FindMentions(text, entity);
            if (mentions.Count == 0) {
                continue;
            }
            chapters++;
            LineIndex lines = new(text);
            string displayName = Project.GetDisplayName(chapter);
            foreach (EntityMention mention in mentions) {
                (int line, int column) = lines.GetLocation(mention.Offset);
                References.Add(new ReferenceItem(chapter, mention.Offset, mention.Length, line, column, CreatePreview(text, mention.Offset, mention.Length)) { Document = displayName });
            }
        }
        ReferencesHeader = $"'{entity.Name}' ({entity.KindInfo.DisplayName}): {References.Count} reference(s) in {chapters} chapter(s)";
        SelectedBottomTab = FindResultsTab;
        StatusText = ReferencesHeader;
    }

    private static string CreatePreview(string text, int offset, int length) {
        int start = Math.Max(0, offset - 50);
        int end = Math.Min(text.Length, offset + length + 70);
        string preview = text[start..end].Replace('\r', ' ').Replace('\n', ' ').Trim();
        return (start > 0 ? "..." : "") + preview + (end < text.Length ? "..." : "");
    }

    private void UpdateProjectStatistics() {
        if (Project is null) {
            TotalWords = 0;
            ProgressPercent = 0;
            ProgressText = "";
            ElementSummary = "";
            return;
        }
        TotalWords = Project.Chapters.Sum(chapter => _wordCounts.TryGetValue(chapter.Id, out int words) ? words : TextStatistics.CountWords(chapter.Text));
        int target = Math.Max(1, Project.TargetWordCount);
        ProgressPercent = Math.Min(100, (100.0 * TotalWords) / target);
        ProgressText = $"{TotalWords:N0} of {Project.TargetWordCount:N0} words ({ProgressPercent:0.#}%)";
        ElementSummary = string.Join("   ", EntityKindInfo.All.Select(info => $"{info.PluralName}: {Project.GetEntities(info.Kind).Count()}"));
    }

    // =====================================================================
    // Misc
    // =====================================================================

    private void ShowOptions() {
        if (Project is null) {
            return;
        }
        OptionsDialog dialog = new(this, Project) { Owner = Application.Current.MainWindow };
        dialog.ShowDialog();
        InvalidateGrammarOptions();
        UpdateProjectStatistics();
    }

    private void ShowAbout() {
        // A HandyControls Dialog: a modal overlay inside the main window, closed by hc:ControlCommands.Close.
        Dialog.Show(new AboutView(Harper.IsRunning ? Harper.Status : null));
    }

    // =====================================================================
    // Import
    // =====================================================================

    private void Import() {
        if (Project is null) {
            return;
        }
        ImportDialog dialog = new(Project) { Owner = Application.Current.MainWindow };
        if ((dialog.ShowDialog() == true) && (dialog.Document is not null)) {
            ApplyImport(dialog.Document, dialog.Options);
        }
    }

    public ImportSummary ApplyImport(ImportDocument document, ImportOptions options) {
        NovelProject project = Project!;
        // A brand-new project's single empty "Chapter 1" is replaced by the imported chapters.
        Chapter? placeholder = (project.Chapters.Count == 1)
            && string.IsNullOrWhiteSpace(GetChapterText(project.Chapters[0]))
            && (document.Chapters.Count > 0)
            && !document.Chapters.Any(chapter => chapter.Title.Equals(project.Chapters[0].Title, StringComparison.OrdinalIgnoreCase))
                ? project.Chapters[0]
                : null;

        Book? placeholderBook = project.FindBook(placeholder?.BookId);
        ImportSummary summary = ImportApplier.Apply(project, document, options, SetChapterText);

        if ((placeholder is not null) && (summary.ChaptersAdded > 0) && !summary.Chapters.Contains(placeholder)) {
            foreach (ChapterDocumentViewModel open in Documents.OfType<ChapterDocumentViewModel>().Where(d => d.Chapter == placeholder).ToList()) {
                RemoveDocument(open);
            }
            if (placeholder.FileName.Length > 0) {
                project.RemovedChapterFiles.Add(placeholder.FileName);
            }
            project.Chapters.Remove(placeholder);
            _diagnostics.Remove(placeholder.Id);
            Log($"Removed the empty placeholder chapter '{placeholder.Title}'.");
            // A new series' empty "Book 1" is dropped too when the import brought its own books.
            if ((placeholderBook is not null) && (project.Books.Count > 1) && !project.GetChapters(placeholderBook).Any()) {
                project.Books.Remove(placeholderBook);
                Log($"Removed the empty placeholder book '{placeholderBook.Title}'.");
            }
        }
        if (document.IsPartial) {
            Log("This was part of a longer reply: reply \"continue\" in the AI chat and import the next part the same way.");
        }
        project.MarkDirty();

        Log("------ Import ------");
        foreach (ImportWarning warning in document.Warnings) {
            Log($"  warning (line {warning.Line}): {warning.Message}");
        }
        Log($"========== Import: {summary} ==========");
        StatusText = $"Import: {summary}";
        Notify($"Import complete: {summary}.", summary.Total > 0 ? NotificationKind.Success : NotificationKind.Warning);

        if (summary.Chapters.Count > 0) {
            OpenChapter(summary.Chapters[0]);
        }
        UpdateProjectStatistics();
        _ = AnalyzeAllAsync(quiet: true);
        return summary;
    }

    /// <summary>Replaces a chapter's text, going through its editor when it is open so undo works.</summary>
    private void SetChapterText(Chapter chapter, string text) {
        ChapterDocumentViewModel? document = Documents.OfType<ChapterDocumentViewModel>().FirstOrDefault(d => d.Chapter == chapter);
        if (document is null) {
            chapter.Text = text;
        }
        else {
            document.Document.Text = text;
        }
    }

    // =====================================================================
    // Shell helpers
    // =====================================================================

    public enum NotificationKind {
        Info,
        Success,
        Warning,
        Error
    }

    /// <summary>Shows a HandyControls Growl toast in the top-right corner of the document well.</summary>
    public static void Notify(string message, NotificationKind kind = NotificationKind.Info) {
        GrowlInfo info = new() {
            Message = message,
            ShowDateTime = false,
            WaitTime = kind == NotificationKind.Error ? 8 : 4
        };
        switch (kind) {
            case NotificationKind.Success:
                Growl.Success(info);
                break;
            case NotificationKind.Warning:
                Growl.Warning(info);
                break;
            case NotificationKind.Error:
                Growl.Error(info);
                break;
            default:
                Growl.Info(info);
                break;
        }
    }

    private void RaiseTitleChanged() {
        OnPropertyChanged(nameof(WindowTitle));
        OnPropertyChanged(nameof(ProjectTitle));
    }

    private void ApplySolutionFilter() {
        ProjectNode? root = SolutionItems.OfType<ProjectNode>().FirstOrDefault();
        int matches = root?.ApplyFilter(SolutionFilter) ?? 0;
        NoFilterMatches = (root is not null) && (SolutionFilter.Trim().Length > 0) && (matches == 0);
    }

    public void Log(string message) {
        _output.AppendLine(message);
        OnPropertyChanged(nameof(OutputText));
    }

    private static EntityKind? ParseKind(object? parameter) {
        return parameter switch {
            EntityKind kind => kind,
            string text when Enum.TryParse(text, out EntityKind parsed) => parsed,
            _ => null
        };
    }

    private static bool Confirm(string message) {
        return HcMessageBox.Show(Application.Current.MainWindow!, message, "WritingIDE", MessageBoxButton.OKCancel, MessageBoxImage.Warning) == MessageBoxResult.OK;
    }

    private static void ShowError(string message) {
        HcMessageBox.Show(Application.Current.MainWindow!, message, "WritingIDE", MessageBoxButton.OK, MessageBoxImage.Error);
    }
}