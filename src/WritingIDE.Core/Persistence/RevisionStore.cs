using System.Text;

using WritingIDE.Core.Model;

namespace WritingIDE.Core.Persistence;

/// <summary>Revision baselines: the text a chapter's tracked changes are measured against, in Revisions/&lt;chapter id&gt;.txt.</summary>
public static class RevisionStore {
    public const string FolderName = "Revisions";

    public static string PathFor(string projectDirectory, Guid chapterId) {
        return Path.Combine(projectDirectory, FolderName, chapterId.ToString("N") + ".txt");
    }

    public static string? Load(string projectDirectory, Chapter chapter) {
        string path = PathFor(projectDirectory, chapter.Id);
        return File.Exists(path) ? File.ReadAllText(path) : null;
    }

    public static void Save(string projectDirectory, Chapter chapter, string baseline) {
        string path = PathFor(projectDirectory, chapter.Id);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, baseline, Encoding.UTF8);
    }

    public static void Delete(string projectDirectory, Chapter chapter) {
        string path = PathFor(projectDirectory, chapter.Id);
        if (File.Exists(path)) {
            File.Delete(path);
        }
        string folder = Path.Combine(projectDirectory, FolderName);
        if (Directory.Exists(folder) && !Directory.EnumerateFileSystemEntries(folder).Any()) {
            Directory.Delete(folder);
        }
    }
}