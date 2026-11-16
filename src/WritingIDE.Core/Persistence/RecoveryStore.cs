using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

using WritingIDE.Core.Model;

namespace WritingIDE.Core.Persistence;

/// <summary>An unsaved state of a project found after WritingIDE closed without saving.</summary>
public sealed record RecoverySnapshot(string ProjectFilePath, DateTime SavedAtUtc, int ChapterCount, string FilePath);

/// <summary>
/// Crash recovery: periodic snapshots of a project's unsaved state (the project file plus every chapter's current
/// text), kept outside the project in %LocalAppData%\WritingIDE\Recovery. A snapshot is deleted on a successful save
/// or a deliberate "don't save", so one that survives means the app crashed or was killed.
/// </summary>
public static class RecoveryStore {
    private const string Suffix = ".recovery.json";

    /// <summary>Where snapshots are kept (tests point this elsewhere).</summary>
    public static string RootDirectory { get; set; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WritingIDE", "Recovery");

    private static readonly Lock WriteGate = new();

    /// <summary>Bumped by <see cref="Delete"/>, so a snapshot captured before a save is never written after it.</summary>
    private static long _generation;

    /// <summary>Writes a snapshot atomically (temporary file, then rename) so a crash mid-write never leaves a broken one.</summary>
    public static void Write(NovelProject project, Func<Chapter, string> getText) {
        Capture(project, getText)();
    }

    /// <summary>
    /// Takes the snapshot now (on the thread that owns the project) and returns the slow part, serializing and
    /// writing it, to run on any thread. If the project is saved before that runs, the write is skipped.
    /// </summary>
    public static Action Capture(NovelProject project, Func<Chapter, string> getText) {
        if (string.IsNullOrEmpty(project.FilePath)) {
            return static () => {
            };
        }
        RecoveryFile file = new() {
            ProjectFilePath = project.FilePath,
            SavedAtUtc = DateTime.UtcNow,
            Project = ProjectSerializer.ToDto(project, getText)
        };
        string target = PathFor(project.FilePath);
        long generation;
        lock (WriteGate) {
            generation = _generation;
        }
        return () => {
            string json = ProjectSerializer.Serialize(file);
            lock (WriteGate) {
                if (generation != _generation) {
                    return;
                }
                Directory.CreateDirectory(RootDirectory);
                string temporary = target + ".tmp";
                File.WriteAllText(temporary, json, Encoding.UTF8);
                File.Move(temporary, target, overwrite: true);
            }
        };
    }

    /// <summary>The snapshot for a project, if one exists and is newer than the project file on disk.</summary>
    public static RecoverySnapshot? Find(string projectFilePath) {
        string path = PathFor(projectFilePath);
        if (!File.Exists(path)) {
            return null;
        }
        try {
            RecoveryFile? file = ProjectSerializer.Deserialize<RecoveryFile>(File.ReadAllText(path));
            if (file?.Project is null) {
                return null;
            }
            DateTime saved = File.Exists(projectFilePath) ? File.GetLastWriteTimeUtc(projectFilePath) : DateTime.MinValue;
            if (file.SavedAtUtc <= saved) {
                // The project was saved after the snapshot (e.g. by another copy of the app): nothing to recover.
                return null;
            }
            return new RecoverySnapshot(file.ProjectFilePath, file.SavedAtUtc, file.Project.Chapters.Count, path);
        }
        catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException) {
            return null;
        }
    }

    /// <summary>Rebuilds the project as it was in the snapshot. It is marked dirty: saving makes the recovery permanent.</summary>
    public static NovelProject Restore(RecoverySnapshot snapshot) {
        RecoveryFile file = ProjectSerializer.Deserialize<RecoveryFile>(File.ReadAllText(snapshot.FilePath))
            ?? throw new InvalidDataException("The recovery file is empty or invalid.");
        string directory = Path.GetDirectoryName(snapshot.ProjectFilePath) ?? "";
        NovelProject project = ProjectSerializer.FromDto(file.Project!, snapshot.ProjectFilePath, chapter => {
            if (chapter.Text is not null) {
                return chapter.Text;
            }
            string path = Path.Combine(directory, chapter.File);
            return File.Exists(path) ? File.ReadAllText(path) : "";
        });
        project.IsDirty = true;
        return project;
    }

    public static void Delete(string projectFilePath) {
        string path = PathFor(projectFilePath);
        lock (WriteGate) {
            _generation++;
            try {
                if (File.Exists(path)) {
                    File.Delete(path);
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) {
                // A stale snapshot is harmless: Find ignores snapshots older than the project file.
            }
        }
    }

    /// <summary>One file per project, named by a hash of its full path.</summary>
    public static string PathFor(string projectFilePath) {
        string key = Path.GetFullPath(projectFilePath).ToUpperInvariant();
        string hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key)))[..16];
        return Path.Combine(RootDirectory, Path.GetFileNameWithoutExtension(projectFilePath) + "-" + hash + Suffix);
    }

    internal sealed class RecoveryFile {
        public string ProjectFilePath { get; set; } = "";
        public DateTime SavedAtUtc { get; set; }
        public ProjectSerializer.ProjectFileDto? Project { get; set; }
    }
}