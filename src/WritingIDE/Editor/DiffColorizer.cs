using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Media;

using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Rendering;

using WritingIDE.Infrastructure;

namespace WritingIDE.Editor;

/// <summary>
/// Colours git's word diff ("--word-diff=plain"): [-removed-] in red with strikethrough, {+added+} in green,
/// hunk headers ("@@ -1,4 +1,5 @@") dimmed. The brackets themselves are faded so the prose stays readable.
/// </summary>
public sealed partial class DiffColorizer : DocumentColorizingTransformer {
    private static readonly Brush AddedBackground = Visuals.CreateBrush("#4D2EA043");
    private static readonly Brush RemovedBackground = Visuals.CreateBrush("#4DE51400");

    protected override void ColorizeLine(DocumentLine line) {
        string text = CurrentContext.Document.GetText(line);
        if (text.StartsWith("@@", StringComparison.Ordinal)) {
            ChangeLinePart(line.Offset, line.EndOffset, element => element.TextRunProperties.SetForegroundBrush(Visuals.SubtleBrush));
            return;
        }
        foreach (Match match in Removed().Matches(text)) {
            Colorize(line.Offset + match.Index, match.Length, RemovedBackground, strike: true);
        }
        foreach (Match match in Added().Matches(text)) {
            Colorize(line.Offset + match.Index, match.Length, AddedBackground, strike: false);
        }
    }

    private void Colorize(int start, int length, Brush background, bool strike) {
        if (length <= 4) {
            return;
        }
        // Markers: "[-" "-]" or "{+" "+}"
        ChangeLinePart(start, start + 2, element => element.TextRunProperties.SetForegroundBrush(Visuals.SubtleBrush));
        ChangeLinePart(start + length - 2, start + length, element => element.TextRunProperties.SetForegroundBrush(Visuals.SubtleBrush));
        ChangeLinePart(start + 2, start + length - 2, element => {
            element.TextRunProperties.SetBackgroundBrush(background);
            if (strike) {
                element.TextRunProperties.SetTextDecorations(TextDecorations.Strikethrough);
            }
        });
    }

    [GeneratedRegex(@"\[-.*?-\]")]
    private static partial Regex Removed();

    [GeneratedRegex(@"\{\+.*?\+\}")]
    private static partial Regex Added();
}