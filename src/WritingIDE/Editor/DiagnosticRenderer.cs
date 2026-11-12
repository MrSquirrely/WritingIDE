using System.Windows;
using System.Windows.Media;

using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Rendering;

using WritingIDE.Core.Grammar;

namespace WritingIDE.Editor;

/// <summary>A diagnostic anchored in the document; its offsets follow edits automatically.</summary>
public sealed class DiagnosticMarker(GrammarDiagnostic diagnostic) : TextSegment {
    public GrammarDiagnostic Diagnostic { get; } = diagnostic;
}

/// <summary>Draws squiggles under diagnostics: red for errors, green for warnings, dotted blue for messages.</summary>
public sealed class DiagnosticRenderer : IBackgroundRenderer {
    private static readonly Pen ErrorPen = CreatePen(Color.FromRgb(0xE5, 0x14, 0x00), null);
    private static readonly Pen WarningPen = CreatePen(Color.FromRgb(0x2E, 0x9E, 0x3A), null);
    private static readonly Pen MessagePen = CreatePen(Color.FromRgb(0x4A, 0x8F, 0xD9), DashStyles.Dot);

    private readonly TextSegmentCollection<DiagnosticMarker> _markers;

    public DiagnosticRenderer(TextDocument document) {
        _markers = new TextSegmentCollection<DiagnosticMarker>(document);
    }

    public KnownLayer Layer => KnownLayer.Selection;

    public void SetDiagnostics(IEnumerable<GrammarDiagnostic> diagnostics, int documentLength) {
        _markers.Clear();
        foreach (GrammarDiagnostic diagnostic in diagnostics) {
            if ((diagnostic.Offset < 0) || (diagnostic.End > documentLength)) {
                continue;
            }
            _markers.Add(new DiagnosticMarker(diagnostic) { StartOffset = diagnostic.Offset, Length = diagnostic.Length });
        }
    }

    /// <summary>Markers that contain the offset (touching the end counts, so the caret right after a word finds it).</summary>
    public IReadOnlyList<DiagnosticMarker> GetMarkersAt(int offset) {
        return _markers.FindSegmentsContaining(offset)
            .OrderByDescending(static marker => marker.Diagnostic.Severity)
            .ToList();
    }

    public void Draw(TextView textView, DrawingContext drawingContext) {
        if (!textView.VisualLinesValid || (textView.VisualLines.Count == 0)) {
            return;
        }
        int viewStart = textView.VisualLines[0].FirstDocumentLine.Offset;
        int viewEnd = textView.VisualLines[^1].LastDocumentLine.EndOffset;
        foreach (DiagnosticMarker marker in _markers.FindOverlappingSegments(viewStart, viewEnd - viewStart)) {
            Pen pen = marker.Diagnostic.Severity switch {
                DiagnosticSeverity.Error => ErrorPen,
                DiagnosticSeverity.Warning => WarningPen,
                _ => MessagePen
            };
            foreach (Rect rect in BackgroundGeometryBuilder.GetRectsForSegment(textView, marker)) {
                if (marker.Diagnostic.Severity == DiagnosticSeverity.Message) {
                    double y = rect.Bottom - 1;
                    drawingContext.DrawLine(pen, new Point(rect.Left, y), new Point(rect.Right, y));
                }
                else {
                    drawingContext.DrawGeometry(null, pen, CreateSquiggle(rect));
                }
            }
        }
    }

    private static StreamGeometry CreateSquiggle(Rect rect) {
        const double Step = 2.0;
        StreamGeometry geometry = new();
        using (StreamGeometryContext context = geometry.Open()) {
            double x = rect.Left;
            double bottom = rect.Bottom - 0.5;
            context.BeginFigure(new Point(x, bottom), false, false);
            bool up = true;
            while (x < rect.Right) {
                x = Math.Min(x + Step, rect.Right);
                context.LineTo(new Point(x, up ? bottom - Step : bottom), true, true);
                up = !up;
            }
        }
        geometry.Freeze();
        return geometry;
    }

    private static Pen CreatePen(Color color, DashStyle? dashStyle) {
        Pen pen = new(new SolidColorBrush(color), dashStyle is null ? 1 : 1.6);
        if (dashStyle is not null) {
            pen.DashStyle = dashStyle;
        }
        pen.Freeze();
        return pen;
    }
}