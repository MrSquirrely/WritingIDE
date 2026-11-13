using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Media;

using WritingIDE.Core.Model;
using WritingIDE.Core.Persistence;
using WritingIDE.Editor;
using WritingIDE.Infrastructure;
using WritingIDE.Views;

namespace WritingIDE.ViewModels;

/// <summary>What a file in a commit is to the writer, which decides its name, its place in the list and how it is shown.</summary>
public enum CommitFileKind {
    Chapter,
    RevisionBaseline,
    Snapshot,
    Project,
    Other
}

/// <summary>A file changed by a commit, named by its chapter where possible.</summary>
public sealed class CommitFileItem(CommitFileDiff file, string displayName, CommitFileKind kind, int order) {
    public CommitFileDiff File { get; } = file;

    public string DisplayName { get; } = displayName;

    public CommitFileKind Kind { get; } = kind;

    /// <summary>Chapters in manuscript order first, then the other files.</summary>
    public int Order { get; } = order;

    /// <summary>The line of this file's header in the commit document's text (1-based), set when the text is built.</summary>
    public int Line { get; set; }

    public string StatusLetter => File.Status switch {
        CommitFileStatus.Added => "A",
        CommitFileStatus.Deleted => "D",
        CommitFileStatus.Renamed => "R",
        _ => "M"
    };

    public string StatusDescription => File.Status switch {
        CommitFileStatus.Added => "added",
        CommitFileStatus.Deleted => "deleted",
        CommitFileStatus.Renamed => $"renamed from {File.OldPath}",
        _ => "modified"
    };

    public Brush StatusBrush => File.Status switch {
        CommitFileStatus.Added => Visuals.CreateBrush("#388A34"),
        CommitFileStatus.Deleted => Visuals.ErrorBrush,
        CommitFileStatus.Renamed => Visuals.MessageBrush,
        _ => Visuals.WarningBrush
    };

    /// <summary>"+120 -34" (words), or "" for files whose words are not counted.</summary>
    public string WordSummary => (Kind is CommitFileKind.Snapshot or CommitFileKind.Project) || File.IsBinary || ((File.WordsAdded == 0) && (File.WordsRemoved == 0))
        ? ""
        : $"+{File.WordsAdded.ToString("N0", CultureInfo.CurrentCulture)} \u2212{File.WordsRemoved.ToString("N0", CultureInfo.CurrentCulture)}";
}

/// <summary>
/// A commit from the Git History list: its message, the files it changed, and a word diff of each, one after another,
/// so a writer can read what a commit did to the manuscript.
/// </summary>
public sealed partial class CommitDocumentViewModel : DocumentViewModel {
    public CommitDocumentViewModel(MainViewModel shell, CommitDetails commit, string repositoryRoot) : base(shell) {
        Commit = commit;
        NovelProject? project = shell.Project;
        List<Chapter> chapters = shell.OrderedChapters.ToList();
        Files = commit.Files.Select(file => Describe(project, chapters, repositoryRoot, file))
            .OrderBy(static item => item.Order)
            .ThenBy(static item => item.File.Path, StringComparer.OrdinalIgnoreCase)
            .ToList();
        Text = BuildText(Files);
    }

    public CommitDetails Commit { get; }

    public IReadOnlyList<CommitFileItem> Files { get; }

    /// <summary>Every file's diff under its own header, for the read-only editor.</summary>
    public string Text { get; }

    public override string Title => $"Commit {Commit.ShortHash}";

    public override string ToolTip => $"{Commit.ShortHash}: {Commit.Subject}";

    public override string Glyph => "\uE81C";

    public override Brush GlyphBrush => Visuals.MessageBrush;

    /// <summary>"b8c8965 - James - Friday, 13 November 2026 14:02 - 3 files - +120 -34 words".</summary>
    public string Summary {
        get {
            List<string> parts = [Commit.ShortHash, Commit.Author];
            if (Commit.Date is DateTimeOffset date) {
                parts.Add(date.ToLocalTime().ToString("f", CultureInfo.CurrentCulture));
            }
            parts.Add($"{Files.Count} file{(Files.Count == 1 ? "" : "s")}");
            // Only chapter words: the project file is JSON, and baselines repeat a chapter's text.
            List<CommitFileItem> chapters = Files.Where(static item => item.Kind == CommitFileKind.Chapter).ToList();
            if (chapters.Count > 0) {
                int added = chapters.Sum(static item => item.File.WordsAdded);
                int removed = chapters.Sum(static item => item.File.WordsRemoved);
                parts.Add($"+{added.ToString("N0", CultureInfo.CurrentCulture)} \u2212{removed.ToString("N0", CultureInfo.CurrentCulture)} words in chapters");
            }
            return string.Join("  \u00B7  ", parts);
        }
    }

    protected override FrameworkElement CreateView() {
        return new CommitView(this);
    }

    private static CommitFileItem Describe(NovelProject? project, List<Chapter> chapters, string root, CommitFileDiff file) {
        int otherOrder = chapters.Count + 10;
        if (project is null) {
            return new CommitFileItem(file, file.Path, CommitFileKind.Other, otherOrder);
        }
        string fullPath = Path.GetFullPath(Path.Combine(root, file.Path));
        for (int index = 0; index < chapters.Count; index++) {
            Chapter chapter = chapters[index];
            if ((chapter.FileName.Length > 0)
                && string.Equals(Path.GetFullPath(Path.Combine(project.DirectoryPath, chapter.FileName)), fullPath, StringComparison.OrdinalIgnoreCase)) {
                return new CommitFileItem(file, project.GetDisplayName(chapter), CommitFileKind.Chapter, index);
            }
        }
        if (string.Equals(fullPath, Path.GetFullPath(project.FilePath), StringComparison.OrdinalIgnoreCase)) {
            return new CommitFileItem(file, "Project file (story elements, bible, chapter list)", CommitFileKind.Project, chapters.Count + 3);
        }
        // Snapshots/<chapter id>/<time>.json and Revisions/<chapter id>.txt belong to a chapter.
        string relative = Path.GetRelativePath(project.DirectoryPath, fullPath);
        string[] parts = relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if ((parts.Length == 3) && string.Equals(parts[0], SnapshotStore.FolderName, StringComparison.OrdinalIgnoreCase)) {
            return new CommitFileItem(file, $"Snapshot of {ChapterName(project, parts[1])}", CommitFileKind.Snapshot, chapters.Count + 2);
        }
        if ((parts.Length == 2) && string.Equals(parts[0], RevisionStore.FolderName, StringComparison.OrdinalIgnoreCase)) {
            return new CommitFileItem(file, $"Revision baseline of {ChapterName(project, Path.GetFileNameWithoutExtension(parts[1]))}", CommitFileKind.RevisionBaseline, chapters.Count + 1);
        }
        return new CommitFileItem(file, file.Path, CommitFileKind.Other, otherOrder);
    }

    private static string ChapterName(NovelProject project, string id) {
        Chapter? chapter = Guid.TryParse(id, out Guid chapterId) ? project.Chapters.FirstOrDefault(chapter => chapter.Id == chapterId) : null;
        return chapter is null ? "a deleted chapter" : project.GetDisplayName(chapter);
    }

    private static string BuildText(IReadOnlyList<CommitFileItem> files) {
        if (files.Count == 0) {
            return "This commit did not change any files.";
        }
        StringBuilder text = new();
        int line = 1;
        void Append(string value) {
            text.Append(value).Append(Environment.NewLine);
            line++;
        }
        foreach (CommitFileItem item in files) {
            if (line > 1) {
                Append("");
            }
            item.Line = line;
            string words = item.WordSummary.Length > 0 ? $"  \u00B7  {item.WordSummary} words" : "";
            Append($"{DiffColorizer.FileHeader}{item.DisplayName}  \u00B7  {item.StatusDescription}{words}");
            foreach (string body in Body(item)) {
                Append(body);
            }
        }
        return text.ToString().TrimEnd();
    }

    private static IEnumerable<string> Body(CommitFileItem item) {
        CommitFileDiff file = item.File;
        if (file.IsBinary) {
            yield return "(A binary file; its contents are not shown.)";
            yield break;
        }
        if (item.Kind == CommitFileKind.Snapshot) {
            // The snapshot holds a whole chapter; open the chapter's snapshots to compare them.
            yield return file.Status == CommitFileStatus.Deleted ? "(A snapshot was deleted.)" : "(A snapshot was saved. Compare it from the chapter's Snapshots menu.)";
            yield break;
        }
        if (file.Diff.Length == 0) {
            yield return file.Status == CommitFileStatus.Renamed ? "(Renamed only; the text did not change.)"
                : file.Status == CommitFileStatus.Added ? "(An empty file.)" : "(No changes to the text.)";
            yield break;
        }
        bool firstHunk = true;
        foreach (string line in file.Diff.Split('\n')) {
            Match hunk = HunkHeader().Match(line);
            if (hunk.Success) {
                // "@@ -12,3 +12,4 @@" means nothing to a writer; say where the change is instead.
                string start = hunk.Groups["new"].Value != "0" ? hunk.Groups["new"].Value : hunk.Groups["old"].Value;
                if (!firstHunk) {
                    yield return "";
                }
                firstHunk = false;
                yield return $"{DiffColorizer.PlaceHeader}line {start}";
            }
            else {
                yield return line;
            }
        }
    }

    [GeneratedRegex(@"^@@ -(?<old>\d+)(?:,\d+)? \+(?<new>\d+)(?:,\d+)? @@")]
    private static partial Regex HunkHeader();
}