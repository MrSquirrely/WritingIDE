using System.Text.Json;

namespace WritingIDE.Services;

/// <summary>Most-recently-used project list, stored in %AppData%\WritingIDE\recent.json.</summary>
public static class RecentProjects {
    private const int MaxEntries = 10;

    private static string StorePath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "WritingIDE", "recent.json");

    public static IReadOnlyList<string> Load() {
        try {
            if (File.Exists(StorePath)) {
                List<string> paths = JsonSerializer.Deserialize<List<string>>(File.ReadAllText(StorePath)) ?? [];
                return paths.Where(File.Exists).ToList();
            }
        }
        catch (Exception exception) when (exception is IOException or JsonException or UnauthorizedAccessException) {
            // A corrupt MRU list is not worth bothering the user about.
        }
        return [];
    }

    public static void Add(string path) {
        List<string> paths = Load().Where(existing => !string.Equals(existing, path, StringComparison.OrdinalIgnoreCase)).ToList();
        paths.Insert(0, path);
        Save(paths.Take(MaxEntries).ToList());
    }

    private static void Save(List<string> paths) {
        try {
            Directory.CreateDirectory(Path.GetDirectoryName(StorePath)!);
            File.WriteAllText(StorePath, JsonSerializer.Serialize(paths));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) {
            // Ignore: the MRU list is a convenience.
        }
    }
}