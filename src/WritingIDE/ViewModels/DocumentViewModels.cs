using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

using ICSharpCode.AvalonEdit.Document;

using WritingIDE.Core;
using WritingIDE.Core.Analysis;
using WritingIDE.Core.Grammar;
using WritingIDE.Core.Model;
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

    protected override FrameworkElement CreateView() {
        _editor = new ManuscriptEditor(this);
        if (LastResult is not null) {
            _editor.ApplyAnalysis(LastResult);
        }
        return _editor;
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
        GrammarOptions options = Shell.GrammarOptions;
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
            _editor?.ApplyAnalysis(result);
            Shell.ReportAnalysis(Chapter, text, result);
        }
        catch (OperationCanceledException) {
            // Superseded by a newer analysis.
        }
    }

    public void SyncToChapter() {
        Chapter.Text = Document.Text;
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