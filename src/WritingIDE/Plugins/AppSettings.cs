using System.Text.Json;

namespace WritingIDE.Plugins;

/// <summary>Per-user application settings, stored in %AppData%\WritingIDE\settings.json.</summary>
public sealed class AppSettings {
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public HarperSettings Harper { get; set; } = new();

    /// <summary>Minutes between automatic saves to disk; 0 turns autosave off (crash recovery snapshots always run).</summary>
    public int AutosaveMinutes { get; set; }

    /// <summary>System (follow Windows), Light or Dark.</summary>
    public string Theme { get; set; } = "System";

    /// <summary>View > Name Lenses: CodeLens-style reference counts above the first mention of each name.</summary>
    public bool ShowNameLenses { get; set; } = true;

    /// <summary>View > Comments: the margin with comment cards and the comment highlighting.</summary>
    public bool ShowCommentMargin { get; set; } = true;

    /// <summary>The manuscript editor font: an installed font or one added in Tools > Fonts.</summary>
    public string EditorFontFamily { get; set; } = "Cambria";

    public double EditorFontSize { get; set; } = 16;

    /// <summary>How long a writing sprint lasts, in minutes.</summary>
    public int SprintMinutes { get; set; } = 25;

    /// <summary>Added font files that could not be deleted while in use; deleted at the next start.</summary>
    public List<string> PendingFontDeletes { get; set; } = [];

    public AiSettings Ai { get; set; } = new();

    public ApiSettings Api { get; set; } = new();

    /// <summary>Ids of installed extensions the user switched off.</summary>
    public List<string> DisabledExtensions { get; set; } = [];

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

/// <summary>AI tools: the chosen provider and each provider's endpoint, model and (encrypted) key.</summary>
public sealed class AiSettings {
    public Core.Ai.AiProviderKind Provider { get; set; } = Core.Ai.AiProviderKind.Gemini;

    public List<AiProviderSettings> Providers { get; set; } = [];

    /// <summary>Providers the writer agreed to send manuscript text to.</summary>
    public List<Core.Ai.AiProviderKind> Consented { get; set; } = [];

    public AiProviderSettings For(Core.Ai.AiProviderKind kind) {
        AiProviderSettings? settings = Providers.FirstOrDefault(provider => provider.Kind == kind);
        if (settings is null) {
            Core.Ai.AiProviderInfo info = Core.Ai.AiProviderInfo.Get(kind);
            settings = new AiProviderSettings { Kind = kind, Endpoint = info.Endpoint, Model = info.DefaultModel };
            Providers.Add(settings);
        }
        return settings;
    }
}

public sealed class AiProviderSettings {
    public Core.Ai.AiProviderKind Kind { get; set; }

    public string Endpoint { get; set; } = "";

    public string Model { get; set; } = "";

    /// <summary>The API key, encrypted for the current Windows user (DPAPI) and Base64-encoded. Never stored in plain text.</summary>
    public string ProtectedKey { get; set; } = "";

    public string GetKey() {
        if (ProtectedKey.Length == 0) {
            return "";
        }
        try {
            byte[] bytes = System.Security.Cryptography.ProtectedData.Unprotect(Convert.FromBase64String(ProtectedKey), null, System.Security.Cryptography.DataProtectionScope.CurrentUser);
            return System.Text.Encoding.UTF8.GetString(bytes);
        }
        catch (Exception exception) when (exception is System.Security.Cryptography.CryptographicException or FormatException) {
            return "";
        }
    }

    public void SetKey(string key) {
        ProtectedKey = key.Trim().Length == 0
            ? ""
            : Convert.ToBase64String(System.Security.Cryptography.ProtectedData.Protect(System.Text.Encoding.UTF8.GetBytes(key.Trim()), null, System.Security.Cryptography.DataProtectionScope.CurrentUser));
    }
}

/// <summary>The local REST API: off by default, localhost only, every request needs the token.</summary>
public sealed class ApiSettings {
    public bool Enabled { get; set; }

    public int Port { get; set; } = 5719;

    public string Token { get; set; } = "";
}

public sealed class HarperSettings {
    public bool Enabled { get; set; }

    /// <summary>Full path to harper-ls.exe; empty to detect it automatically.</summary>
    public string ExecutablePath { get; set; } = "";

    /// <summary>American, British, Australian, Canadian or Indian.</summary>
    public string Dialect { get; set; } = "American";
}