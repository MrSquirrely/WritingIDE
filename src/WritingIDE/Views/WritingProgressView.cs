using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

using WritingIDE.Core.Analysis;
using WritingIDE.Core.Model;
using WritingIDE.ViewModels;

namespace WritingIDE.Views;

/// <summary>Pieces the planning pages share: the page header, buttons and text in the app's styles.</summary>
internal static class PlanningUi {
    public static readonly FontFamily IconFont = new("Segoe MDL2 Assets");

    /// <summary>The title and hint at the top of a page, with optional buttons on the right.</summary>
    public static DockPanel Header(string title, string hint, params UIElement[] buttons) {
        DockPanel header = new() { Margin = new Thickness(24, 16, 24, 10) };
        StackPanel right = new() { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Top };
        foreach (UIElement button in buttons) {
            right.Children.Add(button);
        }
        DockPanel.SetDock(right, Dock.Right);
        header.Children.Add(right);
        StackPanel heading = new();
        heading.Children.Add(new TextBlock { Text = title, FontSize = 20, FontWeight = FontWeights.SemiBold });
        heading.Children.Add(Subtle(hint, wrap: true, margin: new Thickness(0, 2, 16, 0)));
        header.Children.Add(heading);
        DockPanel.SetDock(header, Dock.Top);
        return header;
    }

    public static Button Button(string text, Action click, string? toolTip = null) {
        Button button = new() { Content = text, Padding = new Thickness(10, 4, 10, 4), Margin = new Thickness(6, 0, 0, 0), ToolTip = toolTip };
        button.SetResourceReference(FrameworkElement.StyleProperty, "TileButton");
        button.Click += (_, _) => click();
        return button;
    }

    public static Button Link(string text, Action click, string? toolTip = null) {
        Button button = new() { Content = new TextBlock { Text = text, TextTrimming = TextTrimming.CharacterEllipsis }, HorizontalAlignment = HorizontalAlignment.Left, ToolTip = toolTip };
        button.SetResourceReference(FrameworkElement.StyleProperty, "LinkButton");
        button.Click += (_, _) => click();
        return button;
    }

    public static TextBlock Subtle(string text, bool wrap = false, Thickness margin = default, double size = 12) {
        TextBlock block = new() { Text = text, FontSize = size, Margin = margin, TextWrapping = wrap ? TextWrapping.Wrap : TextWrapping.NoWrap };
        block.SetResourceReference(TextBlock.ForegroundProperty, "SubtleTextBrush");
        return block;
    }

    public static TextBox Box(string name, double width = double.NaN) {
        TextBox box = new() { MinHeight = 28, Width = width, VerticalContentAlignment = VerticalAlignment.Center };
        box.SetResourceReference(FrameworkElement.StyleProperty, "FormTextBox");
        System.Windows.Automation.AutomationProperties.SetName(box, name);
        box.KeyDown += (_, e) => {
            if ((e.Key == Key.Enter) && !box.AcceptsReturn) {
                Keyboard.ClearFocus();
            }
        };
        return box;
    }

    /// <summary>A thin progress bar without a percentage label, 0 to 1.</summary>
    public static ProgressBar Bar(double fraction, string name) {
        ProgressBar bar = new() { Height = 6, Maximum = 1, Value = Math.Clamp(fraction, 0, 1), Template = BarTemplate };
        bar.SetResourceReference(Control.BackgroundProperty, "BadgeBrush");
        bar.SetResourceReference(Control.ForegroundProperty, "AccentBrush");
        System.Windows.Automation.AutomationProperties.SetName(bar, name);
        return bar;
    }

    private static ControlTemplate BarTemplate { get; } = (ControlTemplate)System.Windows.Markup.XamlReader.Parse(
        "<ControlTemplate xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\" TargetType=\"ProgressBar\">"
        + "<Grid><Border x:Name=\"PART_Track\" Background=\"{TemplateBinding Background}\" CornerRadius=\"3\" />"
        + "<Border x:Name=\"PART_Indicator\" Background=\"{TemplateBinding Foreground}\" CornerRadius=\"3\" HorizontalAlignment=\"Left\" /></Grid></ControlTemplate>");

    /// <summary>A bordered panel in the app's card colours.</summary>
    public static Border Card(UIElement child, Thickness? padding = null) {
        Border card = new() { Child = child, Padding = padding ?? new Thickness(14, 10, 14, 12), CornerRadius = new CornerRadius(4), BorderThickness = new Thickness(1) };
        card.SetResourceReference(Border.BackgroundProperty, "PanelBrush");
        card.SetResourceReference(Border.BorderBrushProperty, "DividerBrush");
        return card;
    }
}

/// <summary>Writing Progress: today against the goal, the streak, the deadline, sprints, and a heat map of the writing log.</summary>
public sealed class WritingProgressView : UserControl {
    private const int Weeks = 26;
    private const double Cell = 15;

    private readonly WritingProgressDocumentViewModel _model;
    private readonly WrapPanel _tiles = new() { Margin = new Thickness(18, 0, 18, 0) };
    private readonly TextBox _dailyGoal = PlanningUi.Box("Daily word goal", 90);
    private readonly TextBox _target = PlanningUi.Box("Target word count", 90);
    private readonly DatePicker _deadline = new() { Width = 150, MinHeight = 28 };
    private readonly ComboBox _minutes = new() { Width = 70, ItemsSource = new[] { 5, 10, 15, 20, 25, 30, 45, 60 } };
    private readonly Button _sprintButton;
    private readonly TextBlock _sprintClock = new() { FontSize = 34, FontWeight = FontWeights.Light, Margin = new Thickness(0, 6, 0, 0) };
    private readonly TextBlock _sprintDetail;
    private readonly Canvas _heat = new() { Height = (7 * Cell) + 22, Width = ((Weeks + 1) * Cell) + 40, HorizontalAlignment = HorizontalAlignment.Left };
    private readonly TextBlock _heatSummary;
    private bool _loading;

    public WritingProgressView(WritingProgressDocumentViewModel model) {
        _model = model;
        SetResourceReference(BackgroundProperty, "SurfaceBrush");
        MainViewModel shell = model.Shell;

        _sprintDetail = PlanningUi.Subtle("", wrap: true, margin: new Thickness(0, 2, 0, 0));
        _heatSummary = PlanningUi.Subtle("", wrap: true, margin: new Thickness(0, 6, 0, 0));
        _sprintButton = PlanningUi.Button("Start sprint", () => shell.ToggleSprintCommand.Execute(null));
        _sprintButton.Margin = new Thickness(10, 0, 0, 0);
        System.Windows.Automation.AutomationProperties.SetName(_minutes, "Sprint length in minutes");
        System.Windows.Automation.AutomationProperties.SetName(_deadline, "Deadline");

        StackPanel body = new() { Margin = new Thickness(0, 0, 0, 24) };
        body.Children.Add(_tiles);
        body.Children.Add(Section("Goals", GoalsPanel()));
        body.Children.Add(Section("Writing sprint", SprintPanel()));
        StackPanel history = new();
        history.Children.Add(_heat);
        history.Children.Add(_heatSummary);
        body.Children.Add(Section($"History (the last {Weeks} weeks)", history));

        DockPanel root = new();
        root.Children.Add(PlanningUi.Header("Writing Progress",
            "WritingIDE logs the words you add each day while the project is open (cutting counts against the day; importing does not count). "
            + "Put this page in the second pane (Window > Split) to watch it while you write."));
        root.Children.Add(new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = body });
        Content = root;

        _dailyGoal.LostFocus += (_, _) => SaveGoals();
        _target.LostFocus += (_, _) => SaveGoals();
        _deadline.SelectedDateChanged += (_, _) => SaveGoals();
        _minutes.SelectionChanged += (_, _) => {
            if (!_loading && (_minutes.SelectedItem is int minutes)) {
                shell.SprintMinutes = minutes;
            }
        };
        // Listen only while on screen: the page is rebuilt when it is shown again.
        Loaded += (_, _) => {
            shell.ProgressChanged += Update;
            shell.PropertyChanged += OnShellPropertyChanged;
            _model.Refreshed += Load;
            Load();
        };
        Unloaded += (_, _) => {
            shell.ProgressChanged -= Update;
            shell.PropertyChanged -= OnShellPropertyChanged;
            _model.Refreshed -= Load;
        };
    }

    private void OnShellPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e) {
        if (e.PropertyName is nameof(MainViewModel.SprintText) or nameof(MainViewModel.IsSprinting)) {
            UpdateSprint();
        }
    }

    private static StackPanel Section(string title, UIElement content) {
        StackPanel section = new() { Margin = new Thickness(24, 18, 24, 0) };
        section.Children.Add(new TextBlock { Text = title, FontSize = 14, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 8) });
        section.Children.Add(content);
        return section;
    }

    private WrapPanel GoalsPanel() {
        WrapPanel panel = new();
        panel.Children.Add(Field("Words a day (0 for none)", _dailyGoal));
        panel.Children.Add(Field("Manuscript target (words)", _target));
        StackPanel deadline = new() { Orientation = Orientation.Horizontal };
        deadline.Children.Add(_deadline);
        Button clear = PlanningUi.Link("Clear", () => _deadline.SelectedDate = null, "Remove the deadline");
        clear.Margin = new Thickness(8, 0, 0, 0);
        clear.VerticalAlignment = VerticalAlignment.Center;
        deadline.Children.Add(clear);
        panel.Children.Add(Field("Deadline", deadline));
        return panel;
    }

    private static StackPanel Field(string label, UIElement input) {
        StackPanel field = new() { Margin = new Thickness(0, 0, 28, 6) };
        field.Children.Add(PlanningUi.Subtle(label, margin: new Thickness(0, 0, 0, 3)));
        field.Children.Add(input);
        return field;
    }

    private StackPanel SprintPanel() {
        StackPanel controls = new() { Orientation = Orientation.Horizontal };
        controls.Children.Add(_minutes);
        controls.Children.Add(new TextBlock { Text = "minutes", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 0, 0) });
        controls.Children.Add(_sprintButton);
        StackPanel panel = new();
        panel.Children.Add(controls);
        panel.Children.Add(_sprintClock);
        panel.Children.Add(_sprintDetail);
        return panel;
    }

    /// <summary>Fills the inputs from the project (when the page is shown), then draws everything.</summary>
    private void Load() {
        NovelProject? project = _model.Shell.Project;
        if (project is null) {
            return;
        }
        _loading = true;
        _dailyGoal.Text = project.DailyWordGoal.ToString(CultureInfo.CurrentCulture);
        _target.Text = project.TargetWordCount.ToString(CultureInfo.CurrentCulture);
        _deadline.SelectedDate = project.Deadline?.ToDateTime(TimeOnly.MinValue);
        _minutes.SelectedItem = _model.Shell.SprintMinutes;
        if (_minutes.SelectedItem is null) {
            _minutes.SelectedIndex = 4;
        }
        _loading = false;
        Update();
    }

    private void SaveGoals() {
        NovelProject? project = _model.Shell.Project;
        if (_loading || (project is null)) {
            return;
        }
        if (int.TryParse(_dailyGoal.Text, NumberStyles.Integer | NumberStyles.AllowThousands, CultureInfo.CurrentCulture, out int goal) && (goal >= 0)) {
            project.DailyWordGoal = goal;
        }
        if (int.TryParse(_target.Text, NumberStyles.Integer | NumberStyles.AllowThousands, CultureInfo.CurrentCulture, out int target) && (target > 0)) {
            project.TargetWordCount = target;
        }
        project.Deadline = _deadline.SelectedDate is DateTime date ? DateOnly.FromDateTime(date) : null;
        _loading = true;
        _dailyGoal.Text = project.DailyWordGoal.ToString(CultureInfo.CurrentCulture);
        _target.Text = project.TargetWordCount.ToString(CultureInfo.CurrentCulture);
        _loading = false;
        _model.Shell.OnGoalsChanged();
    }

    /// <summary>Redraws the tiles, the sprint and the heat map (the inputs are left alone, the writer may be typing in them).</summary>
    private void Update() {
        MainViewModel shell = _model.Shell;
        NovelProject? project = shell.Project;
        if (project is null) {
            return;
        }
        DateOnly today = DateOnly.FromDateTime(DateTime.Now);
        int goal = project.DailyWordGoal;
        int written = WritingProgress.WrittenOn(project.WritingLog, today);
        int total = shell.TotalWords;
        ProgressForecast forecast = WritingProgress.Forecast(project.WritingLog, today, total, project.TargetWordCount, project.Deadline);
        int streak = WritingProgress.Streak(project.WritingLog, today, goal);
        int best = WritingProgress.BestStreak(project.WritingLog, goal);

        _tiles.Children.Clear();
        _tiles.Children.Add(Tile("Today", written.ToString("N0", CultureInfo.CurrentCulture),
            goal > 0 ? (written >= goal ? $"Goal of {goal:N0} reached" : $"{Math.Max(0, goal - written):N0} to go of {goal:N0}") : "No daily goal set",
            goal > 0 ? Math.Clamp((double)written / goal, 0, 1) : null));
        _tiles.Children.Add(Tile("Streak", $"{streak} day{(streak == 1 ? "" : "s")}",
            goal > 0 ? $"Days in a row at {goal:N0} words or more. Best: {best}." : $"Days in a row with words written. Best: {best}.", null));
        _tiles.Children.Add(Tile("Manuscript", total.ToString("N0", CultureInfo.CurrentCulture),
            forecast.Remaining == 0 ? $"Target of {project.TargetWordCount:N0} reached" : $"{forecast.Remaining:N0} to go of {project.TargetWordCount:N0}",
            Math.Clamp((double)total / Math.Max(1, project.TargetWordCount), 0, 1)));
        string deadlineValue = forecast.DaysLeft switch {
            null => "No deadline",
            <= 0 => "Passed",
            1 => "Today",
            int days => $"{days} days"
        };
        string pace = forecast.AveragePerDay > 0 ? $"You average {forecast.AveragePerDay:N0} on the days you write." : "Write for a few days to get a forecast.";
        string deadlineDetail = forecast switch {
            { Remaining: 0 } => "The target is reached.",
            { WordsPerDayNeeded: int needed } => $"{needed:N0} words a day gets you there. {pace}",
            { DaysLeft: <= 0 } => $"The deadline was {project.Deadline:d MMM yyyy}.",
            _ => pace
        };
        if ((forecast.Remaining > 0) && (forecast.ProjectedFinish is DateOnly finish)) {
            deadlineDetail += $" At this pace you finish around {finish:d MMM yyyy}.";
        }
        _tiles.Children.Add(Tile("Deadline", deadlineValue, deadlineDetail, null));

        UpdateSprint();
        DrawHeatMap(project, today, goal);
    }

    private void UpdateSprint() {
        MainViewModel shell = _model.Shell;
        _sprintButton.Content = shell.IsSprinting ? "Stop sprint" : "Start sprint";
        _minutes.IsEnabled = !shell.IsSprinting;
        TimeSpan remaining = shell.SprintRemaining < TimeSpan.Zero ? TimeSpan.Zero : shell.SprintRemaining;
        _sprintClock.Text = shell.IsSprinting ? $"{(int)remaining.TotalMinutes}:{remaining.Seconds:00}" : "";
        _sprintClock.Visibility = shell.IsSprinting ? Visibility.Visible : Visibility.Collapsed;
        _sprintDetail.Text = shell.IsSprinting
            ? $"{shell.SprintWords:N0} words so far. The status bar shows the clock while you write."
            : (shell.LastSprintText.Length > 0 ? shell.LastSprintText : "A sprint is a timed burst of writing: pick a length, start, and write until the clock runs out.");
    }

    private static Border Tile(string label, string value, string detail, double? progress) {
        StackPanel panel = new() { Width = 200 };
        panel.Children.Add(PlanningUi.Subtle(label));
        panel.Children.Add(new TextBlock { Text = value, FontSize = 28, FontWeight = FontWeights.Light });
        if (progress is double fraction) {
            ProgressBar bar = PlanningUi.Bar(fraction, $"{label} progress");
            bar.Margin = new Thickness(0, 4, 0, 4);
            panel.Children.Add(bar);
        }
        panel.Children.Add(PlanningUi.Subtle(detail, wrap: true));
        Border card = PlanningUi.Card(panel);
        card.Margin = new Thickness(6, 0, 6, 8);
        return card;
    }

    /// <summary>One square per day, a column per week (Monday at the top), darker for more words.</summary>
    private void DrawHeatMap(NovelProject project, DateOnly today, int goal) {
        _heat.Children.Clear();
        Dictionary<DateOnly, WritingDay> days = project.WritingLog.ToDictionary(static day => day.Date);
        // The last column is this week; the grid starts on the Monday 25 weeks before this week's Monday.
        int weekday = ((int)today.DayOfWeek + 6) % 7;
        DateOnly start = today.AddDays(-weekday - (7 * (Weeks - 1)));
        int scale = Math.Max(goal, Math.Max(1, days.Values.Where(day => day.Date >= start).Select(static day => day.Written).DefaultIfEmpty(0).Max()));
        const double left = 34;
        const double top = 18;
        string[] labels = ["Mon", "", "Wed", "", "Fri", "", "Sun"];
        for (int row = 0; row < 7; row++) {
            if (labels[row].Length > 0) {
                TextBlock label = PlanningUi.Subtle(labels[row], size: 10);
                Canvas.SetLeft(label, 0);
                Canvas.SetTop(label, top + (row * Cell));
                _heat.Children.Add(label);
            }
        }
        int writingDays = 0;
        int words = 0;
        int lastMonth = start.Month;
        for (int week = 0; week < Weeks; week++) {
            DateOnly monday = start.AddDays(7 * week);
            // A label where a month starts (the first, partial month has no room for one).
            if (monday.Month != lastMonth) {
                lastMonth = monday.Month;
                TextBlock month = PlanningUi.Subtle(monday.ToString("MMM", CultureInfo.CurrentCulture), size: 10);
                Canvas.SetLeft(month, left + (week * Cell));
                Canvas.SetTop(month, 0);
                _heat.Children.Add(month);
            }
            for (int row = 0; row < 7; row++) {
                DateOnly date = monday.AddDays(row);
                if (date > today) {
                    continue;
                }
                int written = days.TryGetValue(date, out WritingDay? day) ? day.Written : 0;
                Rectangle square = new() { Width = Cell - 3, Height = Cell - 3, RadiusX = 2, RadiusY = 2 };
                if (written > 0) {
                    writingDays++;
                    words += written;
                    square.SetResourceReference(Shape.FillProperty, "SuccessBrush");
                    square.Opacity = 0.25 + (0.75 * Math.Min(1, (double)written / scale));
                }
                else {
                    square.SetResourceReference(Shape.FillProperty, "BadgeBrush");
                }
                if (date == today) {
                    square.SetResourceReference(Shape.StrokeProperty, "AccentBrush");
                    square.StrokeThickness = 1.5;
                }
                square.ToolTip = $"{date:ddd d MMM yyyy}: {(written == 0 ? "nothing written" : $"{written:N0} words")}{((goal > 0) && (written >= goal) ? " (goal met)" : "")}";
                ToolTipService.SetInitialShowDelay(square, 150);
                Canvas.SetLeft(square, left + (week * Cell));
                Canvas.SetTop(square, top + (row * Cell));
                _heat.Children.Add(square);
            }
        }
        _heatSummary.Text = writingDays == 0
            ? "Nothing logged yet. Squares fill in as you write; hover one for the day's words."
            : $"{words:N0} words on {writingDays} day{(writingDays == 1 ? "" : "s")} in the last {Weeks} weeks. Darker squares are bigger days; hover one for its words.";
    }
}