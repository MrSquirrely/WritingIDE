using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;

using WritingIDE.Core.Model;
using WritingIDE.Infrastructure;
using WritingIDE.ViewModels;

namespace WritingIDE.Views;

/// <summary>
/// The timeline: one column per chapter in reading order (story date, POV, words, synopsis), the events that happen in
/// each, and a lane per character showing where they appear. Story dates are edited in place.
/// </summary>
public sealed class TimelineView : UserControl {
    private const double ColumnWidth = 196;
    private const double LabelWidth = 150;
    private static readonly FontFamily IconFont = new("Segoe MDL2 Assets");

    private readonly TimelineDocumentViewModel _model;
    private readonly Grid _grid = new() { Margin = new Thickness(12, 8, 24, 24) };
    private readonly TextBlock _unplaced = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0) };

    public TimelineView(TimelineDocumentViewModel model) {
        _model = model;
        SetResourceReference(BackgroundProperty, "SurfaceBrush");
        model.Refreshed += Build;

        Button refresh = new() { Content = "Refresh", Padding = new Thickness(10, 4, 10, 4), Margin = new Thickness(0) };
        refresh.SetResourceReference(StyleProperty, "TileButton");
        refresh.Click += (_, _) => _model.Refresh();
        System.Windows.Automation.AutomationProperties.SetName(refresh, "Refresh timeline");
        DockPanel.SetDock(refresh, Dock.Right);

        TextBlock title = new() { Text = "Timeline", FontSize = 20, FontWeight = FontWeights.SemiBold };
        TextBlock hint = new() {
            Text = "Chapters in reading order. Type a story date on a card (\"Day 3\", \"Year 2, Day 14\", \"12 May 1204\") to get warnings when a chapter is set earlier than the one before it. "
                + "Events appear under the chapter that first mentions them, or the one named in their Chapter attribute.",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 2, 0, 0)
        };
        hint.SetResourceReference(TextBlock.ForegroundProperty, "SubtleTextBrush");
        _unplaced.SetResourceReference(TextBlock.ForegroundProperty, "SubtleTextBrush");
        StackPanel heading = new();
        heading.Children.Add(title);
        heading.Children.Add(hint);
        heading.Children.Add(_unplaced);
        DockPanel header = new() { Margin = new Thickness(24, 16, 24, 6) };
        header.Children.Add(refresh);
        header.Children.Add(heading);
        DockPanel.SetDock(header, Dock.Top);

        DockPanel root = new();
        root.Children.Add(header);
        root.Children.Add(new ScrollViewer { HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = _grid });
        Content = root;
    }

    private void Build() {
        _grid.Children.Clear();
        _grid.ColumnDefinitions.Clear();
        _grid.RowDefinitions.Clear();
        IReadOnlyList<TimelineChapter> chapters = _model.Chapters;
        _grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(LabelWidth) });
        foreach (TimelineChapter _ in chapters) {
            _grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(ColumnWidth) });
        }
        bool series = chapters.Any(static chapter => chapter.BookTitle is not null);
        int bookRow = 0;
        int cardRow = 1;
        int eventRow = 2;
        int firstLaneRow = 3;
        _grid.RowDefinitions.Add(new RowDefinition { Height = series ? new GridLength(26) : new GridLength(0) });
        _grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto, MinHeight = 34 });
        foreach (TimelineLane _ in _model.Lanes) {
            _grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(26) });
        }
        int rowCount = _grid.RowDefinitions.Count;

        // Column separators behind everything.
        for (int column = 1; column <= chapters.Count; column++) {
            Border separator = new() { BorderThickness = new Thickness(1, 0, 0, 0) };
            separator.SetResourceReference(Border.BorderBrushProperty, "DividerBrush");
            Place(separator, 1, column, rowSpan: rowCount - 1);
            Panel.SetZIndex(separator, -1);
        }

        // Books (series): one band per run of chapters in the same book.
        if (series) {
            int start = 0;
            while (start < chapters.Count) {
                int end = start;
                while ((end + 1 < chapters.Count) && (chapters[end + 1].BookTitle == chapters[start].BookTitle)) {
                    end++;
                }
                Border band = new() { CornerRadius = new CornerRadius(3), Margin = new Thickness(4, 2, 4, 2), Padding = new Thickness(8, 2, 8, 2) };
                band.SetResourceReference(Border.BackgroundProperty, "BadgeBrush");
                band.Child = new TextBlock { Text = chapters[start].BookTitle ?? "(no book)", FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis };
                Place(band, bookRow, start + 1, columnSpan: end - start + 1);
                start = end + 1;
            }
        }

        // Chapter cards.
        Place(Label("Chapters", null), cardRow, 0);
        foreach (TimelineChapter chapter in chapters) {
            Place(CreateCard(chapter), cardRow, chapter.Index + 1);
        }

        // Events.
        Place(Label("Events", "\uE787"), eventRow, 0);
        foreach (IGrouping<int, TimelineEvent> group in _model.Events.Where(static e => e.ChapterIndex.HasValue).GroupBy(static e => e.ChapterIndex!.Value)) {
            StackPanel chips = new() { Margin = new Thickness(6, 4, 6, 4) };
            foreach (TimelineEvent timelineEvent in group) {
                chips.Children.Add(CreateEventChip(timelineEvent));
            }
            Place(chips, eventRow, group.Key + 1);
        }
        List<string> unplaced = _model.Events.Where(static e => !e.ChapterIndex.HasValue).Select(static e => e.Entity.Name).ToList();
        _unplaced.Text = unplaced.Count == 0 ? "" : $"Events not placed yet: {string.Join(", ", unplaced)}. Mention them in a chapter or give them a Chapter attribute.";
        _unplaced.Visibility = unplaced.Count == 0 ? Visibility.Collapsed : Visibility.Visible;

        // Character lanes.
        for (int lane = 0; lane < _model.Lanes.Count; lane++) {
            TimelineLane timelineLane = _model.Lanes[lane];
            int row = firstLaneRow + lane;
            Button name = new() { Content = new TextBlock { Text = timelineLane.Character.Name, TextTrimming = TextTrimming.CharacterEllipsis }, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center, MaxWidth = LabelWidth - 8 };
            name.SetResourceReference(StyleProperty, "LinkButton");
            StoryEntity character = timelineLane.Character;
            name.Click += (_, _) => _model.Shell.OpenEntity(character);
            Place(name, row, 0);

            IReadOnlyList<int> counts = timelineLane.MentionsPerChapter;
            int first = counts.ToList().FindIndex(static count => count > 0);
            int last = counts.ToList().FindLastIndex(static count => count > 0);
            if (first < 0) {
                continue;
            }
            if (last > first) {
                Rectangle line = new() { Height = 2, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(ColumnWidth / 2, 0, ColumnWidth / 2, 0), Opacity = 0.45 };
                line.SetResourceReference(Shape.FillProperty, "CharacterBrush");
                Place(line, row, first + 1, columnSpan: last - first + 1);
            }
            for (int index = 0; index < counts.Count; index++) {
                if (counts[index] == 0) {
                    continue;
                }
                double size = 8 + Math.Min(10, 2 * Math.Sqrt(counts[index]));
                Ellipse dot = new() { Width = size, Height = size, Cursor = Cursors.Hand, ToolTip = $"{character.Name}: {counts[index]} mention{(counts[index] == 1 ? "" : "s")} in {chapters[index].Chapter.Title}. Click to go there." };
                dot.SetResourceReference(Shape.FillProperty, "CharacterBrush");
                int chapterIndex = index;
                dot.MouseLeftButtonUp += (_, _) => _model.GoToMention(character, chapterIndex);
                Place(dot, row, index + 1);
            }
        }
        if (chapters.Count == 0) {
            Place(new TextBlock { Text = "No chapters yet." }, cardRow, 0);
        }
    }

    private Border CreateCard(TimelineChapter item) {
        Chapter chapter = item.Chapter;
        StackPanel panel = new();

        TextBlock number = new() { Text = $"#{item.Index + 1}  \u00B7  {chapter.Status}", FontSize = 11 };
        number.SetResourceReference(TextBlock.ForegroundProperty, "SubtleTextBrush");
        panel.Children.Add(number);

        Button title = new() { Content = new TextBlock { Text = chapter.Title, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis }, HorizontalAlignment = HorizontalAlignment.Left, ToolTip = "Open chapter" };
        title.SetResourceReference(StyleProperty, "LinkButton");
        title.Click += (_, _) => _model.Shell.OpenChapter(chapter);
        panel.Children.Add(title);

        TextBox date = new() { Margin = new Thickness(0, 4, 0, 2), MinHeight = 26 };
        date.SetResourceReference(StyleProperty, "FormTextBox");
        HandyControl.Controls.InfoElement.SetPlaceholder(date, "Story date, e.g. Day 3");
        System.Windows.Automation.AutomationProperties.SetName(date, $"Story date of {chapter.Title}");
        date.SetBinding(TextBox.TextProperty, new Binding(nameof(Chapter.StoryDate)) { Source = chapter, UpdateSourceTrigger = UpdateSourceTrigger.LostFocus });
        date.LostFocus += (_, _) => Dispatcher.BeginInvoke(DispatcherPriority.Background, _model.Refresh);
        date.KeyDown += (_, e) => {
            if (e.Key == Key.Enter) {
                Keyboard.ClearFocus();
            }
        };
        panel.Children.Add(date);

        TextBlock details = new() { FontSize = 11, TextTrimming = TextTrimming.CharacterEllipsis };
        details.Text = $"POV: {(item.PointOfView.Length == 0 ? "?" : item.PointOfView)}  \u00B7  {item.Words.ToString("N0", CultureInfo.CurrentCulture)} words";
        details.SetResourceReference(TextBlock.ForegroundProperty, "LabelBrush");
        panel.Children.Add(details);

        if (chapter.Synopsis.Length > 0) {
            TextBlock synopsis = new() { Text = chapter.Synopsis, TextWrapping = TextWrapping.Wrap, TextTrimming = TextTrimming.WordEllipsis, MaxHeight = 48, FontSize = 11, Margin = new Thickness(0, 4, 0, 0), ToolTip = chapter.Synopsis };
            synopsis.SetResourceReference(TextBlock.ForegroundProperty, "SubtleTextBrush");
            panel.Children.Add(synopsis);
        }
        if (item.OrderWarning is not null) {
            TextBlock warning = new() { TextWrapping = TextWrapping.Wrap, FontSize = 11, Margin = new Thickness(0, 4, 0, 0) };
            warning.Inlines.Add(new System.Windows.Documents.Run("\uE7BA ") { FontFamily = IconFont });
            warning.Inlines.Add(new System.Windows.Documents.Run(item.OrderWarning));
            warning.SetResourceReference(TextBlock.ForegroundProperty, "WarningBrush");
            panel.Children.Add(warning);
        }

        Border card = new() { Child = panel, Padding = new Thickness(8, 6, 8, 8), Margin = new Thickness(6, 4, 6, 4), CornerRadius = new CornerRadius(4), BorderThickness = new Thickness(1) };
        card.SetResourceReference(Border.BackgroundProperty, "PanelBrush");
        card.SetResourceReference(Border.BorderBrushProperty, item.OrderWarning is null ? "DividerBrush" : "WarningBrush");
        return card;
    }

    private Border CreateEventChip(TimelineEvent timelineEvent) {
        TextBlock text = new() { TextWrapping = TextWrapping.Wrap, Foreground = Brushes.White, FontSize = 11.5 };
        text.Inlines.Add(new System.Windows.Documents.Run(timelineEvent.Entity.Name) { FontWeight = FontWeights.SemiBold });
        if (timelineEvent.Date.Length > 0) {
            text.Inlines.Add(new System.Windows.Documents.Run($"  {timelineEvent.Date}") { Foreground = new SolidColorBrush(Color.FromArgb(0xDD, 0xFF, 0xFF, 0xFF)) });
        }
        Border chip = new() { Child = text, CornerRadius = new CornerRadius(10), Padding = new Thickness(8, 2, 8, 3), Margin = new Thickness(0, 2, 0, 2), Cursor = Cursors.Hand, ToolTip = timelineEvent.Entity.Summary.Length > 0 ? timelineEvent.Entity.Summary : "Open event" };
        chip.SetResourceReference(Border.BackgroundProperty, "EventBrush");
        StoryEntity entity = timelineEvent.Entity;
        chip.MouseLeftButtonUp += (_, _) => _model.Shell.OpenEntity(entity);
        return chip;
    }

    private static FrameworkElement Label(string text, string? glyph) {
        TextBlock label = new() { FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 8, 0, 0), VerticalAlignment = VerticalAlignment.Top };
        if (glyph is not null) {
            label.Inlines.Add(new System.Windows.Documents.Run(glyph + " ") { FontFamily = IconFont });
        }
        label.Inlines.Add(new System.Windows.Documents.Run(text));
        label.SetResourceReference(TextBlock.ForegroundProperty, "LabelBrush");
        return label;
    }

    private void Place(UIElement element, int row, int column, int rowSpan = 1, int columnSpan = 1) {
        Grid.SetRow(element, row);
        Grid.SetColumn(element, column);
        Grid.SetRowSpan(element, rowSpan);
        Grid.SetColumnSpan(element, columnSpan);
        _grid.Children.Add(element);
    }
}