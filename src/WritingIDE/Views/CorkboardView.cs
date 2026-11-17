using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;

using WritingIDE.Core.Model;
using WritingIDE.ViewModels;

namespace WritingIDE.Views;

/// <summary>
/// The corkboard: each chapter an index card with its synopsis, status, words and point of view. Cards are dragged by
/// their top edge into a new order, or into another book of a series.
/// </summary>
public sealed class CorkboardView : UserControl {
    private const string DragFormat = "WritingIDE.CorkboardChapter";
    private const double CardWidth = 230;

    private readonly CorkboardDocumentViewModel _model;
    private readonly StackPanel _books = new() { Margin = new Thickness(18, 0, 18, 24) };
    private Point? _dragStart;
    private Border? _marked;

    public CorkboardView(CorkboardDocumentViewModel model) {
        _model = model;
        SetResourceReference(BackgroundProperty, "SurfaceBrush");
        model.Refreshed += Build;

        DockPanel root = new();
        root.Children.Add(PlanningUi.Header("Corkboard",
            "Every chapter as an index card. Drag a card by its top edge to move it, type a synopsis on it, and click its title to open the chapter. "
            + "The coloured edge shows the status: grey outline, blue draft, amber revised, green final.",
            PlanningUi.Button("New chapter", () => {
                _model.Shell.AddChapterCommand.Execute(null);
                Build();
            }),
            PlanningUi.Button("Refresh", Build)));
        root.Children.Add(new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = _books });
        Content = root;
        Build();
    }

    private void Build() {
        _books.Children.Clear();
        MainViewModel shell = _model.Shell;
        NovelProject? project = shell.Project;
        if (project is null) {
            return;
        }
        int number = 0;
        if (project.IsSeries) {
            foreach (Book book in project.Books) {
                AddBook(project, book, project.GetChapters(book).ToList(), ref number);
            }
            List<Chapter> loose = project.Chapters.Where(chapter => project.FindBook(chapter.BookId) is null).ToList();
            if (loose.Count > 0) {
                AddBook(project, null, loose, ref number, "Not in a book");
            }
        }
        else {
            AddBook(project, null, project.Chapters.ToList(), ref number);
        }
    }

    private void AddBook(NovelProject project, Book? book, List<Chapter> chapters, ref int number, string? heading = null) {
        MainViewModel shell = _model.Shell;
        heading ??= book?.Title;
        if (heading is not null) {
            int words = chapters.Sum(shell.GetWordCount);
            TextBlock title = new() { FontSize = 15, FontWeight = FontWeights.SemiBold, Margin = new Thickness(6, 14, 0, 4) };
            title.Inlines.Add(heading);
            System.Windows.Documents.Run detail = new($"   {chapters.Count} chapter{(chapters.Count == 1 ? "" : "s")}, {words:N0} words") { FontSize = 12, FontWeight = FontWeights.Normal };
            detail.SetResourceReference(System.Windows.Documents.TextElement.ForegroundProperty, "SubtleTextBrush");
            title.Inlines.Add(detail);
            _books.Children.Add(title);
        }
        WrapPanel cards = new() { MinHeight = 60, Background = Brushes.Transparent, AllowDrop = true };
        foreach (Chapter chapter in chapters) {
            cards.Children.Add(CreateCard(project, chapter, ++number));
        }
        if (chapters.Count == 0) {
            cards.Children.Add(PlanningUi.Subtle("No chapters yet. Drop a card here.", margin: new Thickness(8, 18, 0, 0)));
        }
        // Dropping on the empty space after the cards moves the chapter to the end of this book.
        cards.DragOver += (_, e) => {
            e.Effects = e.Data.GetDataPresent(DragFormat) ? DragDropEffects.Move : DragDropEffects.None;
            e.Handled = true;
        };
        cards.Drop += (_, e) => {
            if (e.Data.GetData(DragFormat) is Chapter dragged) {
                _model.Shell.MoveChapterToEnd(dragged, book);
                e.Handled = true;
                Dispatcher.BeginInvoke(Build);
            }
        };
        _books.Children.Add(cards);
    }

    private Border CreateCard(NovelProject project, Chapter chapter, int number) {
        MainViewModel shell = _model.Shell;

        // The grip: the card's top edge, coloured by status. Dragging starts here, so text boxes stay usable.
        Border grip = new() { Height = 22, CornerRadius = new CornerRadius(3, 3, 0, 0), Cursor = Cursors.SizeAll, ToolTip = "Drag to move this chapter" };
        grip.SetResourceReference(Border.BackgroundProperty, StatusBrushKey(chapter.Status));
        DockPanel gripContent = new() { Margin = new Thickness(8, 0, 8, 0) };
        TextBlock status = new() { Text = chapter.Status.ToString(), Foreground = Brushes.White, FontSize = 11, VerticalAlignment = VerticalAlignment.Center };
        DockPanel.SetDock(status, Dock.Right);
        gripContent.Children.Add(status);
        gripContent.Children.Add(new TextBlock { Text = $"#{number}", Foreground = Brushes.White, FontSize = 11, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center });
        grip.Child = gripContent;

        StackPanel body = new() { Margin = new Thickness(10, 6, 10, 8) };
        Button title = PlanningUi.Link(chapter.Title, () => shell.OpenChapter(chapter), "Open chapter");
        ((TextBlock)title.Content).FontWeight = FontWeights.SemiBold;
        title.MaxWidth = CardWidth - 24;
        body.Children.Add(title);

        TextBox synopsis = PlanningUi.Box($"Synopsis of {chapter.Title}");
        synopsis.AcceptsReturn = true;
        synopsis.TextWrapping = TextWrapping.Wrap;
        synopsis.Height = 92;
        synopsis.VerticalContentAlignment = VerticalAlignment.Top;
        synopsis.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        synopsis.Margin = new Thickness(0, 4, 0, 4);
        synopsis.FontSize = 12;
        HandyControl.Controls.InfoElement.SetPlaceholder(synopsis, "What happens in this chapter?");
        synopsis.SetBinding(TextBox.TextProperty, new Binding(nameof(Chapter.Synopsis)) { Source = chapter, UpdateSourceTrigger = UpdateSourceTrigger.LostFocus });
        body.Children.Add(synopsis);

        DockPanel footer = new();
        ComboBox statusBox = new() { Width = 86, ItemsSource = Enum.GetValues<ChapterStatus>(), SelectedItem = chapter.Status, FontSize = 11 };
        System.Windows.Automation.AutomationProperties.SetName(statusBox, $"Status of {chapter.Title}");
        statusBox.SelectionChanged += (_, _) => {
            if (statusBox.SelectedItem is ChapterStatus selected && (selected != chapter.Status)) {
                chapter.Status = selected;
                project.MarkDirty();
                grip.SetResourceReference(Border.BackgroundProperty, StatusBrushKey(selected));
                status.Text = selected.ToString();
            }
        };
        DockPanel.SetDock(statusBox, Dock.Right);
        footer.Children.Add(statusBox);
        string pov = chapter.PointOfView.Length > 0 ? $"  ·  {chapter.PointOfView}" : "";
        TextBlock details = PlanningUi.Subtle($"{shell.GetWordCount(chapter).ToString("N0", CultureInfo.CurrentCulture)} words{pov}", size: 11);
        details.VerticalAlignment = VerticalAlignment.Center;
        details.TextTrimming = TextTrimming.CharacterEllipsis;
        footer.Children.Add(details);
        body.Children.Add(footer);

        StackPanel content = new();
        content.Children.Add(grip);
        content.Children.Add(body);
        Border card = PlanningUi.Card(content, new Thickness(0));
        card.Width = CardWidth;
        card.Margin = new Thickness(6);
        card.BorderThickness = new Thickness(2, 1, 2, 1);
        card.AllowDrop = true;
        System.Windows.Automation.AutomationProperties.SetName(card, $"Card {number}: {chapter.Title}");

        grip.MouseLeftButtonDown += (_, e) => _dragStart = e.GetPosition(this);
        grip.MouseLeftButtonUp += (_, _) => _dragStart = null;
        grip.MouseMove += (_, e) => {
            if ((_dragStart is Point start) && (e.LeftButton == MouseButtonState.Pressed)) {
                Vector moved = e.GetPosition(this) - start;
                if ((Math.Abs(moved.X) > SystemParameters.MinimumHorizontalDragDistance) || (Math.Abs(moved.Y) > SystemParameters.MinimumVerticalDragDistance)) {
                    _dragStart = null;
                    card.Opacity = 0.45;
                    DragDrop.DoDragDrop(card, new DataObject(DragFormat, chapter), DragDropEffects.Move);
                    card.Opacity = 1;
                    ClearMark();
                }
            }
        };
        // Preview events: the synopsis box would otherwise refuse the drop itself, as it only takes text.
        card.PreviewDragOver += (_, e) => {
            e.Handled = true;
            if ((e.Data.GetData(DragFormat) is not Chapter dragged) || ReferenceEquals(dragged, chapter)) {
                e.Effects = DragDropEffects.None;
                ClearMark();
                return;
            }
            e.Effects = DragDropEffects.Move;
            // The thick edge shows on which side of this card the dragged one lands.
            bool after = e.GetPosition(card).X > (card.ActualWidth / 2);
            ClearMark();
            _marked = card;
            card.BorderThickness = after ? new Thickness(2, 1, 5, 1) : new Thickness(5, 1, 2, 1);
            card.SetResourceReference(Border.BorderBrushProperty, "AccentBrush");
        };
        card.DragLeave += (_, _) => ClearMark();
        card.PreviewDrop += (_, e) => {
            e.Handled = true;
            if ((e.Data.GetData(DragFormat) is Chapter dragged) && !ReferenceEquals(dragged, chapter)) {
                bool after = e.GetPosition(card).X > (card.ActualWidth / 2);
                _model.Shell.MoveChapterNextTo(dragged, chapter, after);
                Dispatcher.BeginInvoke(Build);
            }
        };
        return card;
    }

    private void ClearMark() {
        if (_marked is not null) {
            _marked.BorderThickness = new Thickness(2, 1, 2, 1);
            _marked.SetResourceReference(Border.BorderBrushProperty, "DividerBrush");
            _marked = null;
        }
    }

    private static string StatusBrushKey(ChapterStatus status) {
        return status switch {
            ChapterStatus.Draft => "MessageBrush",
            ChapterStatus.Revised => "WarningBrush",
            ChapterStatus.Final => "SuccessBrush",
            _ => "IconGreyBrush"
        };
    }
}