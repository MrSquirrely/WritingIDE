using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

using ICSharpCode.AvalonEdit.Document;

using WritingIDE.Core;
using WritingIDE.Core.Analysis;
using WritingIDE.Core.Grammar;
using WritingIDE.Core.Model;
using WritingIDE.Core.Persistence;
using WritingIDE.Core.Search;
using WritingIDE.Editor;
using WritingIDE.Infrastructure;
using WritingIDE.Services;
using WritingIDE.Views;

namespace WritingIDE.ViewModels;

/// <summary>A tab in the document well.</summary>
public abstract class DocumentViewModel(MainViewModel shell) : ObservableObject {
    public MainViewModel Shell { get; } = shell;

    public abstract string Title { get; }

    public virtual bool IsDirty => false;

    public abstract string Glyph { get; }

    public abstract Brush GlyphBrush { get; }

    public virtual string ToolTip => Title;

    /// <summary>The view is created once and cached so editor state (caret, scroll, undo) survives tab switches.</summary>
    public FrameworkElement View => field ??= CreateView();

    protected abstract FrameworkElement CreateView();

    public virtual void OnActivated() {
    }

    public virtual void OnClosed() {
    }
}

/// <summary>How often a story element is mentioned in a chapter.</summary>
public sealed class MentionSummary(StoryEntity entity, int count) {
    public StoryEntity Entity { get; } = entity;

    public int Count { get; } = count;

    public string Name => Entity.Name;

    public string Glyph => Entity.KindInfo.Glyph;

    public Brush GlyphBrush => Visuals.GetKindBrush(Entity.Kind);
}

/// <summary>A chapter open in the manuscript editor.</summary>
public sealed class ChapterDocumentViewModel : DocumentViewModel {
    private readonly DispatcherTimer _analysisTimer;
    private CancellationTokenSource? _analysisCancellation;
    private ManuscriptEditor? _editor;

    public ChapterDocumentViewModel(MainViewModel shell, Chapter chapter) : base(shell) {
        Chapter = chapter;
        Document = new TextDocument(chapter.Text);
        Document.UndoStack.ClearAll();
        CommentSegments = new TextSegmentCollection<CommentSegment>(Document);
        foreach (ChapterComment comment in chapter.Comments) {
            // The chapter may have been edited while closed (Replace in Files, Git): find each comment's text again.
            CommentAnchoring.Reanchor(chapter.Text, comment);
            CommentSegments.Add(new CommentSegment(comment) { StartOffset = comment.Offset, Length = comment.Length });
        }
        chapter.Comments.CollectionChanged += OnCommentsChanged;
        TakeSnapshotCommand = new RelayCommand(() => TakeSnapshot(null));
        CompareSnapshotCommand = new RelayCommand(parameter => {
            if (parameter is ChapterSnapshot snapshot) {
                CompareSnapshot(snapshot);
            }
        });
        RestoreSnapshotCommand = new RelayCommand(parameter => {
            if (parameter is ChapterSnapshot snapshot) {
                RestoreSnapshot(snapshot);
            }
        });
        DeleteSnapshotCommand = new RelayCommand(parameter => {
            if (parameter is ChapterSnapshot snapshot) {
                DeleteSnapshot(snapshot);
            }
        });
        AddCommentCommand = new RelayCommand(() => AddComment(_editor?.SelectionStart ?? 0, _editor?.SelectionLength ?? 0));
        Document.UndoStack.PropertyChanged += (_, e) => {
            if (e.PropertyName == nameof(UndoStack.IsOriginalFile)) {
                OnPropertyChanged(nameof(IsDirty));
                OnPropertyChanged(nameof(Title));
                Shell.OnDocumentDirtyChanged();
            }
        };
        Document.TextChanged += (_, _) => ScheduleAnalysis();
        chapter.PropertyChanged += (_, e) => {
            if (e.PropertyName == nameof(Chapter.Title)) {
                OnPropertyChanged(nameof(Title));
            }
        };
        _analysisTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(450) };
        _analysisTimer.Tick += (_, _) => {
            _analysisTimer.Stop();
            _ = AnalyzeNowAsync();
        };
    }

    public Chapter Chapter { get; }

    public TextDocument Document { get; }

    public override string Title => Chapter.Title + (IsDirty ? " *" : "");

    public override bool IsDirty => !Document.UndoStack.IsOriginalFile;

    public override string Glyph => Visuals.ChapterGlyph;

    public override Brush GlyphBrush => Visuals.ChapterBrush;

    public override string ToolTip => Shell.Project?.GetDisplayName(Chapter) ?? Chapter.Title;

    public bool IsInSeries => Shell.Project?.IsSeries ?? false;

    public IEnumerable<Book> Books => Shell.Project?.Books ?? [];

    /// <summary>The book this chapter belongs to; setting it moves the chapter to that book.</summary>
    public Book? Book {
        get => Shell.Project?.FindBook(Chapter.BookId);
        set {
            if ((value is not null) && (Chapter.BookId != value.Id)) {
                Chapter.BookId = value.Id;
                OnPropertyChanged();
                OnPropertyChanged(nameof(ToolTip));
            }
        }
    }

    public ManuscriptEditor? Editor => _editor;

    public AnalysisResult? LastResult { get; private set; }

    public TextStatistics Statistics { get; private set => SetProperty(ref field, value); } = TextStatistics.Empty;

    public IReadOnlyList<MentionSummary> Mentions { get; private set => SetProperty(ref field, value); } = [];

    public IReadOnlyList<ChapterStatus> Statuses { get; } = Enum.GetValues<ChapterStatus>();

    /// <summary>Choices for the Point of View box: every character, plus first person.</summary>
    public IEnumerable<string> PointOfViewChoices => (Shell.Project?.GetEntities(EntityKind.Character).Select(static character => character.Name) ?? [])
        .Order(StringComparer.CurrentCultureIgnoreCase)
        .Prepend(ChapterPointOfView.FirstPerson);

    /// <summary>Live positions of this chapter's comments; AvalonEdit keeps them in place as the text is edited.</summary>
    public TextSegmentCollection<CommentSegment> CommentSegments { get; }

    public int OpenCommentCount => Chapter.Comments.Count(static comment => !comment.IsResolved);

    public ObservableCollection<ChapterSnapshot> Snapshots { get; } = [];

    public ICommand TakeSnapshotCommand { get; }

    public ICommand CompareSnapshotCommand { get; }

    public ICommand RestoreSnapshotCommand { get; }

    public ICommand DeleteSnapshotCommand { get; }

    public ICommand AddCommentCommand { get; }

    private CommentMargin? _margin;

    protected override FrameworkElement CreateView() {
        _editor = new ManuscriptEditor(this);
        if (LastResult is not null) {
            _editor.ApplyAnalysis(LastResult);
        }
        _margin = new CommentMargin(this, _editor) { IsWanted = Shell.ShowCommentMargin && !Shell.IsDistractionFree };
        _editor.SetFocusMode(Shell.IsDistractionFree);
        DockPanel host = new() { LastChildFill = true };
        DockPanel.SetDock(_margin, Dock.Right);
        host.Children.Add(_margin);
        host.Children.Add(_editor);
        return host;
    }

    /// <summary>Applies View > Show Comments, Show Name Lenses and distraction-free mode to an open editor.</summary>
    public void ApplyViewOptions() {
        if (_margin is not null) {
            _margin.IsWanted = Shell.ShowCommentMargin && !Shell.IsDistractionFree;
        }
        _editor?.SetCommentsVisible(Shell.ShowCommentMargin && !Shell.IsDistractionFree);
        _editor?.SetFocusMode(Shell.IsDistractionFree);
        _editor?.RefreshLenses();
    }

    public override void OnActivated() {
        RefreshSnapshots();
        OnPropertyChanged(nameof(PointOfViewChoices));
    }

    // ------------------------------------------------------------------
    // Comments
    // ------------------------------------------------------------------

    /// <summary>Adds a comment on the selection, or on the word at the caret when nothing is selected.</summary>
    public void AddComment(int offset, int length) {
        offset = Math.Clamp(offset, 0, Document.TextLength);
        length = Math.Clamp(length, 0, Document.TextLength - offset);
        if (length == 0) {
            (int start, int wordLength) = Core.Text.Tokenizer.GetWordAt(Document.Text, offset);
            if (wordLength > 0) {
                (offset, length) = (start, wordLength);
            }
        }
        ChapterComment comment = new() {
            Offset = offset,
            Length = length,
            AnchorText = Document.GetText(offset, length),
            Author = Environment.UserName,
            CreatedUtc = DateTime.UtcNow
        };
        Chapter.Comments.Add(comment);
        if (!Shell.ShowCommentMargin) {
            Shell.ShowCommentMargin = true;
        }
        _margin?.FocusComment(comment);
    }

    public void DeleteComment(ChapterComment comment) {
        Chapter.Comments.Remove(comment);
        _editor?.TextArea.Focus();
    }

    private void OnCommentsChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e) {
        if (e.OldItems is not null) {
            foreach (ChapterComment removed in e.OldItems) {
                CommentSegment? segment = CommentSegments.FirstOrDefault(segment => segment.Comment == removed);
                if (segment is not null) {
                    CommentSegments.Remove(segment);
                }
            }
        }
        if (e.NewItems is not null) {
            foreach (ChapterComment added in e.NewItems) {
                if (CommentSegments.All(segment => segment.Comment != added)) {
                    CommentAnchoring.Reanchor(Document.Text, added);
                    CommentSegments.Add(new CommentSegment(added) { StartOffset = added.Offset, Length = added.Length });
                }
            }
        }
        if (e.Action == System.Collections.Specialized.NotifyCollectionChangedAction.Reset) {
            CommentSegments.Clear();
        }
        OnPropertyChanged(nameof(OpenCommentCount));
        _editor?.TextArea.TextView.Redraw();
    }

    /// <summary>Copies the live comment positions (and the text they cover) back to the model, for saving.</summary>
    public void SyncComments() {
        foreach (CommentSegment segment in CommentSegments) {
            ChapterComment comment = segment.Comment;
            comment.Offset = segment.StartOffset;
            comment.Length = segment.Length;
            if (segment.Length > 0) {
                comment.AnchorText = Document.GetText(segment.StartOffset, segment.Length);
                comment.IsOrphaned = false;
            }
            else if (comment.AnchorText.Length > 0) {
                comment.IsOrphaned = true;
            }
        }
    }

    // ------------------------------------------------------------------
    // Snapshots (version history)
    // ------------------------------------------------------------------

    public void RefreshSnapshots() {
        Snapshots.Clear();
        string? directory = Shell.Project?.DirectoryPath;
        if (string.IsNullOrEmpty(directory)) {
            return;
        }
        foreach (ChapterSnapshot snapshot in SnapshotStore.List(directory, Chapter.Id)) {
            Snapshots.Add(snapshot);
        }
    }

    /// <summary>Takes a snapshot of the text in the editor. Asks for a name unless <paramref name="label"/> is given.</summary>
    public ChapterSnapshot? TakeSnapshot(string? label) {
        string? directory = Shell.Project?.DirectoryPath;
        if (string.IsNullOrEmpty(directory)) {
            return null;
        }
        if (label is null) {
            InputDialog dialog = new("Take Snapshot", $"Name this version of '{Chapter.Title}' (optional):", "", null) { Owner = Application.Current.MainWindow };
            if (dialog.ShowDialog() != true) {
                return null;
            }
            label = dialog.Value;
        }
        try {
            ChapterSnapshot? snapshot = SnapshotStore.Take(directory, Chapter, Document.Text, label);
            RefreshSnapshots();
            if (snapshot is null) {
                MainViewModel.Notify("No changes since the last snapshot.");
            }
            else {
                Shell.Log($"Took snapshot '{snapshot.DisplayName}' of '{Chapter.Title}' ({snapshot.Words:N0} words).");
                Shell.StatusText = $"Snapshot taken: {snapshot.DisplayName}";
            }
            return snapshot;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) {
            MainViewModel.Notify($"Could not take the snapshot: {exception.Message}", MainViewModel.NotificationKind.Error);
            return null;
        }
    }

    /// <summary>Opens a word diff: what changed from the snapshot to the text in the editor now.</summary>
    public void CompareSnapshot(ChapterSnapshot snapshot) {
        string diff = WordDiff.Render(SnapshotStore.LoadText(snapshot), Document.Text);
        if (diff.Length == 0) {
            MainViewModel.Notify($"'{Chapter.Title}' is identical to the snapshot '{snapshot.DisplayName}'.");
            return;
        }
        Shell.OpenDiff($"{Chapter.Title} vs {snapshot.DisplayName}", $"changes since the snapshot '{snapshot.DisplayName}'", diff, "snapshot");
    }

    /// <summary>Replaces the chapter with a snapshot, after snapshotting the current text. Undoable with Ctrl+Z.</summary>
    public void RestoreSnapshot(ChapterSnapshot snapshot) {
        MessageBoxResult answer = HandyControl.Controls.MessageBox.Show(Application.Current.MainWindow!,
            $"Replace the text of '{Chapter.Title}' with the snapshot '{snapshot.DisplayName}'?\n\nThe current text is saved as a snapshot first, and Ctrl+Z undoes the restore.",
            "Restore Snapshot", MessageBoxButton.OKCancel, MessageBoxImage.Question);
        if (answer != MessageBoxResult.OK) {
            return;
        }
        string text = SnapshotStore.LoadText(snapshot);
        TakeSnapshot($"Before restoring '{snapshot.DisplayName}'");
        Document.Replace(0, Document.TextLength, text);
        Shell.Log($"Restored '{Chapter.Title}' from the snapshot '{snapshot.DisplayName}'.");
        MainViewModel.Notify($"Restored the snapshot '{snapshot.DisplayName}'. Ctrl+Z undoes it.", MainViewModel.NotificationKind.Success);
    }

    public void DeleteSnapshot(ChapterSnapshot snapshot) {
        MessageBoxResult answer = HandyControl.Controls.MessageBox.Show(Application.Current.MainWindow!,
            $"Delete the snapshot '{snapshot.DisplayName}'? This cannot be undone.", "Delete Snapshot", MessageBoxButton.OKCancel, MessageBoxImage.Warning);
        if (answer == MessageBoxResult.OK) {
            SnapshotStore.Delete(snapshot);
            RefreshSnapshots();
        }
    }

    public void ScheduleAnalysis() {
        _analysisTimer.Stop();
        _analysisTimer.Start();
    }

    public async Task AnalyzeNowAsync() {
        _analysisCancellation?.Cancel();
        CancellationTokenSource cancellation = new();
        _analysisCancellation = cancellation;

        string text = Document.Text;
        ITextSourceVersion version = Document.Version;
        GrammarOptions options = Shell.GetGrammarOptions(Chapter);
        GrammarEngine engine = Shell.Grammar;
        Chapter.Text = text;
        try {
            // TryAnalyze returns null when superseded, so cancellation never surfaces as an exception (or a debugger break).
            AnalysisResult? result = await Task.Run(() => engine.TryAnalyze(text, options, cancellation.Token));
            if ((result is null) || cancellation.IsCancellationRequested || (version.CompareAge(Document.Version) != 0)) {
                return;
            }
            LastResult = result;
            Statistics = result.Statistics;
            Mentions = result.Mentions
                .GroupBy(static mention => mention.Entity)
                .Select(static group => new MentionSummary(group.Key, group.Count()))
                .OrderByDescending(static summary => summary.Count)
                .ToList();
            // Report first: the name lenses read the project-wide counts this updates.
            Shell.ReportAnalysis(Chapter, text, result);
            _editor?.ApplyAnalysis(result);
        }
        catch (OperationCanceledException) {
            // Superseded by a newer analysis.
        }
    }

    public void SyncToChapter() {
        Chapter.Text = Document.Text;
        SyncComments();
    }

    public void MarkSaved() {
        Document.UndoStack.MarkAsOriginalFile();
    }

    /// <summary>Selects a span and scrolls it into view once the editor is loaded.</summary>
    public void Select(int offset, int length) {
        FrameworkElement view = View;
        view.Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () => _editor?.SelectAndReveal(offset, length));
    }

    public override void OnClosed() {
        _analysisTimer.Stop();
        _analysisCancellation?.Cancel();
        SyncComments();
        Chapter.Comments.CollectionChanged -= OnCommentsChanged;
        _margin?.Detach();
        _editor?.Detach();
    }
}

/// <summary>A story element open in its designer.</summary>
public sealed class EntityDocumentViewModel : DocumentViewModel {
    public EntityDocumentViewModel(MainViewModel shell, StoryEntity entity) : base(shell) {
        Entity = entity;
        entity.PropertyChanged += (_, e) => {
            if (e.PropertyName == nameof(StoryEntity.Name)) {
                OnPropertyChanged(nameof(Title));
            }
        };
        FindReferencesCommand = new RelayCommand(() => Shell.FindReferences(Entity));
        RenameCommand = new RelayCommand(() => Shell.RenameEntity(Entity));
        DeleteCommand = new RelayCommand(() => Shell.DeleteEntity(Entity));
        AddAttributeCommand = new RelayCommand(() => Entity.Attributes.Add(new EntityAttribute("New attribute", "")));
        RemoveAttributeCommand = new RelayCommand(parameter => {
            if (parameter is EntityAttribute attribute) {
                Entity.Attributes.Remove(attribute);
            }
        });
        OpenChapterCommand = new RelayCommand(parameter => {
            if (parameter is Appearance appearance) {
                Shell.NavigateTo(appearance.Chapter, appearance.FirstOffset, appearance.FirstLength);
            }
        });
        AddRelationshipCommand = new RelayCommand(() => {
            StoryEntity? target = OtherEntities.FirstOrDefault(static other => other.Kind == EntityKind.Character) ?? OtherEntities.FirstOrDefault();
            if (target is not null) {
                Entity.Relationships.Add(new EntityRelationship(target.Id, ""));
            }
        }, () => OtherEntities.Count > 0);
        RemoveRelationshipCommand = new RelayCommand(parameter => {
            if (parameter is EntityRelationship relationship) {
                Entity.Relationships.Remove(relationship);
            }
        });
        OpenMapCommand = new RelayCommand(() => Shell.OpenRelationshipMapCommand.Execute(null));
    }

    /// <summary>Every other story element, for the relationship target box.</summary>
    public IReadOnlyList<StoryEntity> OtherEntities { get; private set => SetProperty(ref field, value); } = [];

    /// <summary>Relationships other elements define towards this one ("Wren: sister of").</summary>
    public string IncomingRelationships { get; private set => SetProperty(ref field, value); } = "";

    public ICommand AddRelationshipCommand { get; }

    public ICommand RemoveRelationshipCommand { get; }

    public ICommand OpenMapCommand { get; }

    private void RefreshRelationships() {
        NovelProject? project = Shell.Project;
        if (project is null) {
            return;
        }
        OtherEntities = project.Entities.Where(other => other != Entity).OrderBy(static other => other.Kind).ThenBy(static other => other.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
        List<string> incoming = project.Entities
            .Where(other => other != Entity)
            .SelectMany(other => other.Relationships.Where(relationship => relationship.TargetId == Entity.Id).Select(relationship => $"{other.Name}: {(relationship.Label.Length > 0 ? relationship.Label : "related")}"))
            .ToList();
        IncomingRelationships = incoming.Count == 0 ? "" : "Others towards this one: " + string.Join("; ", incoming);
    }

    public StoryEntity Entity { get; }

    public override string Title => Entity.Name;

    public override string Glyph => Entity.KindInfo.Glyph;

    public override Brush GlyphBrush => Visuals.GetKindBrush(Entity.Kind);

    public string KindName => Entity.KindInfo.DisplayName;

    public string KindDescription => Entity.KindInfo.Description;

    public string TriggerHint => $"Type {string.Join(" or ", Entity.KindInfo.TriggerKeywords.Select(static keyword => $"'{keyword}.'"))} in a chapter to insert {Entity.KindInfo.PluralName.ToLowerInvariant()}.";

    public ObservableCollection<Appearance> Appearances { get; } = [];

    public string AppearanceSummary { get; private set => SetProperty(ref field, value); } = "";

    public ICommand FindReferencesCommand { get; }

    public ICommand RenameCommand { get; }

    public ICommand DeleteCommand { get; }

    public ICommand AddAttributeCommand { get; }

    public ICommand RemoveAttributeCommand { get; }

    public ICommand OpenChapterCommand { get; }

    protected override FrameworkElement CreateView() {
        return new EntityEditorView { DataContext = this };
    }

    public override void OnActivated() {
        RefreshAppearances();
        RefreshRelationships();
    }

    public void RefreshAppearances() {
        Appearances.Clear();
        if (Shell.Project is null) {
            return;
        }
        int total = 0;
        foreach (Chapter chapter in Shell.Project.Chapters) {
            IReadOnlyList<EntityMention> mentions = EntityIndex.FindMentions(Shell.GetChapterText(chapter), Entity);
            if (mentions.Count > 0) {
                total += mentions.Count;
                Appearances.Add(new Appearance(chapter, mentions.Count, mentions[0].Offset, mentions[0].Length));
            }
        }
        AppearanceSummary = total == 0
            ? "Not mentioned in the manuscript yet."
            : $"Mentioned {total} time{(total == 1 ? "" : "s")} in {Appearances.Count} chapter{(Appearances.Count == 1 ? "" : "s")}.";
    }

    public sealed record Appearance(Chapter Chapter, int Count, int FirstOffset, int FirstLength) {
        public string Title => Chapter.Title;
    }
}

/// <summary>The story bible: logline, genre and free-form notes for the whole novel or series, plus each book's synopsis.</summary>
public sealed class StoryBibleDocumentViewModel : DocumentViewModel {
    public StoryBibleDocumentViewModel(MainViewModel shell, NovelProject project) : base(shell) {
        Project = project;
        project.PropertyChanged += (_, e) => {
            if (e.PropertyName == nameof(NovelProject.Kind)) {
                OnPropertyChanged(nameof(Title));
            }
        };
    }

    public NovelProject Project { get; }

    public override string Title => Project.IsSeries ? "Series Bible" : "Story Bible";

    public override string Glyph => Visuals.BibleGlyph;

    public override Brush GlyphBrush => Visuals.ProjectBrush;

    protected override FrameworkElement CreateView() {
        return new StoryBibleView { DataContext = this };
    }
}

/// <summary>A read-only word diff: a file against the last commit (Git Changes) or a chapter against a snapshot.</summary>
public sealed class DiffDocumentViewModel(MainViewModel shell, string fileTitle, string description, string diff, string comparedWith) : DocumentViewModel(shell) {
    public string FileTitle { get; } = fileTitle;

    public override string Title => $"Diff: {FileTitle}";

    public override string ToolTip => $"{FileTitle}: {description} (compared with the {comparedWith})";

    public override string Glyph => "\uE8CB";

    public override Brush GlyphBrush => Visuals.MessageBrush;

    protected override FrameworkElement CreateView() {
        ICSharpCode.AvalonEdit.TextEditor editor = new() {
            IsReadOnly = true,
            WordWrap = true,
            ShowLineNumbers = false,
            FontFamily = new FontFamily("Cambria, Georgia"),
            FontSize = 15,
            Padding = new Thickness(16, 8, 16, 8),
            Text = Clean(diff)
        };
        editor.SetResourceReference(Control.BackgroundProperty, "EditorBackgroundBrush");
        editor.SetResourceReference(Control.ForegroundProperty, "EditorForegroundBrush");
        editor.TextArea.TextView.LineTransformers.Add(new DiffColorizer());
        return editor;
    }

    /// <summary>Drops git's file headers; keeps hunks. An empty diff gets an explanation instead.</summary>
    private static string Clean(string diff) {
        List<string> lines = diff.Replace("\r\n", "\n").Split('\n').ToList();
        int firstHunk = lines.FindIndex(static line => line.StartsWith("@@", StringComparison.Ordinal));
        if (firstHunk > 0) {
            lines.RemoveRange(0, firstHunk);
        }
        string text = string.Join(Environment.NewLine, lines).Trim();
        return text.Length == 0 ? "No differences from the last commit (the change may be only in line endings or file mode)." : text;
    }
}

/// <summary>The Start Page.</summary>
public sealed class StartPageViewModel(MainViewModel shell) : DocumentViewModel(shell) {
    public override string Title => "Start Page";

    public override string Glyph => Visuals.StartPageGlyph;

    public override Brush GlyphBrush => Visuals.ProjectBrush;

    public ObservableCollection<RecentProject> RecentProjects { get; } = [];

    protected override FrameworkElement CreateView() {
        return new StartPageView { DataContext = this };
    }

    public override void OnActivated() {
        RecentProjects.Clear();
        foreach (string path in Services.RecentProjects.Load()) {
            RecentProjects.Add(new RecentProject(Path.GetFileNameWithoutExtension(path), path));
        }
    }

    public sealed record RecentProject(string Name, string Path);
}