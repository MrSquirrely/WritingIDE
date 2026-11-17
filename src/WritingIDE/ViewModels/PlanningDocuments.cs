using System.Windows;
using System.Windows.Media;

using WritingIDE.Core.Analysis;
using WritingIDE.Core.Model;
using WritingIDE.Infrastructure;
using WritingIDE.Views;

namespace WritingIDE.ViewModels;

/// <summary>A planning page that is rebuilt from the project whenever it comes to the front.</summary>
public abstract class PlanningDocumentViewModel(MainViewModel shell) : DocumentViewModel(shell) {
    /// <summary>Raised when the page should rebuild.</summary>
    public event Action? Refreshed;

    public override Brush GlyphBrush => Visuals.IconBrush;

    public override void OnActivated() {
        Refresh();
    }

    public void Refresh() {
        Refreshed?.Invoke();
    }
}

/// <summary>Analyze > Writing Progress: the daily goal, streak, deadline, sprints and the writing history.</summary>
public sealed class WritingProgressDocumentViewModel(MainViewModel shell) : PlanningDocumentViewModel(shell) {
    public override string Title => "Writing Progress";

    public override string Glyph => "";

    protected override FrameworkElement CreateView() {
        return new WritingProgressView(this);
    }

    public override FrameworkElement? CreateSecondaryView() {
        return new WritingProgressView(this);
    }
}

/// <summary>Project > Corkboard: every chapter as an index card, dragged into order.</summary>
public sealed class CorkboardDocumentViewModel(MainViewModel shell) : PlanningDocumentViewModel(shell) {
    public override string Title => "Corkboard";

    public override string Glyph => "";

    protected override FrameworkElement CreateView() {
        return new CorkboardView(this);
    }
}

/// <summary>Analyze > Beat Sheet: a story structure's beats, placed in chapters, against where the structure expects them.</summary>
public sealed class BeatSheetDocumentViewModel(MainViewModel shell) : PlanningDocumentViewModel(shell) {
    public override string Title => "Beat Sheet";

    public override string Glyph => "";

    public IReadOnlyList<BeatPlacement> Analyze() {
        NovelProject? project = Shell.Project;
        return project?.BeatSheet is BeatSheet sheet ? BeatSheetAnalyzer.Analyze(sheet, Shell.GetManuscript()) : [];
    }

    protected override FrameworkElement CreateView() {
        return new BeatSheetView(this);
    }
}

/// <summary>Analyze > Thread Chart: plot threads against chapters, to see one going quiet.</summary>
public sealed class ThreadChartDocumentViewModel(MainViewModel shell) : PlanningDocumentViewModel(shell) {
    public override string Title => "Thread Chart";

    public override string Glyph => "";

    protected override FrameworkElement CreateView() {
        return new ThreadChartView(this);
    }
}