using System.Text;

using WritingIDE.Core.Model;
using WritingIDE.Core.Search;

namespace WritingIDE.Core.Analysis;

/// <summary>
/// One tracked change in revision mode, positioned in the current text: <see cref="Inserted"/> is the new text at
/// <see cref="Offset"/> (length <see cref="Length"/>, possibly 0) and <see cref="Deleted"/> the baseline text it replaced.
/// </summary>
public sealed record RevisionChange(int Index, int Offset, int Length, string Inserted, string Deleted, int BaselineOffset) {
    public bool IsInsertion => (Inserted.Length > 0) && (Deleted.Length == 0);

    public bool IsDeletion => (Inserted.Length == 0) && (Deleted.Length > 0);

    public int End => Offset + Length;

    public string Description => IsInsertion ? $"Inserted \u201C{Short(Inserted)}\u201D"
        : IsDeletion ? $"Deleted \u201C{Short(Deleted)}\u201D"
        : $"Replaced \u201C{Short(Deleted)}\u201D with \u201C{Short(Inserted)}\u201D";

    private static string Short(string text) {
        string flat = text.Replace('\n', ' ').Replace("\r", "").Trim();
        return flat.Length <= 40 ? flat : flat[..37] + "...";
    }
}

/// <summary>
/// Revision mode ("track changes"): the differences between a chapter's revision baseline and its current text, and
/// accepting (folding a change into the baseline) or rejecting (undoing it in the text) each one.
/// </summary>
public static class RevisionTracker {
    public static IReadOnlyList<RevisionChange> Compare(string baseline, string current) {
        List<RevisionChange> changes = [];
        int currentOffset = 0;
        int baselineOffset = 0;
        StringBuilder inserted = new();
        StringBuilder deleted = new();
        int changeStart = 0;
        int changeBaselineStart = 0;

        void Flush() {
            if ((inserted.Length > 0) || (deleted.Length > 0)) {
                changes.Add(new RevisionChange(changes.Count, changeStart, inserted.Length, inserted.ToString(), deleted.ToString(), changeBaselineStart));
                inserted.Clear();
                deleted.Clear();
            }
        }

        // Raw texts: offsets must match the editor document exactly (line endings included).
        foreach (DiffPiece piece in WordDiff.DiffWords(baseline, current)) {
            if (piece.Kind == DiffKind.Same) {
                Flush();
                currentOffset += piece.Text.Length;
                baselineOffset += piece.Text.Length;
                continue;
            }
            if ((inserted.Length == 0) && (deleted.Length == 0)) {
                changeStart = currentOffset;
                changeBaselineStart = baselineOffset;
            }
            if (piece.Kind == DiffKind.Added) {
                inserted.Append(piece.Text);
                currentOffset += piece.Text.Length;
            }
            else {
                deleted.Append(piece.Text);
                baselineOffset += piece.Text.Length;
            }
        }
        Flush();
        return changes;
    }

    /// <summary>Accepting a change: the baseline takes the new text, so the change is no longer shown.</summary>
    public static string Accept(string baseline, RevisionChange change) {
        string normalized = baseline;
        return normalized[..change.BaselineOffset] + change.Inserted + normalized[(change.BaselineOffset + change.Deleted.Length)..];
    }

    /// <summary>Rejecting a change: the text goes back to the baseline's version of that passage.</summary>
    public static string Reject(string current, RevisionChange change) {
        string normalized = current;
        return normalized[..change.Offset] + change.Deleted + normalized[change.End..];
    }
}

/// <summary>Where a passage of a plot thread sits in the manuscript.</summary>
public sealed record ThreadMarkerLocation(ChapterComment Marker, Chapter Chapter, int ChapterIndex, string Excerpt);

/// <summary>A plot thread's markers in reading order, with what is wrong with it.</summary>
public sealed record ThreadReport(PlotThread Thread, IReadOnlyList<ThreadMarkerLocation> Markers, IReadOnlyList<string> Problems) {
    public int Setups => Markers.Count(static marker => marker.Marker.ThreadRole == ThreadRole.Setup);

    public int Payoffs => Markers.Count(static marker => marker.Marker.ThreadRole == ThreadRole.Payoff);
}

/// <summary>Checks plot threads: setups never paid off, payoffs with nothing set up, payoffs before their setup.</summary>
public static class PlotThreadAnalyzer {
    public static IReadOnlyList<ThreadReport> Analyze(NovelProject project, IReadOnlyList<ChapterText> manuscript) {
        List<ThreadReport> reports = [];
        foreach (PlotThread thread in project.PlotThreads) {
            List<ThreadMarkerLocation> markers = [];
            for (int i = 0; i < manuscript.Count; i++) {
                (Chapter chapter, string text) = manuscript[i];
                foreach (ChapterComment marker in chapter.Comments.Where(marker => (marker.Kind == MarkerKind.PlotThread) && (marker.ThreadId == thread.Id)).OrderBy(static marker => marker.Offset)) {
                    int offset = Math.Clamp(marker.Offset, 0, text.Length);
                    int length = Math.Clamp(marker.Length, 0, text.Length - offset);
                    string excerpt = length > 0 ? text.Substring(offset, length) : marker.AnchorText;
                    markers.Add(new ThreadMarkerLocation(marker, chapter, i, excerpt.Replace('\n', ' ').Trim()));
                }
            }
            List<string> problems = [];
            int firstSetup = markers.FindIndex(static marker => marker.Marker.ThreadRole == ThreadRole.Setup);
            int firstPayoff = markers.FindIndex(static marker => marker.Marker.ThreadRole == ThreadRole.Payoff);
            if (markers.Count == 0) {
                problems.Add("Nothing in the manuscript is marked for this thread yet.");
            }
            else if (thread.Status == PlotThreadStatus.Abandoned) {
                problems.Add($"Abandoned, but still has {markers.Count} marked passage(s): cut them or revive the thread.");
            }
            else {
                if ((firstSetup >= 0) && (firstPayoff < 0)) {
                    problems.Add(thread.Status == PlotThreadStatus.Resolved
                        ? "Marked resolved, but no payoff is marked."
                        : "Set up but never paid off.");
                }
                if ((firstPayoff >= 0) && (firstSetup < 0)) {
                    problems.Add("Paid off, but nothing sets it up: plant it earlier.");
                }
                if ((firstSetup >= 0) && (firstPayoff >= 0) && (firstPayoff < firstSetup)) {
                    problems.Add($"The first payoff ({markers[firstPayoff].Chapter.Title}) comes before the first setup ({markers[firstSetup].Chapter.Title}).");
                }
            }
            reports.Add(new ThreadReport(thread, markers, problems));
        }
        return reports;
    }
}