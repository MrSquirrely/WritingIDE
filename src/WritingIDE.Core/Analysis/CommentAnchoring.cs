using WritingIDE.Core.Model;

namespace WritingIDE.Core.Analysis;

/// <summary>Finds a comment's text again after the chapter changed while the comment was not being tracked live.</summary>
public static class CommentAnchoring {
    /// <summary>
    /// Moves the comment onto its anchor text (the occurrence nearest its old offset). Returns false, and marks the
    /// comment orphaned, when the anchor text no longer exists; the comment then stays near its old position.
    /// </summary>
    public static bool Reanchor(string text, ChapterComment comment) {
        int offset = Math.Clamp(comment.Offset, 0, text.Length);
        if (comment.AnchorText.Length == 0) {
            comment.Offset = offset;
            comment.Length = 0;
            comment.IsOrphaned = false;
            return true;
        }
        if ((offset + comment.AnchorText.Length <= text.Length) && (string.CompareOrdinal(text, offset, comment.AnchorText, 0, comment.AnchorText.Length) == 0)) {
            comment.Offset = offset;
            comment.Length = comment.AnchorText.Length;
            comment.IsOrphaned = false;
            return true;
        }
        int best = -1;
        int index = text.IndexOf(comment.AnchorText, StringComparison.Ordinal);
        while (index >= 0) {
            if ((best < 0) || (Math.Abs(index - offset) < Math.Abs(best - offset))) {
                best = index;
            }
            index = text.IndexOf(comment.AnchorText, index + 1, StringComparison.Ordinal);
        }
        if (best < 0) {
            comment.Offset = offset;
            comment.Length = 0;
            comment.IsOrphaned = true;
            return false;
        }
        comment.Offset = best;
        comment.Length = comment.AnchorText.Length;
        comment.IsOrphaned = false;
        return true;
    }
}