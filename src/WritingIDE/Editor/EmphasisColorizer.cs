using System.Windows;
using System.Windows.Media;

using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Rendering;

using WritingIDE.Core.Text;
using WritingIDE.Infrastructure;

namespace WritingIDE.Editor;

/// <summary>
/// Draws *italic*, **bold** and ***bold italic*** as real italic and bold, and fades the asterisks so the prose reads
/// cleanly. The markers stay in the text (they are what the exporters and Git see).
/// </summary>
public sealed class EmphasisColorizer : DocumentColorizingTransformer {
    protected override void ColorizeLine(DocumentLine line) {
        if (line.Length == 0) {
            return;
        }
        string text = CurrentContext.Document.GetText(line);
        foreach (EmphasisSpan span in Emphasis.Find(text)) {
            int start = line.Offset + span.Start;
            int contentStart = line.Offset + span.ContentStart;
            int contentEnd = line.Offset + span.ContentEnd;
            int end = line.Offset + span.End;
            ChangeLinePart(contentStart, contentEnd, element => {
                Typeface typeface = element.TextRunProperties.Typeface;
                element.TextRunProperties.SetTypeface(new Typeface(
                    typeface.FontFamily,
                    span.Italic ? FontStyles.Italic : typeface.Style,
                    span.Bold ? FontWeights.Bold : typeface.Weight,
                    typeface.Stretch));
            });
            ChangeLinePart(start, contentStart, Fade);
            ChangeLinePart(contentEnd, end, Fade);
        }
    }

    private static void Fade(VisualLineElement element) {
        element.TextRunProperties.SetForegroundBrush(Visuals.SubtleBrush);
    }
}