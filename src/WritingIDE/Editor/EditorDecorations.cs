using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.TextFormatting;

using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Editing;
using ICSharpCode.AvalonEdit.Rendering;

using WritingIDE.Core.Analysis;
using WritingIDE.Core.Model;
using WritingIDE.Infrastructure;

namespace WritingIDE.Editor;

/// <summary>The bookmark gutter: a bookmark icon on each bookmarked line; click the gutter to toggle one.</summary>
public sealed class BookmarkMargin(TextSegmentCollection<CommentSegment> markers, Action<int> toggleAtLine) : AbstractMargin {
    private static readonly Typeface IconFace = new(new FontFamily("Segoe MDL2 Assets"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);

    protected override Size MeasureOverride(Size availableSize) {
        return new Size(18, 0);
    }

    protected override void OnTextViewChanged(TextView? oldTextView, TextView? newTextView) {
        if (oldTextView is not null) {
            oldTextView.VisualLinesChanged -= OnVisualLinesChanged;
        }
        base.OnTextViewChanged(oldTextView, newTextView);
        if (newTextView is not null) {
            newTextView.VisualLinesChanged += OnVisualLinesChanged;
        }
        InvalidateVisual();
    }

    private void OnVisualLinesChanged(object? sender, EventArgs e) {
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext drawingContext) {
        drawingContext.DrawRectangle(Brushes.Transparent, null, new Rect(RenderSize));
        TextView? view = TextView;
        if ((view is null) || !view.VisualLinesValid) {
            return;
        }
        Brush brush = Application.Current.TryFindResource("AccentBrush") as Brush ?? Brushes.SteelBlue;
        HashSet<int> lines = [.. markers.Where(static marker => marker.Comment.Kind == MarkerKind.Bookmark).Select(marker => Document.GetLineByOffset(Math.Min(marker.StartOffset, Document.TextLength)).LineNumber)];
        double dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        foreach (VisualLine line in view.VisualLines) {
            if (!lines.Contains(line.FirstDocumentLine.LineNumber)) {
                continue;
            }
            double y = line.VisualTop - view.VerticalOffset + ((line.TextLines[0].Height - 14) / 2);
            FormattedText icon = new("\uE8A4", System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight, IconFace, 13, brush, dpi);
            drawingContext.DrawText(icon, new Point(2, y));
        }
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e) {
        base.OnMouseLeftButtonDown(e);
        TextView? view = TextView;
        if (view is null) {
            return;
        }
        VisualLine? line = view.GetVisualLineFromVisualTop(e.GetPosition(view).Y + view.VerticalOffset);
        if (line is not null) {
            toggleAtLine(line.FirstDocumentLine.LineNumber);
            e.Handled = true;
        }
    }
}

/// <summary>Scene break lines are dimmed and spaced out; TODO, FIXME, TK and XXX are tagged in blue.</summary>
public sealed partial class ManuscriptMarkupColorizer : DocumentColorizingTransformer {
    private static readonly Brush TaskBackground = Visuals.CreateBrush("#331E7BC4");

    protected override void ColorizeLine(DocumentLine line) {
        string text = CurrentContext.Document.GetText(line);
        if ((text.Length > 0) && SceneParser.IsBreakLine(text)) {
            ChangeLinePart(line.Offset, line.EndOffset, element => {
                element.TextRunProperties.SetForegroundBrush(Visuals.SubtleBrush);
                element.TextRunProperties.SetTypeface(new Typeface(element.TextRunProperties.Typeface.FontFamily, FontStyles.Normal, FontWeights.Bold, FontStretches.Expanded));
            });
            return;
        }
        foreach (Match match in TaskPattern().Matches(text)) {
            ChangeLinePart(line.Offset + match.Index, line.Offset + match.Index + match.Length, element => {
                element.TextRunProperties.SetForegroundBrush(Visuals.MessageBrush);
                element.TextRunProperties.SetBackgroundBrush(TaskBackground);
                element.TextRunProperties.SetTypeface(new Typeface(element.TextRunProperties.Typeface.FontFamily, FontStyles.Normal, FontWeights.Bold, FontStretches.Normal));
            });
        }
    }

    [GeneratedRegex(@"(?<![\p{L}\p{N}])\[?(?:TODO|FIXME|TK|XXX)\]?(?![\p{L}\p{N}])")]
    private static partial Regex TaskPattern();
}

/// <summary>Revision mode, inserted text: green shading with an underline, like Word's track changes.</summary>
public sealed class RevisionRenderer : IBackgroundRenderer {
    private static readonly Brush InsertBrush = Visuals.CreateBrush("#3338A169");
    private static readonly Pen InsertPen = CreatePen("#38A169");

    public IReadOnlyList<RevisionChange> Changes { get; set; } = [];

    public KnownLayer Layer => KnownLayer.Selection;

    public void Draw(TextView textView, DrawingContext drawingContext) {
        if ((Changes.Count == 0) || !textView.VisualLinesValid) {
            return;
        }
        int length = textView.Document.TextLength;
        foreach (RevisionChange change in Changes) {
            if ((change.Length == 0) || (change.End > length)) {
                continue;
            }
            foreach (Rect rect in BackgroundGeometryBuilder.GetRectsForSegment(textView, new TextSegment { StartOffset = change.Offset, Length = change.Length })) {
                drawingContext.DrawRectangle(InsertBrush, null, rect);
                drawingContext.DrawLine(InsertPen, new Point(rect.Left, rect.Bottom - 1), new Point(rect.Right, rect.Bottom - 1));
            }
        }
    }

    private static Pen CreatePen(string color) {
        Pen pen = new(Visuals.CreateBrush(color), 1);
        pen.Freeze();
        return pen;
    }
}

/// <summary>Revision mode, deleted text: shown in place, struck through in red, without being part of the document.</summary>
public sealed class DeletionGenerator : VisualLineElementGenerator {
    private static readonly Brush DeleteBrush = Visuals.CreateBrush("#C53030");
    private static readonly Brush DeleteBackground = Visuals.CreateBrush("#22E53E3E");

    public IReadOnlyList<RevisionChange> Changes { get; set; } = [];

    public override int GetFirstInterestedOffset(int startOffset) {
        int lineEnd = CurrentContext.VisualLine.LastDocumentLine.EndOffset;
        foreach (RevisionChange change in Changes) {
            if ((change.Deleted.Length > 0) && (change.Offset >= startOffset) && (change.Offset <= lineEnd)) {
                return change.Offset;
            }
        }
        return -1;
    }

    public override VisualLineElement? ConstructElement(int offset) {
        RevisionChange? change = Changes.FirstOrDefault(change => (change.Offset == offset) && (change.Deleted.Length > 0));
        if (change is null) {
            return null;
        }
        string text = change.Deleted.Replace("\r\n", " \u21B5 ").Replace('\n', ' ');
        TextBlock block = new() {
            Text = text,
            Foreground = DeleteBrush,
            Background = DeleteBackground,
            TextDecorations = TextDecorations.Strikethrough,
            FontFamily = CurrentContext.GlobalTextRunProperties.Typeface.FontFamily,
            FontSize = CurrentContext.GlobalTextRunProperties.FontRenderingEmSize,
            ToolTip = $"{change.Description}. Right-click to accept or reject."
        };
        return new DeletionElement(block);
    }

    private sealed class DeletionElement(UIElement element) : InlineObjectElement(0, element) {
        public override bool IsWhitespace(int visualColumn) {
            return true;
        }
    }
}