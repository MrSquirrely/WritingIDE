using System.Text;

namespace WritingIDE.Core.Search;

public enum DiffKind {
    Same,
    Removed,
    Added
}

/// <summary>A run of tokens that is unchanged, removed or added.</summary>
public readonly record struct DiffPiece(DiffKind Kind, string Text);

/// <summary>
/// Word-level comparison for prose: paragraphs are matched first, then changed paragraphs are compared word by word.
/// The rendered form uses Git's word-diff markers ([-removed-] {+added+}) so the same colouring is used everywhere.
/// </summary>
public static class WordDiff {
    /// <summary>Beyond this many edits a block is shown as replaced wholesale (keeps memory bounded).</summary>
    private const int MaxEdits = 4000;

    /// <summary>Upper bound on the integers kept for backtracking (about 80 MB).</summary>
    private const long MaxTraceCells = 20_000_000;

    /// <summary>
    /// The changed paragraphs of <paramref name="newText"/> with their changes marked, each under an "@@ line N @@" header.
    /// Empty when the texts are identical.
    /// </summary>
    public static string Render(string oldText, string newText) {
        string[] oldLines = Normalize(oldText).Split('\n');
        string[] newLines = Normalize(newText).Split('\n');
        IReadOnlyList<(DiffKind Kind, int OldIndex, int NewIndex)> lineDiff = DiffIndexes(oldLines, newLines);

        StringBuilder output = new();
        int i = 0;
        while (i < lineDiff.Count) {
            if (lineDiff[i].Kind == DiffKind.Same) {
                i++;
                continue;
            }
            List<string> removed = [];
            List<string> added = [];
            int firstNewLine = -1;
            while ((i < lineDiff.Count) && (lineDiff[i].Kind != DiffKind.Same)) {
                (DiffKind kind, int oldIndex, int newIndex) = lineDiff[i];
                if (kind == DiffKind.Removed) {
                    removed.Add(oldLines[oldIndex]);
                }
                else {
                    added.Add(newLines[newIndex]);
                    if (firstNewLine < 0) {
                        firstNewLine = newIndex;
                    }
                }
                i++;
            }
            string before = string.Join("\n", removed).Trim('\n');
            string after = string.Join("\n", added).Trim('\n');
            if ((before.Length == 0) && (after.Length == 0)) {
                continue;
            }
            int line = firstNewLine >= 0 ? firstNewLine + 1 : (i < lineDiff.Count ? lineDiff[i].NewIndex + 1 : newLines.Length);
            if (output.Length > 0) {
                output.Append('\n');
            }
            output.Append("@@ line ").Append(line).Append(" @@\n");
            output.Append(RenderPieces(DiffWords(before, after))).Append('\n');
        }
        return output.ToString().TrimEnd('\n');
    }

    /// <summary>Word-by-word differences between two texts.</summary>
    public static IReadOnlyList<DiffPiece> DiffWords(string oldText, string newText) {
        List<string> a = Tokenize(oldText);
        List<string> b = Tokenize(newText);
        List<DiffPiece> pieces = [];
        // Runs are built in one buffer: adding token by token to a string copied the whole run each time, which made
        // a long unchanged chapter quadratic.
        StringBuilder run = new();
        DiffKind? runKind = null;
        foreach ((DiffKind kind, int oldIndex, int newIndex) in DiffIndexes(a, b)) {
            if ((runKind is not null) && (runKind != kind)) {
                pieces.Add(new DiffPiece(runKind.Value, run.ToString()));
                run.Clear();
            }
            runKind = kind;
            run.Append(kind == DiffKind.Added ? b[newIndex] : a[oldIndex]);
        }
        if (runKind is not null) {
            pieces.Add(new DiffPiece(runKind.Value, run.ToString()));
        }
        return Tidy(pieces);
    }

    /// <summary>Renders pieces with [-..-] and {+..+} markers, never letting a marker span a line break.</summary>
    public static string RenderPieces(IReadOnlyList<DiffPiece> pieces) {
        StringBuilder builder = new();
        foreach (DiffPiece piece in pieces) {
            if (piece.Kind == DiffKind.Same) {
                builder.Append(piece.Text);
                continue;
            }
            (string open, string close) = piece.Kind == DiffKind.Removed ? ("[-", "-]") : ("{+", "+}");
            string[] parts = piece.Text.Split('\n');
            for (int index = 0; index < parts.Length; index++) {
                if (index > 0) {
                    builder.Append('\n');
                }
                string part = parts[index];
                if (part.Trim().Length == 0) {
                    builder.Append(piece.Kind == DiffKind.Added ? part : "");
                }
                else {
                    builder.Append(open).Append(part).Append(close);
                }
            }
        }
        return builder.ToString();
    }

    /// <summary>
    /// Myers' O(ND) difference algorithm over any token lists. Returns, in order, every token as Same (both indexes),
    /// Removed (old index) or Added (new index).
    /// </summary>
    public static IReadOnlyList<(DiffKind Kind, int OldIndex, int NewIndex)> DiffIndexes<T>(IReadOnlyList<T> a, IReadOnlyList<T> b) where T : IEquatable<T> {
        List<(DiffKind, int, int)> result = [];
        int prefix = 0;
        while ((prefix < a.Count) && (prefix < b.Count) && a[prefix].Equals(b[prefix])) {
            result.Add((DiffKind.Same, prefix, prefix));
            prefix++;
        }
        int suffix = 0;
        while ((suffix < a.Count - prefix) && (suffix < b.Count - prefix) && a[a.Count - 1 - suffix].Equals(b[b.Count - 1 - suffix])) {
            suffix++;
        }
        int n = a.Count - prefix - suffix;
        int m = b.Count - prefix - suffix;
        result.AddRange(Myers(a, b, prefix, n, m));
        for (int k = 0; k < suffix; k++) {
            result.Add((DiffKind.Same, prefix + n + k, prefix + m + k));
        }
        return result;
    }

    private static List<(DiffKind, int, int)> Myers<T>(IReadOnlyList<T> a, IReadOnlyList<T> b, int start, int n, int m) where T : IEquatable<T> {
        List<(DiffKind, int, int)> edits = [];
        if ((n == 0) || (m == 0)) {
            for (int i = 0; i < n; i++) {
                edits.Add((DiffKind.Removed, start + i, -1));
            }
            for (int j = 0; j < m; j++) {
                edits.Add((DiffKind.Added, -1, start + j));
            }
            return edits;
        }
        int max = n + m;
        int offset = max;
        int[] v = new int[(2 * max) + 2];
        List<int[]> trace = [];
        int found = -1;
        for (int d = 0; (d <= max) && (d <= MaxEdits) && (((long)(d + 1) * v.Length) <= MaxTraceCells); d++) {
            trace.Add((int[])v.Clone());
            for (int k = -d; k <= d; k += 2) {
                int x = ((k == -d) || ((k != d) && (v[offset + k - 1] < v[offset + k + 1]))) ? v[offset + k + 1] : v[offset + k - 1] + 1;
                int y = x - k;
                while ((x < n) && (y < m) && a[start + x].Equals(b[start + y])) {
                    x++;
                    y++;
                }
                v[offset + k] = x;
                if ((x >= n) && (y >= m)) {
                    found = d;
                    break;
                }
            }
            if (found >= 0) {
                break;
            }
        }
        if (found < 0) {
            // Too different: show the whole block as replaced.
            return Myers(a, b, start, n, 0).Concat(Myers(a, b, start, 0, m)).ToList();
        }

        // Walk the trace backwards to recover the edit script.
        List<(DiffKind, int, int)> reversed = [];
        int cx = n;
        int cy = m;
        for (int d = found; d > 0; d--) {
            int[] previous = trace[d];
            int k = cx - cy;
            int previousK = ((k == -d) || ((k != d) && (previous[offset + k - 1] < previous[offset + k + 1]))) ? k + 1 : k - 1;
            int previousX = previous[offset + previousK];
            int previousY = previousX - previousK;
            while ((cx > previousX) && (cy > previousY)) {
                cx--;
                cy--;
                reversed.Add((DiffKind.Same, start + cx, start + cy));
            }
            if (cx == previousX) {
                cy--;
                reversed.Add((DiffKind.Added, -1, start + cy));
            }
            else {
                cx--;
                reversed.Add((DiffKind.Removed, start + cx, -1));
            }
        }
        while ((cx > 0) && (cy > 0)) {
            cx--;
            cy--;
            reversed.Add((DiffKind.Same, start + cx, start + cy));
        }
        reversed.Reverse();
        return reversed;
    }

    /// <summary>Words and the whitespace/punctuation between them, so a diff never splits a word.</summary>
    private static List<string> Tokenize(string text) {
        List<string> tokens = [];
        int i = 0;
        while (i < text.Length) {
            int start = i;
            if (char.IsLetterOrDigit(text[i])) {
                while ((i < text.Length) && (char.IsLetterOrDigit(text[i]) || (((text[i] == '\'') || (text[i] == '\u2019') || (text[i] == '-')) && (i + 1 < text.Length) && char.IsLetterOrDigit(text[i + 1])))) {
                    i++;
                }
            }
            else if (char.IsWhiteSpace(text[i])) {
                while ((i < text.Length) && char.IsWhiteSpace(text[i])) {
                    i++;
                }
            }
            else {
                i++;
            }
            tokens.Add(text[start..i]);
        }
        return tokens;
    }

    /// <summary>Merges "removed added removed added" around single spaces into one removed and one added run, which reads better.</summary>
    private static List<DiffPiece> Tidy(List<DiffPiece> pieces) {
        List<DiffPiece> result = [];
        int i = 0;
        while (i < pieces.Count) {
            if (pieces[i].Kind == DiffKind.Same) {
                result.Add(pieces[i]);
                i++;
                continue;
            }
            StringBuilder removed = new();
            StringBuilder added = new();
            while (i < pieces.Count) {
                DiffPiece piece = pieces[i];
                bool bridge = (piece.Kind == DiffKind.Same) && (piece.Text.Length <= 1) && string.IsNullOrWhiteSpace(piece.Text) && !piece.Text.Contains('\n')
                    && (i + 1 < pieces.Count) && (pieces[i + 1].Kind != DiffKind.Same);
                if (piece.Kind == DiffKind.Removed) {
                    removed.Append(piece.Text);
                }
                else if (piece.Kind == DiffKind.Added) {
                    added.Append(piece.Text);
                }
                else if (bridge) {
                    removed.Append(piece.Text);
                    added.Append(piece.Text);
                }
                else {
                    break;
                }
                i++;
            }
            if (removed.Length > 0) {
                result.Add(new DiffPiece(DiffKind.Removed, removed.ToString()));
            }
            if (added.Length > 0) {
                result.Add(new DiffPiece(DiffKind.Added, added.ToString()));
            }
        }
        return result;
    }

    private static string Normalize(string text) {
        return text.Replace("\r\n", "\n").Replace('\r', '\n');
    }
}