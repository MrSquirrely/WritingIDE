using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;

using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Rendering;

using WritingIDE.Core.Model;
using WritingIDE.Infrastructure;

namespace WritingIDE.Editor;

/// <summary>What a name lens says about one story element, as of the chapter it is shown in.</summary>
public sealed record NameLensInfo(StoryEntity Entity, int References, int Chapters, Chapter? LastSeenIn, int ChaptersSince) {
    public bool IsFirstAppearance => LastSeenIn is null;
}

/// <summary>
/// CodeLens for names: a small line of grey text above the first mention of each story element in a chapter,
/// with its reference count, how many chapters it is in, and when it was last seen (or that this is its first appearance).
/// Lenses are zero-length inline elements as wide as the text area, so the paragraph's text wraps onto the line below.
/// </summary>
public sealed class NameLensGenerator(Func<IReadOnlyList<NameLensInfo>, double, FrameworkElement> createLens) : VisualLineElementGenerator {
    private Dictionary<int, IReadOnlyList<NameLensInfo>> _lensesByLine = [];

    public bool HasLenses => _lensesByLine.Count > 0;

    public void SetLenses(Dictionary<int, IReadOnlyList<NameLensInfo>> lensesByLine) {
        _lensesByLine = lensesByLine;
    }

    public override int GetFirstInterestedOffset(int startOffset) {
        DocumentLine line = CurrentContext.VisualLine.FirstDocumentLine;
        return (startOffset == line.Offset) && _lensesByLine.ContainsKey(line.LineNumber) ? startOffset : -1;
    }

    public override VisualLineElement? ConstructElement(int offset) {
        DocumentLine line = CurrentContext.VisualLine.FirstDocumentLine;
        if (!_lensesByLine.TryGetValue(line.LineNumber, out IReadOnlyList<NameLensInfo>? lenses)) {
            return null;
        }
        double width = Math.Max(120, CurrentContext.TextView.ActualWidth - 4);
        return new LensElement(createLens(lenses, width));
    }

    /// <summary>Zero document length: the caret and selection skip over it, and it never changes the text.</summary>
    private sealed class LensElement(UIElement element) : InlineObjectElement(0, element) {
        public override bool IsWhitespace(int visualColumn) {
            return true;
        }
    }

    /// <summary>Builds the lens line: "[icon] Kael Duskmantle \u00B7 42 references \u00B7 12 chapters \u00B7 last seen in Chapter 3 (2 chapters ago)".</summary>
    public static FrameworkElement CreateLensVisual(IReadOnlyList<NameLensInfo> lenses, double width, Action<StoryEntity> openEntity, Action<StoryEntity> findReferences, Action<Chapter> openChapter) {
        WrapPanel panel = new() { Width = width, Margin = new Thickness(0, 2, 0, 0), Cursor = Cursors.Arrow, ClipToBounds = true, MaxHeight = 18 };
        panel.SetValue(TextElement.FontFamilyProperty, new FontFamily("Segoe UI"));
        panel.SetValue(TextElement.FontSizeProperty, 11.0);
        bool firstEntity = true;
        foreach (NameLensInfo lens in lenses) {
            if (!firstEntity) {
                panel.Children.Add(Plain("    |    "));
            }
            firstEntity = false;
            TextBlock icon = new() { Text = lens.Entity.KindInfo.Glyph, FontFamily = new FontFamily("Segoe MDL2 Assets"), FontSize = 10, Foreground = Visuals.GetKindBrush(lens.Entity.Kind), Margin = new Thickness(0, 2, 4, 0) };
            panel.Children.Add(icon);
            panel.Children.Add(Link(lens.Entity.Name, $"Go to {lens.Entity.KindInfo.DisplayName.ToLowerInvariant()} (F12)", () => openEntity(lens.Entity)));
            panel.Children.Add(Plain("  \u00B7  "));
            panel.Children.Add(Link($"{lens.References} reference{(lens.References == 1 ? "" : "s")}", "Find All References (Shift+F12)", () => findReferences(lens.Entity)));
            panel.Children.Add(Plain($"  \u00B7  {lens.Chapters} chapter{(lens.Chapters == 1 ? "" : "s")}  \u00B7  "));
            if (lens.LastSeenIn is Chapter previous) {
                string ago = lens.ChaptersSince <= 1 ? "previous chapter" : $"{lens.ChaptersSince} chapters ago";
                panel.Children.Add(Link($"last seen in {previous.Title} ({ago})", "Open that chapter", () => openChapter(previous)));
            }
            else {
                TextBlock first = Plain("\u2726 first appearance");
                first.SetResourceReference(TextBlock.ForegroundProperty, "AccentBrush");
                panel.Children.Add(first);
            }
        }
        return panel;
    }

    private static TextBlock Plain(string text) {
        TextBlock block = new() { Text = text };
        block.SetResourceReference(TextBlock.ForegroundProperty, "LensBrush");
        return block;
    }

    /// <summary>A clickable piece of the lens. ManuscriptEditor runs the Tag action on mouse down, before the caret moves.</summary>
    private static TextBlock Link(string text, string toolTip, Action action) {
        TextBlock block = Plain(text);
        block.Tag = action;
        block.ToolTip = toolTip;
        block.Cursor = Cursors.Hand;
        block.MouseEnter += (_, _) => block.TextDecorations = TextDecorations.Underline;
        block.MouseLeave += (_, _) => block.TextDecorations = null;
        return block;
    }
}