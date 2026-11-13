using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;

using ICSharpCode.AvalonEdit.Document;

using WritingIDE.Core.Analysis;
using WritingIDE.Core.Model;
using WritingIDE.Core.Persistence;
using WritingIDE.Editor;
using WritingIDE.Infrastructure;

using HcMessageBox = HandyControl.Controls.MessageBox;

namespace WritingIDE.ViewModels;

/// <summary>An entry in the Outline window: a scene, bookmark, task, comment or plot-thread passage.</summary>
public sealed record OutlineItem(string Glyph, string KindLabel, string Text, int Offset, int Length, int Line, string Details) {
    public bool IsScene => KindLabel == "Scene";
}

/// <summary>Revision mode, bookmarks, scenes, plot-thread markers, the outline and split-pane editors for a chapter.</summary>
public sealed partial class ChapterDocumentViewModel {
    private readonly List<ManuscriptEditor> _secondaryEditors = [];
    private string? _revisionBaseline;

    /// <summary>The editor in the tab, then any copies in the split pane.</summary>
    public IEnumerable<ManuscriptEditor> AllEditors => _editor is null ? _secondaryEditors : _secondaryEditors.Prepend(_editor);

    public IReadOnlyList<RevisionChange> RevisionChanges { get; private set => SetProperty(ref field, value); } = [];

    public bool IsTrackingRevisions => Chapter.TrackRevisions && (_revisionBaseline is not null);

    public string RevisionSummary { get; private set => SetProperty(ref field, value); } = "";

    public ObservableCollection<OutlineItem> Outline { get; } = [];

    public ICommand StartRevisionsCommand { get; private set; } = null!;

    public ICommand StopRevisionsCommand { get; private set; } = null!;

    public ICommand AcceptAllCommand { get; private set; } = null!;

    public ICommand RejectAllCommand { get; private set; } = null!;

    public ICommand NextChangeCommand { get; private set; } = null!;

    public ICommand TrackSinceSnapshotCommand { get; private set; } = null!;

    private void InitializeWritingTools() {
        StartRevisionsCommand = new RelayCommand(() => StartRevisions(Document.Text, "now"), () => !IsTrackingRevisions);
        StopRevisionsCommand = new RelayCommand(StopRevisions, () => IsTrackingRevisions);
        AcceptAllCommand = new RelayCommand(AcceptAll, () => RevisionChanges.Count > 0);
        RejectAllCommand = new RelayCommand(RejectAll, () => RevisionChanges.Count > 0);
        NextChangeCommand = new RelayCommand(GoToNextChange, () => RevisionChanges.Count > 0);
        TrackSinceSnapshotCommand = new RelayCommand(parameter => {
            if (parameter is ChapterSnapshot snapshot) {
                StartRevisions(SnapshotStore.LoadText(snapshot), $"the snapshot '{snapshot.DisplayName}'");
            }
        });
        if (Chapter.TrackRevisions && (Shell.Project is not null)) {
            _revisionBaseline = RevisionStore.Load(Shell.Project.DirectoryPath, Chapter);
            UpdateRevisions();
        }
    }

    /// <summary>A second editor on the same text, for the split pane.</summary>
    public override FrameworkElement? CreateSecondaryView() {
        _ = View;
        ManuscriptEditor editor = new(this);
        if (LastResult is not null) {
            editor.ApplyAnalysis(LastResult);
        }
        _secondaryEditors.Add(editor);
        return editor;
    }

    public override void OnSecondaryViewClosed(FrameworkElement view) {
        if (view is ManuscriptEditor editor) {
            editor.Detach();
            _secondaryEditors.Remove(editor);
        }
    }

    private void DetachSecondaryEditors() {
        foreach (ManuscriptEditor editor in _secondaryEditors) {
            editor.Detach();
        }
        _secondaryEditors.Clear();
    }

    /// <summary>Called after each analysis: refreshes revision marks, the outline and the task list.</summary>
    private void UpdateAfterAnalysis(string text) {
        UpdateRevisions();
        RefreshOutline();
    }

    // ------------------------------------------------------------------
    // Revision mode
    // ------------------------------------------------------------------

    /// <summary>Starts tracking changes against <paramref name="baseline"/> (the current text, or a snapshot).</summary>
    public void StartRevisions(string baseline, string since) {
        if (Shell.Project is null) {
            return;
        }
        try {
            RevisionStore.Save(Shell.Project.DirectoryPath, Chapter, baseline);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) {
            MainViewModel.Notify($"Could not start revision mode: {exception.Message}", MainViewModel.NotificationKind.Error);
            return;
        }
        _revisionBaseline = baseline;
        Chapter.TrackRevisions = true;
        UpdateRevisions();
        Shell.Log($"Revision mode on for '{Chapter.Title}': changes since {since} are shown inline.");
        MainViewModel.Notify($"Tracking changes since {since}. Right-click a change to accept or reject it.", MainViewModel.NotificationKind.Success);
    }

    public void StopRevisions() {
        if ((RevisionChanges.Count > 0) && (HcMessageBox.Show(Application.Current.MainWindow!,
                $"Stop tracking changes in '{Chapter.Title}'? The text stays as it is now; the {RevisionChanges.Count} change mark(s) are cleared.",
                "Revision Mode", MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK)) {
            return;
        }
        Chapter.TrackRevisions = false;
        _revisionBaseline = null;
        if (Shell.Project is not null) {
            RevisionStore.Delete(Shell.Project.DirectoryPath, Chapter);
        }
        UpdateRevisions();
    }

    private void UpdateRevisions() {
        RevisionChanges = IsTrackingRevisions ? RevisionTracker.Compare(_revisionBaseline!, Document.Text) : [];
        RevisionSummary = !IsTrackingRevisions ? "" : RevisionChanges.Count switch {
            0 => "Tracking changes: none yet.",
            1 => "Tracking changes: 1 change.",
            int count => $"Tracking changes: {count} changes."
        };
        OnPropertyChanged(nameof(IsTrackingRevisions));
        foreach (ManuscriptEditor editor in AllEditors) {
            editor.ApplyRevisions(RevisionChanges);
        }
        CommandManager.InvalidateRequerySuggested();
    }

    public void AcceptChange(RevisionChange change) {
        SetBaseline(RevisionTracker.Accept(_revisionBaseline!, change));
    }

    /// <summary>Puts the baseline's text back (an ordinary edit, so Ctrl+Z works).</summary>
    public void RejectChange(RevisionChange change) {
        Document.Replace(change.Offset, change.Length, change.Deleted);
        UpdateRevisions();
    }

    public void AcceptAll() {
        SetBaseline(Document.Text);
    }

    public void RejectAll() {
        if (_revisionBaseline is null) {
            return;
        }
        Document.BeginUpdate();
        try {
            foreach (RevisionChange change in RevisionChanges.OrderByDescending(static change => change.Offset)) {
                Document.Replace(change.Offset, change.Length, change.Deleted);
            }
        }
        finally {
            Document.EndUpdate();
        }
        UpdateRevisions();
    }

    private void SetBaseline(string baseline) {
        if (Shell.Project is null) {
            return;
        }
        _revisionBaseline = baseline;
        RevisionStore.Save(Shell.Project.DirectoryPath, Chapter, baseline);
        UpdateRevisions();
    }

    private void GoToNextChange() {
        int caret = _editor?.CaretOffset ?? 0;
        RevisionChange? next = RevisionChanges.FirstOrDefault(change => change.Offset > caret) ?? RevisionChanges.FirstOrDefault();
        if (next is not null) {
            Select(next.Offset, next.Length);
        }
    }

    // ------------------------------------------------------------------
    // Bookmarks
    // ------------------------------------------------------------------

    private IEnumerable<CommentSegment> Bookmarks => CommentSegments.Where(static segment => segment.Comment.Kind == MarkerKind.Bookmark).OrderBy(static segment => segment.StartOffset);

    /// <summary>Ctrl+K, Ctrl+K: adds or removes a bookmark on a line.</summary>
    public void ToggleBookmark(int lineNumber) {
        if ((lineNumber < 1) || (lineNumber > Document.LineCount)) {
            return;
        }
        DocumentLine line = Document.GetLineByNumber(lineNumber);
        CommentSegment? existing = Bookmarks.FirstOrDefault(segment => Document.GetLineByOffset(Math.Min(segment.StartOffset, Document.TextLength)).LineNumber == lineNumber);
        if (existing is not null) {
            Chapter.Comments.Remove(existing.Comment);
            Shell.StatusText = $"Bookmark removed from line {lineNumber}";
        }
        else {
            int length = Math.Min(line.Length, 60);
            Chapter.Comments.Add(new ChapterComment {
                Kind = MarkerKind.Bookmark,
                Offset = line.Offset,
                Length = length,
                AnchorText = Document.GetText(line.Offset, length),
                Text = Document.GetText(line.Offset, length).Trim()
            });
            Shell.StatusText = $"Bookmark set on line {lineNumber}";
        }
        Shell.RefreshBookmarks();
    }

    /// <summary>Ctrl+K, Ctrl+N / Ctrl+K, Ctrl+P: the next or previous bookmark in this chapter, wrapping around.</summary>
    public void GoToBookmark(bool forward) {
        List<CommentSegment> bookmarks = Bookmarks.ToList();
        if (bookmarks.Count == 0) {
            Shell.StatusText = "No bookmarks in this chapter. Ctrl+K, Ctrl+K sets one.";
            return;
        }
        int caret = _editor?.CaretOffset ?? 0;
        int caretLine = Document.GetLineByOffset(caret).LineNumber;
        CommentSegment target = forward
            ? bookmarks.FirstOrDefault(segment => Document.GetLineByOffset(segment.StartOffset).LineNumber > caretLine) ?? bookmarks[0]
            : bookmarks.LastOrDefault(segment => Document.GetLineByOffset(segment.StartOffset).LineNumber < caretLine) ?? bookmarks[^1];
        Select(target.StartOffset, 0);
    }

    public void ClearBookmarks() {
        foreach (ChapterComment bookmark in Chapter.Comments.Where(static comment => comment.Kind == MarkerKind.Bookmark).ToList()) {
            Chapter.Comments.Remove(bookmark);
        }
        Shell.RefreshBookmarks();
        Shell.StatusText = "Bookmarks cleared in this chapter";
    }

    // ------------------------------------------------------------------
    // Scenes and plot threads
    // ------------------------------------------------------------------

    /// <summary>Ctrl+Shift+Enter: a scene break on its own line at the caret.</summary>
    public void InsertSceneBreak() {
        if (_editor is null) {
            return;
        }
        int offset = _editor.CaretOffset;
        string before = Document.GetText(0, offset);
        string after = Document.GetText(offset, Document.TextLength - offset);
        string newline = Document.Text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        string prefix = before.Length == 0 || before.EndsWith(newline + newline, StringComparison.Ordinal) ? "" : (before.EndsWith(newline, StringComparison.Ordinal) ? newline : newline + newline);
        string suffix = after.StartsWith(newline, StringComparison.Ordinal) ? newline : newline + newline;
        string insert = prefix + SceneParser.BreakLine + suffix;
        Document.Insert(offset, insert);
        _editor.CaretOffset = offset + insert.Length;
        _editor.TextArea.Focus();
    }

    /// <summary>Marks the selection (or the sentence at the caret) as a passage of a plot thread.</summary>
    public void MarkPlotThread(PlotThread thread, ThreadRole role, int offset, int length) {
        if (length == 0) {
            (offset, length) = SentenceAt(offset);
        }
        if (length == 0) {
            MainViewModel.Notify("Select the passage to mark first.", MainViewModel.NotificationKind.Warning);
            return;
        }
        ChapterComment marker = new() {
            Kind = MarkerKind.PlotThread,
            ThreadId = thread.Id,
            ThreadRole = role,
            Offset = offset,
            Length = length,
            AnchorText = Document.GetText(offset, length),
            Author = Environment.UserName,
            CreatedUtc = DateTime.UtcNow
        };
        Chapter.Comments.Add(marker);
        if (!Shell.ShowCommentMargin) {
            Shell.ShowCommentMargin = true;
        }
        Shell.StatusText = $"Marked a {role.ToString().ToLowerInvariant()} of '{thread.Name}'";
    }

    private (int Offset, int Length) SentenceAt(int offset) {
        string text = Document.Text;
        if (text.Length == 0) {
            return (0, 0);
        }
        offset = Math.Clamp(offset, 0, text.Length - 1);
        int start = offset;
        while ((start > 0) && (".!?\n".IndexOf(text[start - 1]) < 0)) {
            start--;
        }
        int end = offset;
        while ((end < text.Length) && (".!?\n".IndexOf(text[end]) < 0)) {
            end++;
        }
        if ((end < text.Length) && (text[end] != '\n')) {
            end++;
        }
        while ((start < end) && char.IsWhiteSpace(text[start])) {
            start++;
        }
        return (start, end - start);
    }

    // ------------------------------------------------------------------
    // Outline
    // ------------------------------------------------------------------

    /// <summary>Scenes, bookmarks, tasks, comments and plot-thread passages, in the order they appear.</summary>
    public void RefreshOutline() {
        string text = Document.Text;
        List<OutlineItem> items = [];
        foreach (Scene scene in SceneParser.Split(text)) {
            string sceneText = text[scene.Start..scene.End];
            int words = TextStatistics.CountWords(sceneText);
            string pov = Shell.Project is null ? "" : PovOf(sceneText);
            int anchor = scene.BreakOffset < 0 ? scene.Start : scene.BreakOffset;
            items.Add(new OutlineItem("\uE76F", "Scene", $"{scene.Index + 1}. {SceneParser.Describe(text, scene)}", anchor, 0, LineOf(anchor),
                $"{words:N0} words{(pov.Length > 0 ? $" \u00B7 POV {pov}" : "")}"));
        }
        foreach (CommentSegment segment in CommentSegments) {
            ChapterComment marker = segment.Comment;
            (string glyph, string kind, string label) = marker.Kind switch {
                MarkerKind.Bookmark => ("\uE8A4", "Bookmark", marker.Text.Length > 0 ? marker.Text : "Bookmark"),
                MarkerKind.PlotThread => ("\uE8F1", "Plot thread", $"{marker.ThreadRole}: {Shell.Project?.PlotThreads.FirstOrDefault(thread => thread.Id == marker.ThreadId)?.Name ?? "?"}"),
                _ => ("\uE90A", "Comment", marker.Text.Length > 0 ? marker.Text.Replace('\n', ' ') : "(empty comment)")
            };
            if ((marker.Kind == MarkerKind.Comment) && marker.IsResolved) {
                continue;
            }
            items.Add(new OutlineItem(glyph, kind, label, segment.StartOffset, segment.Length, LineOf(segment.StartOffset), ""));
        }
        foreach (ManuscriptTask task in TaskScanner.Scan(Chapter, text)) {
            items.Add(new OutlineItem("\uE73A", task.Kind, task.Text.Length > 0 ? task.Text : task.Kind, task.Offset, task.Length, LineOf(task.Offset), ""));
        }
        Outline.Clear();
        foreach (OutlineItem item in items.OrderBy(static item => item.Offset).ThenBy(static item => item.IsScene ? 0 : 1)) {
            Outline.Add(item);
        }
    }

    private int LineOf(int offset) {
        return Document.GetLineByOffset(Math.Clamp(offset, 0, Document.TextLength)).LineNumber;
    }

    private string PovOf(string sceneText) {
        EntityIndex characters = new(Shell.Project!.GetEntities(EntityKind.Character));
        Chapter probe = new("") { PointOfView = "" };
        ChapterPointOfView pov = CharacterStatistics.DetectPointOfView(probe, 0, sceneText, characters.FindMentions(sceneText), CharacterStatistics.FindQuotations(sceneText), characters);
        return pov.PointOfView;
    }
}