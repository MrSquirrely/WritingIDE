using System.Text.RegularExpressions;

using WritingIDE.Core.Model;

namespace WritingIDE.Core.Analysis;

/// <summary>
/// A scene: the part of a chapter between scene breaks. <see cref="Start"/>..<see cref="End"/> is the scene's text
/// (without the break line); <see cref="Label"/> is the optional text after the break marker ("*** Kael at the gate").
/// </summary>
public sealed record Scene(int Index, int Start, int End, string Label, int BreakOffset, int BreakLength) {
    public int Length => End - Start;
}

/// <summary>Finds scene breaks: a line made of *, #, ~ or - marks ("***", "* * *", "#", "~~~"), optionally followed by a label.</summary>
public static partial class SceneParser {
    /// <summary>The break inserted by Insert Scene Break.</summary>
    public const string BreakLine = "* * *";

    public static IReadOnlyList<Scene> Split(string text) {
        List<Scene> scenes = [];
        int start = 0;
        string label = "";
        int breakOffset = -1;
        int breakLength = 0;
        foreach (Match match in BreakPattern().Matches(text)) {
            scenes.Add(new Scene(scenes.Count, start, match.Index, label, breakOffset, breakLength));
            label = match.Groups["label"].Value.Trim();
            breakOffset = match.Index;
            breakLength = match.Length;
            start = Math.Min(text.Length, match.Index + match.Length);
            // The scene text starts after the line break that ends the marker line.
            if ((start < text.Length) && (text[start] == '\r')) {
                start++;
            }
            if ((start < text.Length) && (text[start] == '\n')) {
                start++;
            }
        }
        scenes.Add(new Scene(scenes.Count, start, text.Length, label, breakOffset, breakLength));
        return scenes;
    }

    /// <summary>True if the line is a scene break.</summary>
    public static bool IsBreakLine(string line) {
        return BreakPattern().IsMatch(line);
    }

    /// <summary>The scene containing <paramref name="offset"/>.</summary>
    public static Scene SceneAt(string text, int offset) {
        IReadOnlyList<Scene> scenes = Split(text);
        return scenes.LastOrDefault(scene => (scene.BreakOffset < 0 ? 0 : scene.BreakOffset) <= offset) ?? scenes[0];
    }

    /// <summary>A short title for a scene: its label, or the start of its first sentence.</summary>
    public static string Describe(string text, Scene scene, int maxLength = 60) {
        if (scene.Label.Length > 0) {
            return scene.Label;
        }
        string body = text[scene.Start..scene.End].Trim();
        int end = body.IndexOfAny(['\n', '.', '!', '?']);
        string first = (end > 0 ? body[..(end + (body[end] == '\n' ? 0 : 1))] : body).Trim();
        return first.Length <= maxLength ? first : first[..(maxLength - 3)].TrimEnd() + "...";
    }

    // Only "***" breaks may carry a label ("*** At the gate"); "# Heading" is a heading, not a break.
    [GeneratedRegex(@"^[ \t]*(?:(?:\*[ \t]*){3,}(?:[ \t]+(?<label>[^\r\n*][^\r\n]*?))?|#{1,3}|(?:~[ \t]*){3,}|(?:-[ \t]*){3,}|\u2042|(?:\u2022[ \t]*){3,})[ \t]*(?=\r?$)", RegexOptions.Multiline)]
    private static partial Regex BreakPattern();
}

/// <summary>Split and merge chapters, carrying comments, bookmarks and plot markers along with their text.</summary>
public static class ChapterOperations {
    /// <summary>
    /// Splits <paramref name="chapter"/> at <paramref name="offset"/>: the text after it (and the markers in it) move to
    /// a new chapter inserted right after, in the same book. Returns the new chapter; the first keeps the text before.
    /// </summary>
    public static Chapter Split(NovelProject project, Chapter chapter, string text, int offset, string newTitle) {
        offset = Math.Clamp(offset, 0, text.Length);
        string before = text[..offset].TrimEnd();
        string after = TrimLeadingBreak(text[offset..]);
        int removedFromStart = text.Length - offset - after.Length;
        Chapter second = new(newTitle) {
            BookId = chapter.BookId,
            Status = chapter.Status,
            PointOfView = chapter.PointOfView,
            StoryDate = chapter.StoryDate,
            Text = after
        };
        foreach (ChapterComment comment in chapter.Comments.Where(comment => comment.Offset >= offset).ToList()) {
            chapter.Comments.Remove(comment);
            comment.Offset = Math.Max(0, comment.Offset - offset - removedFromStart);
            second.Comments.Add(comment);
        }
        foreach (ChapterComment comment in chapter.Comments.Where(comment => comment.Offset + comment.Length > before.Length)) {
            comment.Length = Math.Max(0, before.Length - comment.Offset);
        }
        chapter.Text = before;
        project.Chapters.Insert(project.Chapters.IndexOf(chapter) + 1, second);
        return second;
    }

    /// <summary>
    /// Appends <paramref name="second"/> to <paramref name="first"/> with a scene break between them, moves its markers
    /// across and removes it from the project (its file is deleted on save). Returns the merged text.
    /// </summary>
    public static string Merge(NovelProject project, Chapter first, string firstText, Chapter second, string secondText) {
        string head = firstText.TrimEnd();
        string separator = head.Length == 0 ? "" : $"{Environment.NewLine}{Environment.NewLine}{SceneParser.BreakLine}{Environment.NewLine}{Environment.NewLine}";
        string merged = head + separator + secondText.TrimStart('\r', '\n');
        int shift = head.Length + separator.Length - (secondText.Length - secondText.TrimStart('\r', '\n').Length);
        foreach (ChapterComment comment in second.Comments.ToList()) {
            second.Comments.Remove(comment);
            comment.Offset = Math.Max(0, comment.Offset + shift);
            first.Comments.Add(comment);
        }
        if ((first.Synopsis.Length > 0) && (second.Synopsis.Length > 0)) {
            first.Synopsis = first.Synopsis.TrimEnd() + " " + second.Synopsis.Trim();
        }
        else if (first.Synopsis.Length == 0) {
            first.Synopsis = second.Synopsis;
        }
        first.Text = merged;
        if (second.FileName.Length > 0) {
            project.RemovedChapterFiles.Add(second.FileName);
        }
        project.Chapters.Remove(second);
        return merged;
    }

    /// <summary>Drops a scene break (and blank lines) at the very start of the text, as when splitting at a break.</summary>
    private static string TrimLeadingBreak(string text) {
        string trimmed = text.TrimStart('\r', '\n', ' ', '\t');
        int lineEnd = trimmed.IndexOf('\n');
        string firstLine = (lineEnd < 0 ? trimmed : trimmed[..lineEnd]).TrimEnd('\r');
        if (SceneParser.IsBreakLine(firstLine)) {
            trimmed = (lineEnd < 0 ? "" : trimmed[(lineEnd + 1)..]).TrimStart('\r', '\n');
        }
        return trimmed;
    }
}

/// <summary>A TODO, FIXME or TK left in the manuscript.</summary>
public sealed record ManuscriptTask(Chapter Chapter, int Offset, int Length, string Kind, string Text);

/// <summary>Finds tasks writers leave in the text: "TODO: ...", "FIXME ...", "[TK]" / "TK" (to come), "XXX".</summary>
public static partial class TaskScanner {
    public static IReadOnlyList<ManuscriptTask> Scan(Chapter chapter, string text) {
        List<ManuscriptTask> tasks = [];
        foreach (Match match in TaskPattern().Matches(text)) {
            string description = match.Groups["text"].Value.Trim();
            tasks.Add(new ManuscriptTask(chapter, match.Index, match.Length, match.Groups["kind"].Value.ToUpperInvariant(), description));
        }
        return tasks;
    }

    // A description follows TODO/FIXME/XXX (after a colon, dash or space) or any marker after a colon; it ends at the
    // line end or the next marker. "[TK]" (to come) stands alone.
    [GeneratedRegex(@"(?<![\p{L}\p{N}])\[?(?<kind>TODO|FIXME|TK|XXX)\]?(?![\p{L}\p{N}])(?:(?:(?<=TODO|FIXME|XXX)[:\-\u2014]?[ \t]+|[:\-\u2014][ \t]*)(?<text>(?:(?!(?<![\p{L}\p{N}])(?:TODO|FIXME|TK|XXX)(?![\p{L}\p{N}]))[^\r\n])*))?")]
    private static partial Regex TaskPattern();
}