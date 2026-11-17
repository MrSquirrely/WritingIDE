using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Text;
using System.Windows;
using System.Windows.Input;

using Microsoft.Win32;

using WritingIDE.Core.Analysis;
using WritingIDE.Core.Export;
using WritingIDE.Core.Grammar;
using WritingIDE.Core.Model;
using WritingIDE.Core.Search;
using WritingIDE.Core.Text;
using WritingIDE.Infrastructure;
using WritingIDE.Plugins;
using WritingIDE.Services;
using WritingIDE.Views;

namespace WritingIDE.ViewModels;

/// <summary>Scenes, split and merge, bookmarks, tasks, plot threads, the split pane, export, AI and extensions.</summary>
public sealed partial class MainViewModel {
    private readonly Dictionary<Guid, IReadOnlyList<ManuscriptTask>> _tasks = [];

    public AiService Ai { get; private set; } = null!;

    public ExtensionManager Extensions { get; private set; } = null!;

    public LocalApiServer LocalApi { get; private set; } = null!;

    public ObservableCollection<ReferenceItem> BookmarkItems { get; } = [];

    public ObservableCollection<ReferenceItem> TaskItems { get; } = [];

    public ICommand InsertSceneBreakCommand { get; private set; } = null!;

    public ICommand SplitChapterCommand { get; private set; } = null!;

    public ICommand MergeWithNextCommand { get; private set; } = null!;

    public ICommand OpenPlotThreadsCommand { get; private set; } = null!;

    public ICommand NewPlotThreadCommand { get; private set; } = null!;

    public ICommand OpenInSplitPaneCommand { get; private set; } = null!;

    public ICommand CloseSplitPaneCommand { get; private set; } = null!;

    public ICommand ToggleBookmarkCommand { get; private set; } = null!;

    public ICommand NextBookmarkCommand { get; private set; } = null!;

    public ICommand PreviousBookmarkCommand { get; private set; } = null!;

    public ICommand ClearBookmarksCommand { get; private set; } = null!;

    public ICommand ExtensionsCommand { get; private set; } = null!;

    private void InitializeWritingTools() {
        InsertSceneBreakCommand = new RelayCommand(() => ActiveChapter?.InsertSceneBreak(), () => ActiveChapter is not null);
        SplitChapterCommand = new RelayCommand(() => {
            if (ActiveChapter is { Editor: not null } document) {
                SplitChapter(document, document.Editor.CaretOffset);
            }
        }, () => ActiveChapter is not null);
        MergeWithNextCommand = new RelayCommand(() => {
            if (ActiveChapter is not null) {
                MergeWithNext(ActiveChapter.Chapter);
            }
        }, () => (ActiveChapter is not null) && (NextChapterInBook(ActiveChapter.Chapter) is not null));
        OpenPlotThreadsCommand = new RelayCommand(() => OpenToolDocument(() => new PlotThreadsDocumentViewModel(this)), () => HasProject);
        NewPlotThreadCommand = new RelayCommand(() => CreatePlotThread(), () => HasProject);
        OpenInSplitPaneCommand = new RelayCommand(parameter => OpenInSplitPane(parameter as DocumentViewModel ?? ActiveDocument), _ => ActiveDocument is not null);
        CloseSplitPaneCommand = new RelayCommand(CloseSplitPane, () => SecondaryDocument is not null);
        ToggleBookmarkCommand = new RelayCommand(() => ActiveChapter?.ToggleBookmark(ActiveChapter.Editor?.TextArea.Caret.Line ?? 1), () => ActiveChapter is not null);
        NextBookmarkCommand = new RelayCommand(() => ActiveChapter?.GoToBookmark(forward: true), () => ActiveChapter is not null);
        PreviousBookmarkCommand = new RelayCommand(() => ActiveChapter?.GoToBookmark(forward: false), () => ActiveChapter is not null);
        ClearBookmarksCommand = new RelayCommand(() => ActiveChapter?.ClearBookmarks(), () => ActiveChapter is not null);
        ExtensionsCommand = new RelayCommand(ShowPlugins);

        Extensions = new ExtensionManager(this);
        Extensions.LoadAll();
        ApplyPluginRules();
        LocalApi = new LocalApiServer(this);
        if (Settings.Api.Enabled) {
            LocalApi.Start();
        }
    }

    /// <summary>Every diagnostic in the manuscript, in reading order, ignoring the Error List's filters (for the API).</summary>
    public IReadOnlyList<ErrorItem> ErrorItemsUnfiltered => OrderedChapters.SelectMany(chapter => _diagnostics.TryGetValue(chapter.Id, out List<ErrorItem>? items) ? items : []).ToList();

    /// <summary>Harper's rule (when running) plus every extension's rules.</summary>
    public void ApplyPluginRules() {
        List<GrammarRule> rules = [];
        if (Harper.IsRunning) {
            rules.Add(new HarperRule(Harper));
        }
        rules.AddRange(Extensions.Rules);
        Grammar.SetPluginRules(rules);
    }

    // =====================================================================
    // Split and merge chapters
    // =====================================================================

    /// <summary>Splits a chapter at an offset: the rest becomes a new chapter right after it (in the same book).</summary>
    public void SplitChapter(ChapterDocumentViewModel document, int offset) {
        if (Project is null) {
            return;
        }
        string text = document.Document.Text;
        if ((offset <= 0) || (offset >= text.Length) || string.IsNullOrWhiteSpace(text[offset..])) {
            Notify("Put the caret where the new chapter should start (not at the very start or end).", NotificationKind.Warning);
            return;
        }
        InputDialog dialog = new("Split Chapter", "Title of the new chapter (the text after the caret):", NextTitle(document.Chapter.Title), null) { Owner = Application.Current.MainWindow };
        if ((dialog.ShowDialog() != true) || string.IsNullOrWhiteSpace(dialog.Value)) {
            return;
        }
        document.SyncComments();
        CloseDocument(document);
        Chapter second = ChapterOperations.Split(Project, document.Chapter, text, offset, dialog.Value.Trim());
        Project.MarkDirty();
        Log($"Split '{document.Chapter.Title}': the text from the caret on is now '{second.Title}'.");
        OpenChapter(document.Chapter);
        OpenChapter(second);
        Notify($"Split into '{document.Chapter.Title}' and '{second.Title}'.", NotificationKind.Success);
    }

    /// <summary>Appends the next chapter (in the same book) to this one, with a scene break between.</summary>
    public void MergeWithNext(Chapter chapter) {
        Chapter? next = Project is null ? null : NextChapterInBook(chapter);
        if ((Project is null) || (next is null)) {
            return;
        }
        if (!Confirm($"Merge '{next.Title}' into '{chapter.Title}'? Its text follows a scene break, and its comments come along. '{next.Title}' is removed (its file is deleted when you save).")) {
            return;
        }
        string firstText = GetChapterText(chapter);
        string secondText = GetChapterText(next);
        foreach (ChapterDocumentViewModel open in Documents.OfType<ChapterDocumentViewModel>().Where(d => (d.Chapter == chapter) || (d.Chapter == next)).ToList()) {
            open.SyncComments();
            CloseDocument(open);
        }
        ChapterOperations.Merge(Project, chapter, firstText, next, secondText);
        _diagnostics.Remove(next.Id);
        _wordCounts.Remove(next.Id);
        _mentionCounts.Remove(next.Id);
        _tasks.Remove(next.Id);
        Project.MarkDirty();
        RefreshErrorList();
        Log($"Merged '{next.Title}' into '{chapter.Title}'.");
        OpenChapter(chapter);
        Notify($"Merged '{next.Title}' into '{chapter.Title}'.", NotificationKind.Success);
    }

    public Chapter? NextChapterInBook(Chapter chapter) {
        if (Project is null) {
            return null;
        }
        List<Chapter> ordered = OrderedChapters.ToList();
        int index = ordered.IndexOf(chapter);
        return (index >= 0) && (index + 1 < ordered.Count) && (ordered[index + 1].BookId == chapter.BookId) ? ordered[index + 1] : null;
    }

    private static string NextTitle(string title) {
        System.Text.RegularExpressions.Match number = System.Text.RegularExpressions.Regex.Match(title, @"\d+");
        return number.Success ? title[..number.Index] + (int.Parse(number.Value, System.Globalization.CultureInfo.InvariantCulture) + 1) + title[(number.Index + number.Length)..] : title + " (continued)";
    }

    // =====================================================================
    // Bookmarks and Task List
    // =====================================================================

    /// <summary>Rebuilds the Bookmarks window from every chapter.</summary>
    public void RefreshBookmarks() {
        BookmarkItems.Clear();
        if (Project is null) {
            return;
        }
        foreach (Chapter chapter in OrderedChapters) {
            ChapterDocumentViewModel? open = Documents.OfType<ChapterDocumentViewModel>().FirstOrDefault(d => d.Chapter == chapter);
            open?.SyncComments();
            string text = GetChapterText(chapter);
            LineIndex lines = new(text);
            foreach (ChapterComment bookmark in chapter.Comments.Where(static comment => comment.Kind == MarkerKind.Bookmark).OrderBy(static comment => comment.Offset)) {
                int offset = Math.Clamp(bookmark.Offset, 0, text.Length);
                (int line, int column) = lines.GetLocation(offset);
                int end = text.IndexOf('\n', offset);
                string preview = text[offset..(end < 0 ? text.Length : end)].Trim();
                BookmarkItems.Add(new ReferenceItem(chapter, offset, 0, line, column, preview.Length > 120 ? preview[..117] + "..." : preview) { Document = Project.GetDisplayName(chapter) });
            }
        }
        foreach (ChapterDocumentViewModel document in Documents.OfType<ChapterDocumentViewModel>()) {
            document.RefreshOutline();
        }
    }

    private void RefreshTaskList() {
        TaskItems.Clear();
        if (Project is null) {
            return;
        }
        foreach (Chapter chapter in OrderedChapters) {
            if (!_tasks.TryGetValue(chapter.Id, out IReadOnlyList<ManuscriptTask>? tasks) || (tasks.Count == 0)) {
                continue;
            }
            LineIndex lines = new(GetChapterText(chapter));
            foreach (ManuscriptTask task in tasks) {
                (int line, int column) = lines.GetLocation(task.Offset);
                TaskItems.Add(new ReferenceItem(chapter, task.Offset, task.Length, line, column, $"{task.Kind}{(task.Text.Length > 0 ? ": " + task.Text : "")}") { Document = Project.GetDisplayName(chapter) });
            }
        }
    }

    // =====================================================================
    // Plot threads
    // =====================================================================

    /// <summary>Asks for a name and adds a plot thread. Returns null if cancelled.</summary>
    public PlotThread? CreatePlotThread() {
        if (Project is null) {
            return null;
        }
        InputDialog dialog = new("New Plot Thread", "What is the thread or promise? (e.g. \"The compass points out to sea\", \"Who killed Tobias?\")", "", null) { Owner = Application.Current.MainWindow };
        if ((dialog.ShowDialog() != true) || string.IsNullOrWhiteSpace(dialog.Value)) {
            return null;
        }
        PlotThread thread = new(dialog.Value.Trim());
        Project.PlotThreads.Add(thread);
        Log($"Added plot thread '{thread.Name}'. Mark its setups and payoffs by right-clicking passages in a chapter.");
        foreach (PlotThreadsDocumentViewModel tracker in Documents.OfType<PlotThreadsDocumentViewModel>()) {
            tracker.Refresh();
        }
        return thread;
    }

    // =====================================================================
    // Split pane (multiple editor panes)
    // =====================================================================

    /// <summary>The document shown in the right-hand split pane, if any.</summary>
    public DocumentViewModel? SecondaryDocument { get; private set => SetProperty(ref field, value); }

    public FrameworkElement? SecondaryView { get; private set => SetProperty(ref field, value); }

    public string SecondaryTitle => SecondaryDocument is null ? "" : SecondaryDocument.Title;

    /// <summary>Opens an extension's tool window as a tab, or brings it forward when it is already open.</summary>
    public void OpenPluginToolWindow(ExtensionInfo extension, Sdk.PluginToolWindow window) {
        if (Project is null) {
            StatusText = $"Open a project to use '{window.Title}'.";
            return;
        }
        PluginToolDocumentViewModel? document = Documents.OfType<PluginToolDocumentViewModel>().FirstOrDefault(document => document.Window == window);
        if (document is null) {
            document = new PluginToolDocumentViewModel(this, extension, window);
            Documents.Add(document);
        }
        ActiveDocument = document;
    }

    /// <summary>Window > Split: shows a document in a second pane beside the document well (a chapter gets a second editor on the same text).</summary>
    public void OpenInSplitPane(DocumentViewModel? document) {
        if (document is null) {
            return;
        }
        FrameworkElement? view = document.CreateSecondaryView();
        if (view is null) {
            Notify($"'{document.Title}' cannot be shown in the split pane. Chapters, story elements and the bible can.", NotificationKind.Warning);
            return;
        }
        CloseSplitPane();
        SecondaryDocument = document;
        SecondaryView = view;
        OnPropertyChanged(nameof(SecondaryTitle));
        if (!Documents.Contains(document)) {
            Documents.Add(document);
        }
    }

    public void CloseSplitPane() {
        if ((SecondaryDocument is not null) && (SecondaryView is not null)) {
            SecondaryDocument.OnSecondaryViewClosed(SecondaryView);
        }
        SecondaryView = null;
        SecondaryDocument = null;
        OnPropertyChanged(nameof(SecondaryTitle));
    }

    /// <summary>Opens a chapter in the split pane (Solution Explorer: Open in Split Pane).</summary>
    public void OpenChapterInSplitPane(Chapter chapter) {
        ChapterDocumentViewModel? document = Documents.OfType<ChapterDocumentViewModel>().FirstOrDefault(d => d.Chapter == chapter);
        if (document is null) {
            DocumentViewModel? previous = ActiveDocument;
            document = OpenChapter(chapter);
            ActiveDocument = previous ?? document;
        }
        OpenInSplitPane(document);
    }

    public void OpenEntityInSplitPane(StoryEntity entity) {
        EntityDocumentViewModel? document = Documents.OfType<EntityDocumentViewModel>().FirstOrDefault(d => d.Entity == entity) ?? new EntityDocumentViewModel(this, entity);
        document.OnActivated();
        OpenInSplitPane(document);
    }

    // =====================================================================
    // Export
    // =====================================================================

    private void ShowExportDialog() {
        if (Project is null) {
            return;
        }
        ExportDialog dialog = new(this, Project) { Owner = Application.Current.MainWindow };
        dialog.ShowDialog();
    }

    /// <summary>Runs an export chosen in the Export dialog. Returns the file written, or null.</summary>
    public string? Export(ExportOptions options, Sdk.IManuscriptExporter? extension) {
        if (Project is null) {
            return null;
        }
        string extensionName = extension?.FileExtension ?? Exporter.Extension(options.Format);
        SaveFileDialog save = new() {
            Title = "Export",
            FileName = (extension is null ? Exporter.DefaultFileName(Project, options.Chapters) : Project.Name) + (options.Format == ExportFormat.AiContext ? " - AI context" : "") + extensionName,
            InitialDirectory = Project.DirectoryPath,
            Filter = extension is null ? Exporter.FileFilter(options.Format) : $"{extension.Name} (*{extension.FileExtension})|*{extension.FileExtension}"
        };
        if (save.ShowDialog(Application.Current.MainWindow) != true) {
            return null;
        }
        foreach (ChapterDocumentViewModel document in Documents.OfType<ChapterDocumentViewModel>()) {
            document.SyncComments();
        }
        IReadOnlyList<string> warnings = [];
        try {
            if (extension is not null) {
                extension.Export(save.FileName, Extensions.ProjectApi);
            }
            else {
                warnings = Exporter.Export(save.FileName, Project, options, GetChapterText);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException) {
            ShowErrorMessage($"Could not export.\n\n{exception.Message}");
            return null;
        }
        Log($"Exported {(extension?.Name ?? options.Format.ToString())} to {save.FileName}");
        foreach (string warning in warnings) {
            Log($"  {warning}");
            Notify(warning, NotificationKind.Warning);
        }
        StatusText = "Export complete";
        if (options.Format == ExportFormat.GoogleDocs) {
            // Google Docs converts an uploaded .docx: show the file and open Google Drive to upload it.
            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{save.FileName}\"") { UseShellExecute = true });
            Process.Start(new ProcessStartInfo("https://drive.google.com/drive/my-drive") { UseShellExecute = true });
            Notify("Exported. In Google Drive, choose New > File upload (or drag the file in), then open it with Google Docs.", NotificationKind.Success);
        }
        else if (options.Format == ExportFormat.AiContext) {
            string text = File.ReadAllText(save.FileName);
            Clipboard.SetText(text);
            Notify($"Exported and copied to the clipboard (about {AiContextExporter.EstimateTokens(text):N0} tokens). Paste it into an AI chat or attach the file.", NotificationKind.Success);
        }
        else {
            Notify($"Exported {Path.GetFileName(save.FileName)}.", NotificationKind.Success);
        }
        return save.FileName;
    }

    private static void ShowErrorMessage(string message) {
        HandyControl.Controls.MessageBox.Show(Application.Current.MainWindow!, message, "WritingIDE", MessageBoxButton.OK, MessageBoxImage.Error);
    }

    /// <summary>The chapters an export or AI tool covers.</summary>
    public IReadOnlyList<Chapter> ChaptersFor(SearchScope scope) {
        return GetChaptersInScope(scope);
    }

    /// <summary>The project's Markdown context for AI tools: story bible, elements, threads and synopses (and full text if asked).</summary>
    public string BuildAiContext(bool fullText) {
        if (Project is null) {
            return "";
        }
        return AiContextExporter.Build(Project, new ExportOptions { Format = ExportFormat.AiContext, Chapters = OrderedChapters.ToList(), IncludeFullText = fullText }, GetChapterText);
    }
}