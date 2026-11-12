using System.Globalization;
using System.Text;
using System.Text.Json;

using WritingIDE.Core.Analysis;
using WritingIDE.Core.Model;

namespace WritingIDE.Core.Persistence;

/// <summary>A saved version of a chapter, taken by the writer before a big revision (or automatically before a restore).</summary>
public sealed record ChapterSnapshot(string FilePath, Guid ChapterId, DateTime CreatedUtc, string Label, string ChapterTitle, int Words) {
    public string DisplayName => Label.Length > 0 ? Label : CreatedUtc.ToLocalTime().ToString("g", CultureInfo.CurrentCulture);
}

/// <summary>
/// Per-chapter version history, like Scrivener's snapshots: Snapshots/&lt;chapter id&gt;/&lt;timestamp&gt;.json inside the
/// project folder, so they travel with the project (and with Git). Independent of saving: a snapshot captures the text
/// in the editor right now.
/// </summary>
public static class SnapshotStore {
    public const string FolderName = "Snapshots";
    private const string Extension = ".json";

    /// <summary>Takes a snapshot, or returns null when the text is identical to the newest snapshot.</summary>
    public static ChapterSnapshot? Take(string projectDirectory, Chapter chapter, string text, string label) {
        IReadOnlyList<ChapterSnapshot> existing = List(projectDirectory, chapter.Id);
        if ((existing.Count > 0) && (LoadText(existing[0]) == text)) {
            return null;
        }
        string folder = FolderFor(projectDirectory, chapter.Id);
        Directory.CreateDirectory(folder);
        DateTime now = DateTime.UtcNow;
        if ((existing.Count > 0) && (existing[0].CreatedUtc >= now)) {
            now = existing[0].CreatedUtc.AddMilliseconds(1);
        }
        string path = Path.Combine(folder, now.ToString("yyyyMMdd'T'HHmmssfff", CultureInfo.InvariantCulture) + Extension);
        SnapshotFile file = new() {
            ChapterId = chapter.Id,
            ChapterTitle = chapter.Title,
            Label = label.Trim(),
            CreatedUtc = now,
            Words = TextStatistics.CountWords(text),
            Text = text
        };
        File.WriteAllText(path, ProjectSerializer.Serialize(file), Encoding.UTF8);
        return ToSnapshot(path, file);
    }

    /// <summary>A chapter's snapshots, newest first. Unreadable files are skipped.</summary>
    public static IReadOnlyList<ChapterSnapshot> List(string projectDirectory, Guid chapterId) {
        string folder = FolderFor(projectDirectory, chapterId);
        if (!Directory.Exists(folder)) {
            return [];
        }
        List<ChapterSnapshot> snapshots = [];
        foreach (string path in Directory.EnumerateFiles(folder, "*" + Extension)) {
            SnapshotFile? file = Read(path);
            if (file is not null) {
                snapshots.Add(ToSnapshot(path, file));
            }
        }
        return snapshots.OrderByDescending(static snapshot => snapshot.CreatedUtc).ToList();
    }

    public static string LoadText(ChapterSnapshot snapshot) {
        return Read(snapshot.FilePath)?.Text ?? throw new InvalidDataException($"The snapshot '{snapshot.DisplayName}' could not be read.");
    }

    public static void Delete(ChapterSnapshot snapshot) {
        if (File.Exists(snapshot.FilePath)) {
            File.Delete(snapshot.FilePath);
        }
        string? folder = Path.GetDirectoryName(snapshot.FilePath);
        if ((folder is not null) && Directory.Exists(folder) && !Directory.EnumerateFileSystemEntries(folder).Any()) {
            Directory.Delete(folder);
        }
    }

    private static string FolderFor(string projectDirectory, Guid chapterId) {
        return Path.Combine(projectDirectory, FolderName, chapterId.ToString("N"));
    }

    private static SnapshotFile? Read(string path) {
        try {
            return ProjectSerializer.Deserialize<SnapshotFile>(File.ReadAllText(path));
        }
        catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException) {
            return null;
        }
    }

    private static ChapterSnapshot ToSnapshot(string path, SnapshotFile file) {
        return new ChapterSnapshot(path, file.ChapterId, file.CreatedUtc, file.Label, file.ChapterTitle, file.Words);
    }

    internal sealed class SnapshotFile {
        public Guid ChapterId { get; set; }
        public string ChapterTitle { get; set; } = "";
        public string Label { get; set; } = "";
        public DateTime CreatedUtc { get; set; }
        public int Words { get; set; }
        public string Text { get; set; } = "";
    }
}