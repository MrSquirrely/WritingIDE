using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Rendering;

using WritingIDE.Core.Model;
using WritingIDE.ViewModels;

namespace WritingIDE.Editor;

/// <summary>
/// Margin notes: comment cards to the right of the manuscript, each level with the line it comments on (pushed down
/// when cards would overlap), like comments in Word. Cards are edited in place.
/// </summary>
public sealed class CommentMargin : Border {
    private const double CardMargin = 8;
    private static readonly FontFamily IconFont = new("Segoe MDL2 Assets");

    private readonly ChapterDocumentViewModel _owner;
    private readonly ManuscriptEditor _editor;
    private readonly Canvas _canvas = new() { ClipToBounds = true };
    private readonly Dictionary<ChapterComment, (Border Card, TextBox Box)> _cards = [];
    private bool _repositionQueued;

    public CommentMargin(ChapterDocumentViewModel owner, ManuscriptEditor editor) {
        _owner = owner;
        _editor = editor;
        Width = 260;
        BorderThickness = new Thickness(1, 0, 0, 0);
        SetResourceReference(BackgroundProperty, "CommentMarginBrush");
        SetResourceReference(BorderBrushProperty, "DividerBrush");
        Child = _canvas;

        TextView textView = editor.TextArea.TextView;
        textView.VisualLinesChanged += OnLayoutChanged;
        textView.ScrollOffsetChanged += OnLayoutChanged;
        SizeChanged += OnLayoutChanged;
        owner.Chapter.Comments.CollectionChanged += OnCommentsChanged;
        SyncCards();
    }

    /// <summary>Whether the writer wants the margin (View > Show Comments); it is also hidden while there are no comments.</summary>
    public bool IsWanted {
        get;
        set {
            field = value;
            UpdateVisibility();
        }
    } = true;

    public void Detach() {
        _owner.Chapter.Comments.CollectionChanged -= OnCommentsChanged;
        foreach (ChapterComment comment in _cards.Keys) {
            comment.PropertyChanged -= OnCommentPropertyChanged;
        }
    }

    /// <summary>Puts the caret in a comment's card (used right after adding a comment).</summary>
    public void FocusComment(ChapterComment comment) {
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () => {
            Reposition();
            if (_cards.TryGetValue(comment, out (Border Card, TextBox Box) card)) {
                card.Box.Focus();
                card.Box.CaretIndex = card.Box.Text.Length;
            }
        });
    }

    private void OnCommentsChanged(object? sender, NotifyCollectionChangedEventArgs e) {
        SyncCards();
    }

    private void SyncCards() {
        // Bookmarks live in the gutter, not the margin.
        HashSet<ChapterComment> current = [.. _owner.Chapter.Comments.Where(static comment => comment.Kind != MarkerKind.Bookmark)];
        foreach (ChapterComment removed in _cards.Keys.Where(comment => !current.Contains(comment)).ToList()) {
            _canvas.Children.Remove(_cards[removed].Card);
            removed.PropertyChanged -= OnCommentPropertyChanged;
            _cards.Remove(removed);
        }
        foreach (ChapterComment comment in current.Where(comment => !_cards.ContainsKey(comment))) {
            (Border card, TextBox box) = CreateCard(comment);
            _cards[comment] = (card, box);
            _canvas.Children.Add(card);
            comment.PropertyChanged += OnCommentPropertyChanged;
        }
        UpdateVisibility();
        QueueReposition();
    }

    /// <summary>False when the editor is too narrow for the margin (a split pane): comments stay shaded and in the Outline.</summary>
    public bool HasRoom {
        get;
        set {
            if (field != value) {
                field = value;
                UpdateVisibility();
            }
        }
    } = true;

    private void UpdateVisibility() {
        Visibility = IsWanted && HasRoom && (_cards.Count > 0) ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnCommentPropertyChanged(object? sender, PropertyChangedEventArgs e) {
        if ((sender is ChapterComment comment) && (e.PropertyName == nameof(ChapterComment.IsResolved)) && _cards.TryGetValue(comment, out (Border Card, TextBox Box) card)) {
            card.Card.Opacity = comment.IsResolved ? 0.55 : 1;
            _editor.TextArea.TextView.Redraw();
        }
    }

    private (Border Card, TextBox Box) CreateCard(ChapterComment comment) {
        TextBox box = new() {
            Style = new Style(typeof(TextBox)),
            TextWrapping = TextWrapping.Wrap,
            AcceptsReturn = true,
            BorderThickness = new Thickness(0),
            Background = Brushes.Transparent,
            Padding = new Thickness(0),
            Margin = new Thickness(0, 3, 0, 0),
            FontSize = 12.5,
            MinHeight = 18
        };
        box.SetResourceReference(Control.ForegroundProperty, "TextBrush");
        box.SetResourceReference(TextBoxBase.CaretBrushProperty, "TextBrush");
        box.SetBinding(TextBox.TextProperty, new Binding(nameof(ChapterComment.Text)) { Source = comment, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged });
        AutomationPropertiesHelper.SetName(box, "Comment text");
        box.GotKeyboardFocus += (_, _) => _editor.SetActiveComment(comment);
        box.LostKeyboardFocus += (_, _) => _editor.SetActiveComment(null);
        box.PreviewKeyDown += (_, e) => {
            if (e.Key == Key.Escape) {
                // Back to the manuscript, like Word.
                _editor.TextArea.Focus();
                e.Handled = true;
            }
        };

        bool isThread = comment.Kind == MarkerKind.PlotThread;
        TextBlock header = new() {
            FontSize = 11,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Text = isThread
                ? $"\u25C6 {comment.ThreadRole}: {_owner.Shell.Project?.PlotThreads.FirstOrDefault(thread => thread.Id == comment.ThreadId)?.Name ?? "(deleted thread)"}"
                : $"{(comment.Author.Length > 0 ? comment.Author + "  \u00B7  " : "")}{comment.CreatedUtc.ToLocalTime().ToString("g", CultureInfo.CurrentCulture)}",
            Cursor = Cursors.Hand,
            ToolTip = isThread ? "A plot-thread passage. Click to show it; open Analyze > Plot Threads for the whole thread." : "Show the commented text"
        };
        header.SetResourceReference(TextBlock.ForegroundProperty, isThread ? "EventBrush" : "SubtleTextBrush");
        if (isThread) {
            header.FontWeight = FontWeights.SemiBold;
            HandyControl.Controls.InfoElement.SetPlaceholder(box, "Note (optional)");
        }
        header.MouseLeftButtonDown += (_, e) => {
            _editor.RevealComment(comment);
            e.Handled = true;
        };

        Button resolve = CreateIconButton("\uE73E", comment.IsResolved ? "Reopen" : "Resolve");
        resolve.Click += (_, _) => {
            comment.IsResolved = !comment.IsResolved;
            resolve.ToolTip = comment.IsResolved ? "Reopen" : "Resolve";
        };
        Button delete = CreateIconButton("\uE74D", isThread ? "Remove from the plot thread" : "Delete comment");
        delete.Click += (_, _) => _owner.DeleteComment(comment);

        DockPanel top = new();
        DockPanel.SetDock(delete, Dock.Right);
        DockPanel.SetDock(resolve, Dock.Right);
        top.Children.Add(delete);
        if (!isThread) {
            top.Children.Add(resolve);
        }
        top.Children.Add(header);

        TextBlock orphaned = new() { Text = "The commented text was deleted.", FontSize = 11, FontStyle = FontStyles.Italic, Margin = new Thickness(0, 2, 0, 0) };
        orphaned.SetResourceReference(TextBlock.ForegroundProperty, "WarningTextBrush");
        orphaned.SetBinding(VisibilityProperty, new Binding(nameof(ChapterComment.IsOrphaned)) { Source = comment, Converter = new BooleanToVisibilityConverter() });

        StackPanel content = new();
        content.Children.Add(top);
        content.Children.Add(orphaned);
        content.Children.Add(box);

        Border card = new() {
            Width = Width - (2 * CardMargin) - 1,
            CornerRadius = new CornerRadius(4),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(8, 5, 6, 6),
            Child = content,
            Opacity = comment.IsResolved ? 0.55 : 1
        };
        card.SetResourceReference(BackgroundProperty, isThread ? "PanelBrush" : "CommentCardBrush");
        card.SetResourceReference(BorderBrushProperty, isThread ? "EventBrush" : "CommentCardBorderBrush");
        if (isThread) {
            card.BorderThickness = new Thickness(3, 1, 1, 1);
        }
        card.SizeChanged += (_, _) => QueueReposition();
        return (card, box);
    }

    private static Button CreateIconButton(string glyph, string toolTip) {
        TextBlock icon = new() { Text = glyph, FontFamily = IconFont, FontSize = 11 };
        icon.SetResourceReference(TextBlock.ForegroundProperty, "IconGreyBrush");
        Button button = new() { Content = icon, ToolTip = toolTip, Padding = new Thickness(4, 2, 4, 2), Margin = new Thickness(2, 0, 0, 0), Cursor = Cursors.Hand };
        button.SetResourceReference(StyleProperty, "ToolButton");
        AutomationPropertiesHelper.SetName(button, toolTip);
        return button;
    }

    private void OnLayoutChanged(object? sender, EventArgs e) {
        QueueReposition();
    }

    private void QueueReposition() {
        if (_repositionQueued) {
            return;
        }
        _repositionQueued = true;
        Dispatcher.BeginInvoke(DispatcherPriority.Render, () => {
            _repositionQueued = false;
            Reposition();
        });
    }

    /// <summary>Places each visible comment's card level with its line, stacking cards that would overlap.</summary>
    private void Reposition() {
        if (Visibility != Visibility.Visible) {
            return;
        }
        TextView view = _editor.TextArea.TextView;
        if (!view.VisualLinesValid || (view.VisualLines.Count == 0) || (PresentationSource.FromVisual(this) is null)) {
            return;
        }
        int first = view.VisualLines[0].FirstDocumentLine.Offset;
        int last = view.VisualLines[^1].LastDocumentLine.EndOffset;
        List<(Border Card, double Y)> visible = [];
        foreach (CommentSegment segment in _owner.CommentSegments.OrderBy(static segment => segment.StartOffset)) {
            if (!_cards.TryGetValue(segment.Comment, out (Border Card, TextBox Box) card)) {
                continue;
            }
            if ((segment.StartOffset < first) || (segment.StartOffset > last)) {
                card.Card.Visibility = Visibility.Collapsed;
                continue;
            }
            TextViewPosition position = new(_editor.Document.GetLocation(segment.StartOffset));
            double y = view.GetVisualPosition(position, VisualYPosition.LineTop).Y - view.VerticalOffset;
            visible.Add((card.Card, view.TranslatePoint(new Point(0, y), _canvas).Y));
        }
        double bottom = 4;
        foreach ((Border card, double y) in visible) {
            card.Visibility = Visibility.Visible;
            double top = Math.Max(y - 2, bottom);
            Canvas.SetLeft(card, CardMargin);
            Canvas.SetTop(card, top);
            card.Measure(new Size(card.Width, double.PositiveInfinity));
            bottom = top + card.DesiredSize.Height + 6;
        }
    }
}

/// <summary>Sets UI Automation names from code (screen readers and UI tests).</summary>
internal static class AutomationPropertiesHelper {
    public static void SetName(DependencyObject element, string name) {
        System.Windows.Automation.AutomationProperties.SetName(element, name);
    }
}