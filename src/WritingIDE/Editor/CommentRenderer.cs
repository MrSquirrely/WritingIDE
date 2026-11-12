using System.Windows;
using System.Windows.Media;

using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Rendering;

using WritingIDE.Core.Model;

namespace WritingIDE.Editor;

/// <summary>The live position of a comment in the editor; AvalonEdit moves it as the text around it is edited.</summary>
public sealed class CommentSegment(ChapterComment comment) : TextSegment {
    public ChapterComment Comment { get; } = comment;
}

/// <summary>Highlights commented text, like Word's comment shading; the comment being edited is shaded more strongly.</summary>
public sealed class CommentRenderer(TextSegmentCollection<CommentSegment> segments) : IBackgroundRenderer {
    // Selection layer: above the current-line highlight (which would cover the shading), still under the text.
    public KnownLayer Layer => KnownLayer.Selection;

    public bool IsEnabled { get; set; } = true;

    public ChapterComment? ActiveComment { get; set; }

    public void Draw(TextView textView, DrawingContext drawingContext) {
        if (!IsEnabled || !textView.VisualLinesValid || (textView.VisualLines.Count == 0)) {
            return;
        }
        Brush normal = Resource("CommentHighlightBrush");
        Brush active = Resource("CommentActiveHighlightBrush");
        int viewStart = textView.VisualLines[0].FirstDocumentLine.Offset;
        int viewEnd = textView.VisualLines[^1].LastDocumentLine.EndOffset;
        foreach (CommentSegment segment in segments.FindOverlappingSegments(viewStart, viewEnd - viewStart)) {
            if (segment.Comment.IsResolved) {
                continue;
            }
            Brush brush = segment.Comment == ActiveComment ? active : normal;
            if (segment.Length == 0) {
                // A comment on a point (its text was deleted): a small marker.
                TextViewPosition position = new(textView.Document.GetLocation(segment.StartOffset));
                Point top = textView.GetVisualPosition(position, VisualYPosition.LineTop) - textView.ScrollOffset;
                Point bottom = textView.GetVisualPosition(position, VisualYPosition.LineBottom) - textView.ScrollOffset;
                drawingContext.DrawRectangle(active, null, new Rect(top.X - 1, top.Y, 3, Math.Max(1, bottom.Y - top.Y)));
                continue;
            }
            BackgroundGeometryBuilder builder = new() { AlignToWholePixels = true, CornerRadius = 2 };
            builder.AddSegment(textView, segment);
            Geometry? geometry = builder.CreateGeometry();
            if (geometry is not null) {
                drawingContext.DrawGeometry(brush, null, geometry);
            }
        }
    }

    private static Brush Resource(string key) {
        return Application.Current.TryFindResource(key) as Brush ?? Brushes.Transparent;
    }
}