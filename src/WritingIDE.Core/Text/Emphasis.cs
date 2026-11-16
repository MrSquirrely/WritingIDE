using System.Text.RegularExpressions;

using WritingIDE.Core.Analysis;

namespace WritingIDE.Core.Text;

/// <summary>
/// A stretch of emphasised text in a line: *italic* (or _italic_), **bold** or ***bold italic***.
/// <see cref="Start"/> and <see cref="Length"/> include the markers; the content lies between them.
/// </summary>
public readonly record struct EmphasisSpan(int Start, int Length, int ContentStart, int ContentLength, bool Bold, bool Italic) {
    public int End => Start + Length;

    public int ContentEnd => ContentStart + ContentLength;
}

/// <summary>
/// Finds the Markdown-style emphasis a manuscript uses for formatting. Chapters stay plain text, so the markup works
/// with Git, the grammar checker and every exporter; the editor draws it as real bold and italic.
/// </summary>
public static partial class Emphasis {
    public static IReadOnlyList<EmphasisSpan> Find(string line) {
        // A scene break ("***", "* * *") is not emphasis.
        if (SceneParser.IsBreakLine(line)) {
            return [];
        }
        List<EmphasisSpan> spans = [];
        foreach (Match match in Pattern().Matches(line)) {
            (Group content, bool bold, bool italic) = match.Groups["both"].Success ? (match.Groups["both"], true, true)
                : match.Groups["bold"].Success ? (match.Groups["bold"], true, false)
                : match.Groups["italic"].Success ? (match.Groups["italic"], false, true)
                : (match.Groups["under"], false, true);
            spans.Add(new EmphasisSpan(match.Index, match.Length, content.Index, content.Length, bold, italic));
        }
        return spans;
    }

    [GeneratedRegex(@"\*\*\*(?<both>[^*\n]+?)\*\*\*(?!\*)|\*\*(?<bold>[^*\n]+)\*\*|(?<![\w*])\*(?<italic>[^*\s][^*\n]*?)\*(?![\w*])|(?<!\w)_(?<under>[^_\s][^_\n]*?)_(?!\w)")]
    private static partial Regex Pattern();
}

public enum FormatCommand {
    Bold,
    Italic,

    /// <summary>Removes bold and italic.</summary>
    Clear
}

/// <summary>Replace <see cref="Length"/> characters at <see cref="Start"/> with <see cref="Text"/>, then select the given range.</summary>
public readonly record struct FormatEdit(int Start, int Length, string Text, int SelectionStart, int SelectionLength);

/// <summary>
/// Bold and italic for the editor, like Word's Ctrl+B and Ctrl+I, done by adding or removing the emphasis markers
/// around the selection. With no selection the word at the caret is formatted (or, between words, an empty pair of
/// markers is inserted to type into). Selections are widened to whole words, because Markdown emphasis cannot start
/// or end inside a word, and a selection over several paragraphs formats each of them.
/// </summary>
public static class TextFormatting {
    public static FormatEdit Apply(string text, int start, int length, FormatCommand command) {
        start = Math.Clamp(start, 0, text.Length);
        int end = Math.Clamp(start + length, start, text.Length);
        // A selection that ends at the start of a line (a triple-click) stops at the line before.
        while ((end > start) && (text[end - 1] is '\n' or '\r')) {
            end--;
        }
        bool collapsed = end == start;
        int regionStart = LineStart(text, start);
        int regionEnd = LineEnd(text, end);
        System.Text.StringBuilder output = new();
        int? selectionStart = null;
        int selectionEnd = 0;
        bool? targetBold = null;
        bool? targetItalic = null;
        int lineStart = regionStart;
        while (true) {
            int lineEnd = LineEnd(text, lineStart);
            string line = text[lineStart..lineEnd];
            int segmentStart = Math.Max(start, lineStart) - lineStart;
            int segmentEnd = Math.Min(end, lineEnd) - lineStart;
            (string formatted, int contentStart, int contentEnd) = FormatLine(line, segmentStart, segmentEnd, collapsed, command, ref targetBold, ref targetItalic);
            selectionStart ??= regionStart + output.Length + contentStart;
            selectionEnd = regionStart + output.Length + contentEnd;
            output.Append(formatted);
            if (lineEnd >= regionEnd) {
                break;
            }
            int next = text.IndexOf('\n', lineEnd) + 1;
            output.Append(text, lineEnd, next - lineEnd);
            lineStart = next;
        }
        int selection = selectionStart ?? start;
        return new FormatEdit(regionStart, regionEnd - regionStart, output.ToString(), selection, Math.Max(0, selectionEnd - selection));
    }

    /// <summary>
    /// The edit as the smallest insertions and deletions of marker characters, last first, so an editor can apply
    /// them without disturbing comments, bookmarks or revision marks anchored to the words in between.
    /// </summary>
    public static IReadOnlyList<(int Offset, int RemoveLength, string Insert)> ToChanges(string text, FormatEdit edit) {
        string old = text.Substring(edit.Start, edit.Length);
        string updated = edit.Text;
        List<(int Offset, int RemoveLength, string Insert)> changes = [];
        int i = 0;
        int j = 0;
        while ((i < old.Length) || (j < updated.Length)) {
            if ((i < old.Length) && (j < updated.Length) && (old[i] == updated[j])) {
                i++;
                j++;
            }
            else if ((j < updated.Length) && (updated[j] is '*' or '_') && ((i >= old.Length) || (old[i] is not ('*' or '_')))) {
                int start = j;
                while ((j < updated.Length) && (updated[j] == updated[start]) && ((i >= old.Length) || (old[i] != updated[j]))) {
                    j++;
                }
                changes.Add((edit.Start + i, 0, updated[start..j]));
            }
            else if ((i < old.Length) && (old[i] is '*' or '_')) {
                int start = i;
                while ((i < old.Length) && (old[i] == old[start]) && ((j >= updated.Length) || (old[i] != updated[j]))) {
                    i++;
                }
                changes.Add((edit.Start + start, i - start, ""));
            }
            else {
                // Not a marker-only difference: replace the rest in one go.
                changes.Add((edit.Start + i, old.Length - i, updated[j..]));
                break;
            }
        }
        changes.Reverse();
        return changes;
    }

    private static (string Line, int ContentStart, int ContentEnd) FormatLine(string line, int start, int end, bool collapsed, FormatCommand command, ref bool? targetBold, ref bool? targetItalic) {
        if ((line.Trim().Length == 0) || SceneParser.IsBreakLine(line)) {
            return (line, start, end);
        }
        if (command == FormatCommand.Clear) {
            return Clear(line, start, end, collapsed);
        }
        int contentStart = start;
        int contentEnd = end;
        if (collapsed) {
            (contentStart, contentEnd) = WordAt(line, start);
            if (contentStart == contentEnd) {
                // Between words: an empty pair of markers to type into.
                string pair = new('*', command == FormatCommand.Bold ? 2 : 1);
                return (line.Insert(start, pair + pair), start + pair.Length, start + pair.Length);
            }
        }
        else {
            // Emphasis cannot start or end inside a word, so take whole words; never wrap surrounding spaces.
            while ((contentStart > 0) && IsWordChar(line[contentStart - 1]) && (contentStart < line.Length) && IsWordChar(line[contentStart])) {
                contentStart--;
            }
            while ((contentEnd < line.Length) && (contentEnd > 0) && IsWordChar(line[contentEnd - 1]) && IsWordChar(line[contentEnd])) {
                contentEnd++;
            }
            while ((contentStart < contentEnd) && char.IsWhiteSpace(line[contentStart])) {
                contentStart++;
            }
            while ((contentEnd > contentStart) && char.IsWhiteSpace(line[contentEnd - 1])) {
                contentEnd--;
            }
            if (contentStart == contentEnd) {
                return (line, start, end);
            }
        }
        // Markers selected along with the words ("**word**") count as the word's current formatting.
        int level = 0;
        while ((level < 3) && (contentEnd - contentStart > 2) && (line[contentStart] == '*') && (line[contentEnd - 1] == '*')) {
            contentStart++;
            contentEnd--;
            level++;
        }
        int before = 0;
        while ((level + before < 3) && (contentStart - level - before > 0) && (line[contentStart - level - before - 1] == '*')) {
            before++;
        }
        int after = 0;
        while ((level + after < 3) && (contentEnd + level + after < line.Length) && (line[contentEnd + level + after] == '*')) {
            after++;
        }
        level += Math.Min(before, after);
        bool bold = level >= 2;
        bool italic = (level % 2) == 1;
        if (command == FormatCommand.Bold) {
            targetBold ??= !bold;
            bold = targetBold.Value;
        }
        else {
            targetItalic ??= !italic;
            italic = targetItalic.Value;
        }
        string marker = new('*', (bold ? 2 : 0) + (italic ? 1 : 0));
        string formatted = line[..(contentStart - level)] + marker + line[contentStart..contentEnd] + marker + line[(contentEnd + level)..];
        int newStart = contentStart - level + marker.Length;
        return (formatted, newStart, newStart + (contentEnd - contentStart));
    }

    /// <summary>Removes the emphasis around the selection (or around the caret).</summary>
    private static (string Line, int ContentStart, int ContentEnd) Clear(string line, int start, int end, bool collapsed) {
        List<EmphasisSpan> spans = Emphasis.Find(line)
            .Where(span => collapsed ? (span.Start <= start) && (start <= span.End) : (span.Start < end) && (span.End > start))
            .ToList();
        if (spans.Count == 0) {
            return (line, start, end);
        }
        System.Text.StringBuilder output = new();
        int position = 0;
        int first = -1;
        foreach (EmphasisSpan span in spans) {
            output.Append(line, position, span.Start - position);
            if (first < 0) {
                first = output.Length;
            }
            output.Append(line, span.ContentStart, span.ContentLength);
            position = span.End;
        }
        int last = output.Length;
        output.Append(line, position, line.Length - position);
        return (output.ToString(), first, last);
    }

    private static (int Start, int End) WordAt(string line, int position) {
        int start = position;
        int end = position;
        while ((start > 0) && IsWordChar(line[start - 1])) {
            start--;
        }
        while ((end < line.Length) && IsWordChar(line[end])) {
            end++;
        }
        return (start, end);
    }

    private static bool IsWordChar(char c) {
        return char.IsLetterOrDigit(c) || (c is '\'' or '\u2019' or '-');
    }

    private static int LineStart(string text, int position) {
        return position == 0 ? 0 : text.LastIndexOf('\n', position - 1) + 1;
    }

    /// <summary>The end of the line holding <paramref name="position"/>, before any "\r\n".</summary>
    private static int LineEnd(string text, int position) {
        int newline = text.IndexOf('\n', position);
        int end = newline < 0 ? text.Length : newline;
        return (end > position) && (text[end - 1] == '\r') ? end - 1 : end;
    }
}