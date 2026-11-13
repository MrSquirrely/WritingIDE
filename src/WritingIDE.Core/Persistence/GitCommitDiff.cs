using System.Globalization;
using System.Text.RegularExpressions;

namespace WritingIDE.Core.Persistence;

public enum CommitFileStatus {
    Modified,
    Added,
    Deleted,
    Renamed
}

/// <summary>
/// One file changed by a commit. <see cref="Diff"/> holds only the hunks of a "--word-diff=plain" diff: "@@" lines and
/// text with "[-removed-]" and "{+added+}" markers.
/// </summary>
public sealed record CommitFileDiff(string Path, string? OldPath, CommitFileStatus Status, bool IsBinary, string Diff, int WordsAdded, int WordsRemoved);

/// <summary>A commit with the changes it made, from <see cref="GitCommitDiff.Parse"/>.</summary>
public sealed record CommitDetails(string Hash, string ShortHash, string Author, DateTimeOffset? Date, string Subject, string Body, IReadOnlyList<CommitFileDiff> Files) {
    public int WordsAdded => Files.Sum(static file => file.WordsAdded);

    public int WordsRemoved => Files.Sum(static file => file.WordsRemoved);
}

/// <summary>
/// Reads the output of <see cref="Arguments"/> ("git show" with a word diff), which is what a writer wants to see for a
/// commit: which chapters changed and the words that were added and removed, rather than whole changed paragraphs.
/// </summary>
public static partial class GitCommitDiff {
    private const char FieldSeparator = '\u001f';
    private const char HeaderEnd = '\u001e';

    /// <summary>The git arguments whose output <see cref="Parse"/> reads, for the commit <paramref name="revision"/>.</summary>
    public static string[] Arguments(string revision) {
        return [
            "-c", "core.quotepath=off", "show", "--no-color", "--word-diff=plain", "--unified=0", "--diff-merges=first-parent", "--find-renames",
            "--format=%H%x1f%h%x1f%an%x1f%aI%x1f%B%x1e", revision, "--"
        ];
    }

    public static CommitDetails Parse(string output) {
        string text = output.Replace("\r\n", "\n");
        int headerEnd = text.IndexOf(HeaderEnd);
        if (headerEnd < 0) {
            throw new FormatException("This is not the output of git show for a commit.");
        }
        string[] fields = text[..headerEnd].Split(FieldSeparator);
        if (fields.Length < 5) {
            throw new FormatException("The commit header is incomplete.");
        }
        string message = fields[4].Trim();
        int firstBreak = message.IndexOf('\n');
        string subject = firstBreak < 0 ? message : message[..firstBreak].Trim();
        string body = firstBreak < 0 ? "" : message[(firstBreak + 1)..].Trim();
        DateTimeOffset? date = DateTimeOffset.TryParse(fields[3].Trim(), CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTimeOffset parsed) ? parsed : null;
        IReadOnlyList<CommitFileDiff> files = ParseFiles(text[(headerEnd + 1)..]);
        return new CommitDetails(fields[0].Trim(), fields[1].Trim(), fields[2].Trim(), date, subject, body, files);
    }

    private static List<CommitFileDiff> ParseFiles(string diff) {
        List<CommitFileDiff> files = [];
        List<string>? section = null;
        foreach (string line in diff.Split('\n')) {
            if (line.StartsWith("diff --git ", StringComparison.Ordinal)) {
                if (section is not null) {
                    files.Add(ParseFile(section));
                }
                section = [];
            }
            section?.Add(line);
        }
        if (section is not null) {
            files.Add(ParseFile(section));
        }
        return files;
    }

    private static CommitFileDiff ParseFile(List<string> lines) {
        string? oldPath = null;
        string? newPath = null;
        CommitFileStatus status = CommitFileStatus.Modified;
        bool binary = false;
        int firstHunk = -1;
        for (int index = 1; index < lines.Count; index++) {
            string line = lines[index];
            if (line.StartsWith("@@", StringComparison.Ordinal)) {
                firstHunk = index;
                break;
            }
            if (line.StartsWith("new file mode", StringComparison.Ordinal)) {
                status = CommitFileStatus.Added;
            }
            else if (line.StartsWith("deleted file mode", StringComparison.Ordinal)) {
                status = CommitFileStatus.Deleted;
            }
            else if (line.StartsWith("rename from ", StringComparison.Ordinal)) {
                status = CommitFileStatus.Renamed;
                oldPath = Unquote(line["rename from ".Length..]);
            }
            else if (line.StartsWith("rename to ", StringComparison.Ordinal)) {
                newPath = Unquote(line["rename to ".Length..]);
            }
            else if (line.StartsWith("--- ", StringComparison.Ordinal)) {
                oldPath ??= StripPrefix(line[4..], "a/");
            }
            else if (line.StartsWith("+++ ", StringComparison.Ordinal)) {
                newPath = StripPrefix(line[4..], "b/") ?? newPath;
            }
            else if (line.StartsWith("Binary files ", StringComparison.Ordinal)) {
                binary = true;
            }
        }
        string path = newPath ?? oldPath ?? PathFromHeader(lines[0]);
        if (status != CommitFileStatus.Renamed) {
            oldPath = null;
        }
        string hunks = firstHunk < 0 ? "" : string.Join("\n", lines.Skip(firstHunk)).TrimEnd();
        int added = 0;
        int removed = 0;
        foreach (Match match in Added().Matches(hunks)) {
            added += CountWords(match.Groups[1].Value);
        }
        foreach (Match match in Removed().Matches(hunks)) {
            removed += CountWords(match.Groups[1].Value);
        }
        return new CommitFileDiff(path, oldPath, status, binary, hunks, added, removed);
    }

    /// <summary>"a/Manuscript/One.txt" to "Manuscript/One.txt"; "/dev/null" (an added or deleted file) to null.</summary>
    private static string? StripPrefix(string value, string prefix) {
        // Git ends the name with a tab when it contains spaces.
        string path = Unquote(value.TrimEnd('\t'));
        if (path == "/dev/null") {
            return null;
        }
        return path.StartsWith(prefix, StringComparison.Ordinal) ? path[prefix.Length..] : path;
    }

    /// <summary>The path from "diff --git a/x b/x" when the diff has no "---"/"+++" lines (a binary file or pure rename).</summary>
    private static string PathFromHeader(string header) {
        string rest = header["diff --git ".Length..];
        int split = rest.LastIndexOf(" b/", StringComparison.Ordinal);
        return split >= 0 ? Unquote(rest[(split + 3)..]) : Unquote(rest);
    }

    private static string Unquote(string value) {
        string trimmed = value.Trim();
        return (trimmed.Length >= 2) && (trimmed[0] == '"') && (trimmed[^1] == '"') ? Regex.Unescape(trimmed[1..^1]) : trimmed;
    }

    private static int CountWords(string text) {
        return text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Count(static word => word.Any(char.IsLetterOrDigit));
    }

    [GeneratedRegex(@"\{\+(.*?)\+\}", RegexOptions.Singleline)]
    private static partial Regex Added();

    [GeneratedRegex(@"\[-(.*?)-\]", RegexOptions.Singleline)]
    private static partial Regex Removed();
}