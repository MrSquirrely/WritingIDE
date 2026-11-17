using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Shapes;

using WritingIDE.Core.Analysis;
using WritingIDE.Core.Model;
using WritingIDE.ViewModels;

namespace WritingIDE.Views;

/// <summary>
/// The beat sheet. Before a structure is chosen it lists the templates; afterwards, each beat with the chapter it
/// happens in, the writer's notes, and a track showing where the template expects the beat against where it falls.
/// </summary>
public sealed class BeatSheetView : UserControl {
    private const double TrackWidth = 220;

    private readonly BeatSheetDocumentViewModel _model;
    private readonly DockPanel _root = new();

    public BeatSheetView(BeatSheetDocumentViewModel model) {
        _model = model;
        SetResourceReference(BackgroundProperty, "SurfaceBrush");
        model.Refreshed += Build;
        Content = _root;
        Build();
    }

    private void Build() {
        _root.Children.Clear();
        NovelProject? project = _model.Shell.Project;
        if (project is null) {
            return;
        }
        if (project.BeatSheet is BeatSheet sheet) {
            BuildSheet(project, sheet);
        }
        else {
            BuildTemplates(project);
        }
    }

    private void BuildTemplates(NovelProject project) {
        _root.Children.Add(PlanningUi.Header("Beat Sheet",
            "A beat sheet lists a story's turning points and roughly where each should fall. Choose a structure to plan against; "
            + "you then place each beat in a chapter and WritingIDE shows whether it lands early, late or out of order."));
        StackPanel list = new() { Margin = new Thickness(18, 0, 18, 24) };
        foreach (BeatTemplate template in BeatTemplate.All) {
            StackPanel text = new();
            text.Children.Add(new TextBlock { Text = template.Name, FontSize = 15, FontWeight = FontWeights.SemiBold });
            text.Children.Add(PlanningUi.Subtle(template.Description, wrap: true, margin: new Thickness(0, 2, 0, 4)));
            text.Children.Add(PlanningUi.Subtle(string.Join("  →  ", template.Beats.Select(static beat => beat.Name)), wrap: true, size: 11));
            DockPanel row = new();
            Button use = PlanningUi.Button("Use this structure", () => {
                project.BeatSheet = template.Create();
                Build();
            });
            use.VerticalAlignment = VerticalAlignment.Center;
            System.Windows.Automation.AutomationProperties.SetName(use, $"Use {template.Name}");
            DockPanel.SetDock(use, Dock.Right);
            row.Children.Add(use);
            row.Children.Add(text);
            Border card = PlanningUi.Card(row);
            card.Margin = new Thickness(6, 0, 6, 8);
            card.MaxWidth = 900;
            card.HorizontalAlignment = HorizontalAlignment.Left;
            list.Children.Add(card);
        }
        _root.Children.Add(new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = list });
    }

    private void BuildSheet(NovelProject project, BeatSheet sheet) {
        MainViewModel shell = _model.Shell;
        IReadOnlyList<BeatPlacement> placements = _model.Analyze();
        List<Chapter> chapters = shell.OrderedChapters.ToList();
        int placed = placements.Count(static placement => placement.Chapter is not null);
        int problems = placements.Count(static placement => placement is { Chapter: not null, Problem: not null });

        _root.Children.Add(PlanningUi.Header($"Beat Sheet: {sheet.Template}",
            $"{placed} of {placements.Count} beats placed{(problems > 0 ? $", {problems} to look at" : "")}. "
            + "On each track the line marks where the structure expects the beat and the bar is the chapter you put it in. "
            + $"Positions are measured against the {shell.TotalWords:N0} words written so far, so they settle as the draft fills in.",
            PlanningUi.Button("Change structure...", () => {
                if (MessageBox.Show(Window.GetWindow(this)!, "Choosing another structure replaces this beat sheet, including your notes and the chapters you placed beats in.",
                    "Change structure", MessageBoxButton.OKCancel, MessageBoxImage.Warning) == MessageBoxResult.OK) {
                    project.BeatSheet = null;
                    Build();
                }
            }),
            PlanningUi.Button("Refresh", Build)));

        Grid grid = new() { Margin = new Thickness(24, 0, 24, 24) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(210) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(TrackWidth + 24) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(210) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 140 });

        string[] headers = ["Beat", "Where it falls (0% to 100%)", "Chapter", "Your notes"];
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        for (int column = 0; column < headers.Length; column++) {
            TextBlock header = PlanningUi.Subtle(headers[column], margin: new Thickness(0, 0, 0, 6));
            header.FontWeight = FontWeights.SemiBold;
            Grid.SetColumn(header, column);
            grid.Children.Add(header);
        }

        // "(not placed)" first, then the chapters in reading order.
        List<ChapterChoice> choices = [new(null, "(not placed)")];
        choices.AddRange(chapters.Select((chapter, index) => new ChapterChoice(chapter, $"{index + 1}. {project.GetDisplayName(chapter)}")));

        for (int i = 0; i < placements.Count; i++) {
            BeatPlacement placement = placements[i];
            Beat beat = placement.Beat;
            int row = i + 1;
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            Border line = new() { BorderThickness = new Thickness(0, 1, 0, 0) };
            line.SetResourceReference(Border.BorderBrushProperty, "DividerBrush");
            Grid.SetRow(line, row);
            Grid.SetColumnSpan(line, 4);
            grid.Children.Add(line);

            StackPanel name = new() { Margin = new Thickness(0, 8, 12, 8) };
            name.Children.Add(new TextBlock { Text = beat.Name, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
            name.Children.Add(PlanningUi.Subtle($"around {beat.ExpectedPercent:0}%", size: 11));
            name.Children.Add(PlanningUi.Subtle(beat.Description, wrap: true, margin: new Thickness(0, 2, 0, 0), size: 11));
            Place(grid, name, row, 0);

            StackPanel where = new() { Margin = new Thickness(0, 10, 24, 8) };
            where.Children.Add(CreateTrack(placement));
            if (placement.Problem is not null) {
                TextBlock problem = new() { Text = placement.Problem, TextWrapping = TextWrapping.Wrap, FontSize = 11, Margin = new Thickness(0, 4, 0, 0) };
                problem.SetResourceReference(TextBlock.ForegroundProperty, placement.Chapter is null ? "SubtleTextBrush" : "WarningBrush");
                where.Children.Add(problem);
            }
            else if (placement is { StartPercent: double start, EndPercent: double end }) {
                where.Children.Add(PlanningUi.Subtle($"Its chapter runs from {start:0}% to {end:0}%.", margin: new Thickness(0, 4, 0, 0), size: 11));
            }
            Place(grid, where, row, 1);

            ComboBox chapterBox = new() { ItemsSource = choices, DisplayMemberPath = nameof(ChapterChoice.Label), Margin = new Thickness(0, 8, 12, 8), VerticalAlignment = VerticalAlignment.Top };
            chapterBox.SelectedItem = choices.FirstOrDefault(choice => choice.Chapter?.Id == beat.ChapterId) ?? choices[0];
            System.Windows.Automation.AutomationProperties.SetName(chapterBox, $"Chapter for {beat.Name}");
            chapterBox.SelectionChanged += (_, _) => {
                Guid? selected = (chapterBox.SelectedItem as ChapterChoice)?.Chapter?.Id;
                if (selected != beat.ChapterId) {
                    beat.ChapterId = selected;
                    project.MarkDirty();
                    Dispatcher.BeginInvoke(Build);
                }
            };
            Place(grid, chapterBox, row, 2);

            TextBox notes = PlanningUi.Box($"Notes for {beat.Name}");
            notes.AcceptsReturn = true;
            notes.TextWrapping = TextWrapping.Wrap;
            notes.MinHeight = 44;
            notes.VerticalContentAlignment = VerticalAlignment.Top;
            notes.Margin = new Thickness(0, 8, 0, 8);
            HandyControl.Controls.InfoElement.SetPlaceholder(notes, "How does your story hit this beat?");
            notes.SetBinding(TextBox.TextProperty, new Binding(nameof(Beat.Notes)) { Source = beat, UpdateSourceTrigger = UpdateSourceTrigger.LostFocus });
            string before = beat.Notes;
            notes.LostFocus += (_, _) => {
                if (beat.Notes != before) {
                    before = beat.Notes;
                    project.MarkDirty();
                }
            };
            Place(grid, notes, row, 3);
        }
        _root.Children.Add(new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = grid });
    }

    /// <summary>The story from 0% to 100%: a line where the beat is expected, a bar where its chapter is.</summary>
    private static Canvas CreateTrack(BeatPlacement placement) {
        Canvas track = new() { Width = TrackWidth, Height = 16, HorizontalAlignment = HorizontalAlignment.Left };
        Rectangle rail = new() { Width = TrackWidth, Height = 6, RadiusX = 3, RadiusY = 3 };
        rail.SetResourceReference(Shape.FillProperty, "BadgeBrush");
        Canvas.SetTop(rail, 5);
        track.Children.Add(rail);
        if (placement is { StartPercent: double start, EndPercent: double end }) {
            Rectangle span = new() { Width = Math.Max(4, (end - start) * TrackWidth / 100), Height = 6, RadiusX = 3, RadiusY = 3 };
            span.SetResourceReference(Shape.FillProperty, placement.Problem is null ? "SuccessBrush" : "WarningBrush");
            Canvas.SetLeft(span, Math.Min(TrackWidth - span.Width, start * TrackWidth / 100));
            Canvas.SetTop(span, 5);
            track.Children.Add(span);
        }
        Rectangle expected = new() { Width = 2, Height = 16 };
        expected.SetResourceReference(Shape.FillProperty, "TextBrush");
        Canvas.SetLeft(expected, Math.Clamp((placement.Beat.ExpectedPercent * TrackWidth / 100) - 1, 0, TrackWidth - 2));
        track.Children.Add(expected);
        track.ToolTip = placement.Chapter is null
            ? $"Expected around {placement.Beat.ExpectedPercent:0}%."
            : $"Expected around {placement.Beat.ExpectedPercent:0}%; '{placement.Chapter.Title}' runs from {placement.StartPercent:0}% to {placement.EndPercent:0}%.";
        return track;
    }

    private static void Place(Grid grid, UIElement element, int row, int column) {
        Grid.SetRow(element, row);
        Grid.SetColumn(element, column);
        grid.Children.Add(element);
    }

    private sealed record ChapterChoice(Chapter? Chapter, string Label);
}

/// <summary>
/// The thread chart (a braid chart): one row per plot thread, one column per chapter, a dot wherever the thread has a
/// marked passage. A thread that goes quiet for several chapters is shaded.
/// </summary>
public sealed class ThreadChartView : UserControl {
    private const double ColumnWidth = 30;
    private const double RowHeight = 30;
    private const double LabelWidth = 220;

    /// <summary>A thread missing from this many chapters in a row is pointed out.</summary>
    private const int QuietChapters = 3;

    private readonly ThreadChartDocumentViewModel _model;
    private readonly DockPanel _root = new();

    public ThreadChartView(ThreadChartDocumentViewModel model) {
        _model = model;
        SetResourceReference(BackgroundProperty, "SurfaceBrush");
        model.Refreshed += Build;
        Content = _root;
        Build();
    }

    private void Build() {
        _root.Children.Clear();
        MainViewModel shell = _model.Shell;
        NovelProject? project = shell.Project;
        if (project is null) {
            return;
        }
        IReadOnlyList<ChapterText> manuscript = shell.GetManuscript();
        IReadOnlyList<BraidRow> rows = ThreadBraid.Build(project, manuscript);
        _root.Children.Add(PlanningUi.Header("Thread Chart",
            "Plot threads down the side, chapters across. A dot marks a chapter where the thread has a passage: "
            + "blue for a setup, purple for a development, green for a payoff. Shading shows where a thread goes quiet for "
            + $"{QuietChapters} chapters or more between two of its passages. Click a dot to open the chapter.",
            PlanningUi.Button("Plot Threads", () => shell.OpenPlotThreadsCommand.Execute(null), "Add threads and see their passages"),
            PlanningUi.Button("Refresh", Build)));
        if (rows.Count == 0) {
            _root.Children.Add(PlanningUi.Subtle(
                "No plot threads yet. Select a passage in a chapter, right-click, and choose Mark for Plot Thread to start one.",
                wrap: true, margin: new Thickness(24, 8, 24, 0), size: 13));
            return;
        }

        Grid grid = new() { Margin = new Thickness(24, 0, 24, 24), HorizontalAlignment = HorizontalAlignment.Left };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(LabelWidth) });
        foreach (ChapterText _ in manuscript) {
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(ColumnWidth) });
        }
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(24) });

        for (int column = 0; column < manuscript.Count; column++) {
            Chapter chapter = manuscript[column].Chapter;
            TextBlock number = PlanningUi.Subtle((column + 1).ToString(), size: 11);
            number.HorizontalAlignment = HorizontalAlignment.Center;
            number.VerticalAlignment = VerticalAlignment.Center;
            number.ToolTip = project.GetDisplayName(chapter);
            Grid.SetColumn(number, column + 1);
            grid.Children.Add(number);
        }

        for (int index = 0; index < rows.Count; index++) {
            BraidRow row = rows[index];
            int gridRow = index + 1;
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(RowHeight) });

            Border line = new() { BorderThickness = new Thickness(0, 1, 0, 0) };
            line.SetResourceReference(Border.BorderBrushProperty, "DividerBrush");
            Grid.SetRow(line, gridRow);
            Grid.SetColumnSpan(line, manuscript.Count + 2);
            grid.Children.Add(line);

            TextBlock name = new() { Text = row.Thread.Name, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0), ToolTip = $"{row.Thread.Name} ({row.Thread.Status})" };
            if (row.Thread.Status == PlotThreadStatus.Abandoned) {
                name.SetResourceReference(TextBlock.ForegroundProperty, "SubtleTextBrush");
            }
            Grid.SetRow(name, gridRow);
            grid.Children.Add(name);

            bool quiet = row.LongestGap >= QuietChapters;
            if (quiet) {
                Border gap = new() { Margin = new Thickness(0, 4, 0, 4), CornerRadius = new CornerRadius(3), ToolTip = $"'{row.Thread.Name}' does not appear in these {row.LongestGap} chapters." };
                gap.SetResourceReference(Border.BackgroundProperty, "WarningPanelBrush");
                Grid.SetRow(gap, gridRow);
                Grid.SetColumn(gap, row.GapStart + 1);
                Grid.SetColumnSpan(gap, row.LongestGap);
                grid.Children.Add(gap);
            }

            // The strand from the thread's first passage to its last.
            List<int> marked = Enumerable.Range(0, row.Cells.Count).Where(i => !row.Cells[i].IsEmpty).ToList();
            if (marked.Count > 1) {
                Rectangle strand = new() { Height = 2, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(ColumnWidth / 2, 0, ColumnWidth / 2, 0), Opacity = 0.4 };
                strand.SetResourceReference(Shape.FillProperty, "LabelBrush");
                Grid.SetRow(strand, gridRow);
                Grid.SetColumn(strand, marked[0] + 1);
                Grid.SetColumnSpan(strand, marked[^1] - marked[0] + 1);
                grid.Children.Add(strand);
            }
            foreach (int column in marked) {
                Chapter chapter = manuscript[column].Chapter;
                BraidCell cell = row.Cells[column];
                StackPanel dots = new() { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
                List<string> roles = [];
                if (cell.Setup) {
                    dots.Children.Add(Dot("MessageBrush"));
                    roles.Add("setup");
                }
                if (cell.Development) {
                    dots.Children.Add(Dot("EventBrush"));
                    roles.Add("development");
                }
                if (cell.Payoff) {
                    dots.Children.Add(Dot("SuccessBrush"));
                    roles.Add("payoff");
                }
                Button open = new() { Content = dots, Background = Brushes.Transparent, BorderThickness = new Thickness(0), Padding = new Thickness(0), Cursor = System.Windows.Input.Cursors.Hand,
                    ToolTip = $"{row.Thread.Name}: {string.Join(", ", roles)} in {project.GetDisplayName(chapter)}" };
                open.Template = DotButtonTemplate;
                System.Windows.Automation.AutomationProperties.SetName(open, $"{row.Thread.Name}, chapter {column + 1}: {string.Join(", ", roles)}");
                open.Click += (_, _) => shell.OpenChapter(chapter);
                Grid.SetRow(open, gridRow);
                Grid.SetColumn(open, column + 1);
                grid.Children.Add(open);
            }

            string note = marked.Count == 0
                ? "No passages marked yet"
                : (quiet ? $"Quiet for {row.LongestGap} chapters ({row.GapStart + 1}–{row.GapStart + row.LongestGap})" : "");
            TextBlock noteText = new() { Text = note, FontSize = 11, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0) };
            noteText.SetResourceReference(TextBlock.ForegroundProperty, quiet ? "WarningBrush" : "SubtleTextBrush");
            Grid.SetRow(noteText, gridRow);
            Grid.SetColumn(noteText, manuscript.Count + 1);
            grid.Children.Add(noteText);
        }
        _root.Children.Add(new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, Content = grid });
    }

    /// <summary>A button that is only its content, so the dots are not drawn inside a button frame.</summary>
    private static ControlTemplate DotButtonTemplate { get; } = CreateDotButtonTemplate();

    private static ControlTemplate CreateDotButtonTemplate() {
        FrameworkElementFactory border = new(typeof(Border));
        border.SetValue(Border.BackgroundProperty, Brushes.Transparent);
        border.AppendChild(new FrameworkElementFactory(typeof(ContentPresenter)));
        return new ControlTemplate(typeof(Button)) { VisualTree = border };
    }

    private static Ellipse Dot(string brushKey) {
        Ellipse dot = new() { Width = 10, Height = 10, Margin = new Thickness(1, 0, 1, 0) };
        dot.SetResourceReference(Shape.FillProperty, brushKey);
        return dot;
    }
}