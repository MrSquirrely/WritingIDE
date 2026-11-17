using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

using WritingIDE.Core.Model;
using WritingIDE.Editor;
using WritingIDE.Infrastructure;
using WritingIDE.Sdk;
using WritingIDE.ViewModels;

namespace WritingIDE.Services;

/// <summary>The chapter editor in the active tab, as seen by extensions. Every call runs on the UI thread.</summary>
public sealed class EditorApi(MainViewModel shell) : IEditorApi {
    public Guid? ActiveChapterId => ProjectApi.OnUi(() => shell.ActiveChapter?.Chapter.Id);

    public int CaretOffset => ProjectApi.OnUi(() => ActiveEditor?.CaretOffset ?? 0);

    public int SelectionStart => ProjectApi.OnUi(() => ActiveEditor?.SelectionStart ?? 0);

    public int SelectionLength => ProjectApi.OnUi(() => ActiveEditor?.SelectionLength ?? 0);

    public string SelectedText => ProjectApi.OnUi(() => ActiveEditor?.SelectedText ?? "");

    private ManuscriptEditor? ActiveEditor {
        get {
            ChapterDocumentViewModel? document = shell.ActiveChapter;
            if (document is null) {
                return null;
            }
            // The editor is created with the view.
            _ = document.View;
            return document.Editor;
        }
    }

    public bool ReplaceSelection(string text) {
        ArgumentNullException.ThrowIfNull(text);
        return ProjectApi.OnUi(() => {
            ManuscriptEditor? editor = ActiveEditor;
            if (editor is null) {
                return false;
            }
            int start = editor.SelectionStart;
            editor.Document.Replace(start, editor.SelectionLength, text);
            editor.Select(start + text.Length, 0);
            return true;
        });
    }

    public void Select(Guid chapterId, int offset, int length) {
        ProjectApi.OnUi(() => {
            Chapter chapter = shell.Project?.Chapters.FirstOrDefault(chapter => chapter.Id == chapterId) ?? throw new KeyNotFoundException($"No chapter with id {chapterId}.");
            shell.OpenChapter(chapter).Select(offset, length);
            return 0;
        });
    }
}

/// <summary>
/// One extension's data: per-user settings in %LocalAppData%\WritingIDE\ExtensionData\&lt;id&gt;\settings.json, and
/// values kept in the open project's file.
/// </summary>
public sealed class PluginStorage(MainViewModel shell, string extensionId) : IPluginStorage {
    private const string SettingsFile = "settings.json";

    private readonly Lock _lock = new();
    private Dictionary<string, string>? _settings;

    public static string Root { get; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WritingIDE", "ExtensionData");

    public string DataDirectory {
        get {
            string path = Path.Combine(Root, GetFolderName(extensionId));
            Directory.CreateDirectory(path);
            return path;
        }
    }

    public string? GetSetting(string key) {
        lock (_lock) {
            return Load().GetValueOrDefault(key);
        }
    }

    public void SetSetting(string key, string? value) {
        ArgumentException.ThrowIfNullOrEmpty(key);
        lock (_lock) {
            Dictionary<string, string> settings = Load();
            if (value is null ? !settings.Remove(key) : (settings.TryGetValue(key, out string? old) && (old == value))) {
                return;
            }
            if (value is not null) {
                settings[key] = value;
            }
            string path = Path.Combine(DataDirectory, SettingsFile);
            string temporary = path + ".tmp";
            File.WriteAllText(temporary, JsonSerializer.Serialize(settings));
            File.Move(temporary, path, overwrite: true);
        }
    }

    public string? GetProjectValue(string key) {
        return ProjectApi.OnUi(() => shell.Project?.ExtensionData.GetValueOrDefault(extensionId)?.GetValueOrDefault(key));
    }

    public void SetProjectValue(string key, string? value) {
        ArgumentException.ThrowIfNullOrEmpty(key);
        ProjectApi.OnUi(() => {
            NovelProject project = shell.Project ?? throw new InvalidOperationException("No project is open.");
            project.ExtensionData.TryGetValue(extensionId, out Dictionary<string, string>? values);
            if (value == values?.GetValueOrDefault(key)) {
                return 0;
            }
            if (values is null) {
                values = [];
                project.ExtensionData[extensionId] = values;
            }
            if (value is null) {
                values.Remove(key);
            }
            else {
                values[key] = value;
            }
            project.MarkDirty();
            return 0;
        });
    }

    /// <summary>Called with the lock held. A missing or damaged file gives empty settings.</summary>
    private Dictionary<string, string> Load() {
        if (_settings is not null) {
            return _settings;
        }
        try {
            string path = Path.Combine(Root, GetFolderName(extensionId), SettingsFile);
            _settings = File.Exists(path) ? JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(path)) : null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException) {
            _settings = null;
        }
        return _settings ??= [];
    }

    /// <summary>The extension id as a single folder name: an id can never point outside <see cref="Root"/>.</summary>
    internal static string GetFolderName(string id) {
        char[] invalid = Path.GetInvalidFileNameChars();
        string name = new string(id.Select(character => invalid.Contains(character) ? '_' : character).ToArray()).Trim(' ', '.');
        return name.Length == 0 ? "_" : name;
    }
}

/// <summary>A tool window an extension registered, open as a tab.</summary>
public sealed class PluginToolDocumentViewModel(MainViewModel shell, ExtensionInfo extension, PluginToolWindow window) : DocumentViewModel(shell) {
    public PluginToolWindow Window { get; } = window;

    public override string Title => Window.Title;

    public override string Glyph => Visuals.PluginGlyph;

    public override Brush GlyphBrush => Visuals.IconBrush;

    public override string ToolTip => $"{Window.Title} ({extension.Name})";

    protected override FrameworkElement CreateView() {
        return CreateContent();
    }

    public override FrameworkElement? CreateSecondaryView() {
        return CreateContent();
    }

    public override void OnClosed() {
        try {
            Window.Closed?.Invoke();
        }
        catch (Exception exception) {
            Shell.Log($"warning: tool window '{Window.Title}' of extension '{extension.Name}' failed while closing: {exception.Message}");
        }
    }

    private FrameworkElement CreateContent() {
        string problem;
        try {
            if (Window.CreateContent() is FrameworkElement element) {
                return element;
            }
            problem = "its content is not a WPF element.";
        }
        catch (Exception exception) {
            problem = exception.Message;
        }
        Shell.Log($"warning: tool window '{Window.Title}' of extension '{extension.Name}' could not be shown: {problem}");
        return new TextBlock { Text = $"'{Window.Title}' could not be shown: {problem}", Margin = new Thickness(16), TextWrapping = TextWrapping.Wrap };
    }
}