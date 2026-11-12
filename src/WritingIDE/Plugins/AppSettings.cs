using System.Text.Json;

namespace WritingIDE.Plugins;

/// <summary>Per-user application settings, stored in %AppData%\WritingIDE\settings.json.</summary>
public sealed class AppSettings {
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public HarperSettings Harper { get; set; } = new();

    /// <summary>System (follow Windows), Light or Dark.</summary>
    public string Theme { get; set; } = "System";

    private static string StorePath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "WritingIDE", "settings.json");

    public static AppSettings Load() {
        try {
            if (File.Exists(StorePath)) {
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(StorePath), JsonOptions) ?? new AppSettings();
            }
        }
        catch (Exception exception) when (exception is IOException or JsonException or UnauthorizedAccessException) {
            // Fall back to defaults.
        }
        return new AppSettings();
    }

    public void Save() {
        try {
            Directory.CreateDirectory(Path.GetDirectoryName(StorePath)!);
            File.WriteAllText(StorePath, JsonSerializer.Serialize(this, JsonOptions));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) {
            // Settings are a convenience; failing to save them is not fatal.
        }
    }
}

public sealed class HarperSettings {
    public bool Enabled { get; set; }

    /// <summary>Full path to harper-ls.exe; empty to detect it automatically.</summary>
    public string ExecutablePath { get; set; } = "";

    /// <summary>American, British, Australian, Canadian or Indian.</summary>
    public string Dialect { get; set; } = "American";
}