using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

using WritingIDE.Core.Analysis;
using WritingIDE.Core.Model;
using WritingIDE.Infrastructure;
using WritingIDE.Views;

namespace WritingIDE.ViewModels;

// =========================================================================
// Character and POV statistics
// =========================================================================

/// <summary>One cell of the presence heat map: how often a character is mentioned in one chapter.</summary>
public sealed record PresenceCell(Chapter Chapter, int Count, double Intensity, string ToolTip);

public sealed record CharacterRow(CharacterStats Stats, string FirstAppearance, string LastAppearance, IReadOnlyList<PresenceCell> Cells) {
    public StoryEntity Entity => Stats.Character;

    public string Name => Stats.Character.Name;

    public string Role => Stats.Character.Attributes.FirstOrDefault(static attribute => attribute.Key.Equals("Role", StringComparison.OrdinalIgnoreCase))?.Value ?? "";

    public Brush Brush => Visuals.GetKindBrush(EntityKind.Character);
}

public sealed record PovRow(Chapter Chapter, string ChapterTitle, string PointOfView, bool IsDetected, int Words) {
    public string Source => IsDetected ? (PointOfView.Length == 0 ? "not detected" : "detected") : "set";
}

/// <summary>A point-of-view character's share of the manuscript.</summary>
public sealed record PovShare(string Name, int Chapters, int Words, double Percent, double BarWidth);

/// <summary>Analyze > Character and POV Statistics.</summary>
public sealed class CharacterStatisticsDocumentViewModel : DocumentViewModel {
    public CharacterStatisticsDocumentViewModel(MainViewModel shell) : base(shell) {
        RefreshCommand = new RelayCommand(Refresh);
        OpenEntityCommand = new RelayCommand(parameter => {
            if (parameter is CharacterRow row) {
                Shell.OpenEntity(row.Entity);
            }
        });
        OpenChapterCommand = new RelayCommand(parameter => {
            switch (parameter) {
                case PovRow row:
                    Shell.OpenChapter(row.Chapter);
                    break;
                case PresenceCell cell:
                    Shell.OpenChapter(cell.Chapter);
                    break;
            }
        });
    }

    public override string Title => "Character Statistics";

    public override string Glyph => "\uE77B";

    public override Brush GlyphBrush => Visuals.GetKindBrush(EntityKind.Character);

    public IReadOnlyList<CharacterRow> Characters { get; private set => SetProperty(ref field, value); } = [];

    public IReadOnlyList<PovRow> PointsOfView { get; private set => SetProperty(ref field, value); } = [];

    public IReadOnlyList<PovShare> PovShares { get; private set => SetProperty(ref field, value); } = [];

    /// <summary>Chapter numbers across the top of the heat map.</summary>
    public IReadOnlyList<string> ChapterNumbers { get; private set => SetProperty(ref field, value); } = [];

    public string Summary { get; private set => SetProperty(ref field, value); } = "";

    public ICommand RefreshCommand { get; }

    public ICommand OpenEntityCommand { get; }

    public ICommand OpenChapterCommand { get; }

    protected override FrameworkElement CreateView() {
        return new CharacterStatisticsView { DataContext = this };
    }

    public override void OnActivated() {
        Refresh();
    }

    public void Refresh() {
        NovelProject? project = Shell.Project;
        if (project is null) {
            return;
        }
        IReadOnlyList<ChapterText> manuscript = Shell.GetManuscript();
        CharacterReport report = CharacterStatistics.Compute(manuscript, project.Entities);
        string Label(int? index) {
            return index is int i ? $"{i + 1}. {manuscript[i].Chapter.Title}" : "-";
        }

        Characters = report.Characters.Select(stats => {
            int max = Math.Max(1, stats.MentionsPerChapter.DefaultIfEmpty(0).Max());
            List<PresenceCell> cells = stats.MentionsPerChapter.Select((count, index) => new PresenceCell(
                manuscript[index].Chapter,
                count,
                count == 0 ? 0 : 0.18 + (0.82 * Math.Sqrt((double)count / max)),
                $"{stats.Character.Name} in {manuscript[index].Chapter.Title}: {count} mention{(count == 1 ? "" : "s")}")).ToList();
            return new CharacterRow(stats, Label(stats.FirstChapterIndex), Label(stats.LastChapterIndex), cells);
        }).ToList();
        PointsOfView = report.Chapters.Select(pov => new PovRow(pov.Chapter, $"{pov.Index + 1}. {project.GetDisplayName(pov.Chapter)}",
            pov.PointOfView.Length == 0 ? "?" : pov.PointOfView, pov.IsDetected, pov.Words)).ToList();
        int totalWords = Math.Max(1, report.Chapters.Sum(static pov => pov.Words));
        List<(string Name, int Chapters, int Words)> shares = report.Chapters
            .GroupBy(static pov => pov.PointOfView.Length == 0 ? "Unknown" : pov.PointOfView)
            .Select(static group => (group.Key, group.Count(), group.Sum(static pov => pov.Words)))
            .OrderByDescending(static share => share.Item3)
            .ToList();
        int maxWords = Math.Max(1, shares.Select(static share => share.Words).DefaultIfEmpty(0).Max());
        PovShares = shares.Select(share => new PovShare(share.Name, share.Chapters, share.Words, Math.Round((100.0 * share.Words) / totalWords, 1), (220.0 * share.Words) / maxWords)).ToList();
        ChapterNumbers = Enumerable.Range(1, manuscript.Count).Select(static number => number.ToString(CultureInfo.CurrentCulture)).ToList();
        int present = report.Characters.Count(static stats => stats.Mentions > 0);
        Summary = $"{report.Characters.Count} characters, {present} appear in the manuscript. {manuscript.Count} chapters, {report.Chapters.Count(static pov => !pov.IsDetected)} with a POV set by you "
            + "(set it in a chapter's Properties; otherwise it is detected from the narration).";
    }
}

// =========================================================================
// Timeline
// =========================================================================

/// <summary>A chapter column on the timeline.</summary>
public sealed record TimelineChapter(Chapter Chapter, int Index, string? BookTitle, string PointOfView, int Words, string? OrderWarning);

/// <summary>An Event story element, placed under the chapter it happens in.</summary>
public sealed record TimelineEvent(StoryEntity Entity, int? ChapterIndex, string Date);

/// <summary>A character's lane: where they appear.</summary>
public sealed record TimelineLane(StoryEntity Character, IReadOnlyList<int> MentionsPerChapter);

/// <summary>Analyze > Timeline: chapters in reading order with their story dates, events and character appearances.</summary>
public sealed class TimelineDocumentViewModel(MainViewModel shell) : DocumentViewModel(shell) {
    public override string Title => "Timeline";

    public override string Glyph => "\uE823";

    public override Brush GlyphBrush => Visuals.GetKindBrush(EntityKind.Event);

    public IReadOnlyList<TimelineChapter> Chapters { get; private set; } = [];

    public IReadOnlyList<TimelineEvent> Events { get; private set; } = [];

    public IReadOnlyList<TimelineLane> Lanes { get; private set; } = [];

    public IReadOnlyList<string> ChapterTexts { get; private set; } = [];

    /// <summary>Raised after <see cref="Refresh"/> so the view rebuilds.</summary>
    public event Action? Refreshed;

    protected override FrameworkElement CreateView() {
        TimelineView view = new(this);
        Refresh();
        return view;
    }

    public override void OnActivated() {
        Refresh();
    }

    public void Refresh() {
        NovelProject? project = Shell.Project;
        if (project is null) {
            return;
        }
        IReadOnlyList<ChapterText> manuscript = Shell.GetManuscript();
        CharacterReport report = CharacterStatistics.Compute(manuscript, project.Entities);

        List<TimelineChapter> chapters = [];
        double? latest = null;
        string? latestTitle = null;
        for (int i = 0; i < manuscript.Count; i++) {
            Chapter chapter = manuscript[i].Chapter;
            double? key = StoryTime.TryGetSortKey(chapter.StoryDate);
            string? warning = null;
            if (key is double value) {
                if ((latest is double previous) && (value < previous)) {
                    warning = $"Set before {latestTitle} ({FormatDate(manuscript, latestTitle)}). A flashback?";
                }
                if ((latest is null) || (value >= latest)) {
                    latest = value;
                    latestTitle = chapter.Title;
                }
            }
            ChapterPointOfView pov = report.Chapters[i];
            chapters.Add(new TimelineChapter(chapter, i, project.FindBook(chapter.BookId)?.Title, pov.PointOfView, pov.Words, warning));
        }

        List<string> titles = manuscript.Select(static entry => entry.Chapter.Title).ToList();
        Events =project.GetEntities(EntityKind.Event).Select(entity => {
            string date = Attribute(entity, "Date");
            string chapterReference = Attribute(entity, "Chapter");
            int? index = chapterReference.Length > 0 ? new ManuscriptPosition(0, titles).Resolve(chapterReference) : null;
            if (index is null) {
                for (int i = 0; i < manuscript.Count; i++) {
                    if (EntityIndex.FindMentions(manuscript[i].Text, entity).Count > 0) {
                        index = i;
                        break;
                    }
                }
            }
            return new TimelineEvent(entity, index, date);
        }).ToList();

        Lanes = report.Characters
            .Where(static stats => stats.Mentions > 0)
            .OrderBy(static stats => stats.FirstChapterIndex)
            .ThenByDescending(static stats => stats.Mentions)
            .Take(24)
            .Select(static stats => new TimelineLane(stats.Character, stats.MentionsPerChapter))
            .ToList();
        Chapters = chapters;
        ChapterTexts = manuscript.Select(static entry => entry.Text).ToList();
        Refreshed?.Invoke();
    }

    private static string FormatDate(IReadOnlyList<ChapterText> manuscript, string? title) {
        return manuscript.FirstOrDefault(entry => entry.Chapter.Title == title)?.Chapter.StoryDate ?? "";
    }

    private static string Attribute(StoryEntity entity, string key) {
        return entity.Attributes.FirstOrDefault(attribute => attribute.Key.Equals(key, StringComparison.OrdinalIgnoreCase))?.Value.Trim() ?? "";
    }

    /// <summary>Opens the chapter at the character's first mention in it.</summary>
    public void GoToMention(StoryEntity character, int chapterIndex) {
        if ((chapterIndex < 0) || (chapterIndex >= Chapters.Count)) {
            return;
        }
        Chapter chapter = Chapters[chapterIndex].Chapter;
        EntityMention? mention = EntityIndex.FindMentions(Shell.GetChapterText(chapter), character).FirstOrDefault();
        if (mention is null) {
            Shell.OpenChapter(chapter);
        }
        else {
            Shell.NavigateTo(chapter, mention.Offset, mention.Length);
        }
    }
}

// =========================================================================
// Relationship map
// =========================================================================

/// <summary>Analyze > Relationship Map: story elements as nodes, relationships and shared scenes as links.</summary>
public sealed class RelationshipMapDocumentViewModel(MainViewModel shell) : DocumentViewModel(shell) {
    /// <summary>The area the layout is computed in; positions are stored as fractions of it.</summary>
    public const double CanvasWidth = 1000;
    public const double CanvasHeight = 620;

    public override string Title => "Relationship Map";

    public override string Glyph => "\uE716";

    public override Brush GlyphBrush => Visuals.GetKindBrush(EntityKind.Faction);

    public bool IncludeCharacters { get; set => SetAndRefresh(ref field, value); } = true;

    public bool IncludeFactions { get; set => SetAndRefresh(ref field, value); } = true;

    public bool IncludePlaces { get; set => SetAndRefresh(ref field, value); }

    public bool IncludeOthers { get; set => SetAndRefresh(ref field, value); }

    /// <summary>Paragraphs two elements must share to be linked; 0 shows only the relationships you defined.</summary>
    public int MinimumSharedParagraphs { get; set => SetAndRefresh(ref field, value); } = 3;

    public IReadOnlyList<int> SharedParagraphChoices { get; } = [0, 1, 2, 3, 5, 10, 20];

    public IReadOnlyList<StoryEntity> Nodes { get; private set; } = [];

    public IReadOnlyList<GraphEdge> Edges { get; private set; } = [];

    public Dictionary<Guid, (double X, double Y)> Positions { get; private set; } = [];

    public event Action? Refreshed;

    protected override FrameworkElement CreateView() {
        RelationshipMapView view = new(this);
        Refresh();
        return view;
    }

    public override void OnActivated() {
        Refresh();
    }

    public void Refresh() {
        NovelProject? project = Shell.Project;
        if (project is null) {
            return;
        }
        Nodes = project.Entities.Where(entity => entity.Kind switch {
            EntityKind.Character => IncludeCharacters,
            EntityKind.Faction => IncludeFactions,
            EntityKind.Place => IncludePlaces,
            _ => IncludeOthers
        }).ToList();
        Edges = RelationshipGraph.Build(Nodes, Shell.GetManuscript().Select(static entry => entry.Text), MinimumSharedParagraphs);
        Positions = RelationshipGraph.Layout(Nodes, Edges, CanvasWidth, CanvasHeight);
        Refreshed?.Invoke();
    }

    /// <summary>Forgets where you dragged the shown elements and lays them out again.</summary>
    public void Rearrange() {
        foreach (StoryEntity node in Nodes) {
            node.MapX = null;
            node.MapY = null;
        }
        Refresh();
    }

    private void SetAndRefresh<T>(ref T storage, T value, [System.Runtime.CompilerServices.CallerMemberName] string? propertyName = null) {
        if (SetProperty(ref storage, value, propertyName)) {
            Refresh();
        }
    }
}