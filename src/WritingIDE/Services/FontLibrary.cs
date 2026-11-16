using System.Windows.Media;

using WritingIDE.Core.Export;
using WritingIDE.Plugins;

namespace WritingIDE.Services;

/// <summary>A font in the pickers: one installed in Windows, or one added to WritingIDE.</summary>
public sealed record FontChoice(string Name, FontFamily Family, bool IsAdded) {
    public string Detail => IsAdded ? "Added to WritingIDE" : "Installed";

    public override string ToString() {
        return Name;
    }
}

/// <summary>
/// The fonts WritingIDE can use: every font installed in Windows plus font files added in Tools > Fonts, which are
/// copied to %LocalAppData%\WritingIDE\Fonts and usable only inside WritingIDE (in the editor and in exports).
/// Also holds the editor font, which every manuscript editor follows.
/// </summary>
public static class FontLibrary {
    public const string DefaultFamily = "Cambria";
    public const double DefaultSize = 16;
    public const string FileFilter = "Font files (*.ttf;*.otf;*.ttc)|*.ttf;*.otf;*.ttc";

    public static readonly IReadOnlyList<double> Sizes = [10, 11, 12, 13, 14, 15, 16, 18, 20, 22, 24, 28, 32];

    private static IReadOnlyList<FontChoice>? _choices;
    private static AppSettings? _settings;

    /// <summary>The editor font or size changed.</summary>
    public static event Action? Changed;

    public static string Folder => FontCatalog.AppFontsFolder;

    public static string EditorFamilyName => _settings?.EditorFontFamily is { Length: > 0 } name ? name : DefaultFamily;

    public static double EditorSize => _settings?.EditorFontSize is >= 6 and <= 96 ? _settings.EditorFontSize : DefaultSize;

    public static FontFamily EditorFamily => Resolve(EditorFamilyName);

    /// <summary>Installed and added fonts, sorted by name; added fonts win when a name is in both.</summary>
    public static IReadOnlyList<FontChoice> Choices => _choices ??= LoadChoices();

    /// <summary>Called once at startup: deletes fonts removed last session, then reads the editor font settings.</summary>
    public static void Initialize(AppSettings settings) {
        _settings = settings;
        foreach (string path in settings.PendingFontDeletes.ToList()) {
            try {
                if (File.Exists(path)) {
                    File.Delete(path);
                }
                settings.PendingFontDeletes.Remove(path);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) {
                // Still in use: try again next time.
            }
        }
    }

    public static void SetEditorFont(string family, double size) {
        if (_settings is null) {
            return;
        }
        _settings.EditorFontFamily = family.Trim().Length > 0 ? family.Trim() : DefaultFamily;
        _settings.EditorFontSize = Math.Clamp(size, 6, 96);
        _settings.Save();
        Changed?.Invoke();
    }

    /// <summary>The WPF family for a name: an added font first, then an installed one; unknown names fall back to Cambria.</summary>
    public static FontFamily Resolve(string name) {
        FontChoice? choice = Choices.FirstOrDefault(choice => choice.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        return choice?.Family ?? new FontFamily($"{name}, {DefaultFamily}, Georgia");
    }

    /// <summary>
    /// Copies font files into WritingIDE's fonts folder. Returns the names of the families added and the files that
    /// were not fonts.
    /// </summary>
    public static (IReadOnlyList<string> Families, IReadOnlyList<string> Rejected) AddFiles(IEnumerable<string> paths) {
        Directory.CreateDirectory(Folder);
        List<string> families = [];
        List<string> rejected = [];
        foreach (string path in paths) {
            IReadOnlyList<FontFace> faces = FontCatalog.Read(path);
            if (faces.Count == 0) {
                rejected.Add(Path.GetFileName(path));
                continue;
            }
            string target = Path.Combine(Folder, Path.GetFileName(path));
            if (!string.Equals(Path.GetFullPath(path), Path.GetFullPath(target), StringComparison.OrdinalIgnoreCase)) {
                File.Copy(path, target, overwrite: true);
            }
            families.AddRange(faces.Select(static face => face.TypographicFamily ?? face.Family));
        }
        Refresh();
        return (families.Distinct(StringComparer.OrdinalIgnoreCase).ToList(), rejected);
    }

    /// <summary>
    /// Removes an added font's files. Windows may keep a font file open while it is in use, so a file that cannot be
    /// deleted now is deleted the next time WritingIDE starts. Returns true when the font was removed from the list.
    /// </summary>
    public static bool Remove(string family) {
        if (!Directory.Exists(Folder)) {
            return false;
        }
        List<string> files = Directory.EnumerateFiles(Folder)
            .Where(path => FontCatalog.Read(path).Any(face => family.Equals(face.TypographicFamily ?? face.Family, StringComparison.OrdinalIgnoreCase) || family.Equals(face.Family, StringComparison.OrdinalIgnoreCase)))
            .ToList();
        foreach (string file in files) {
            try {
                File.Delete(file);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) {
                if (_settings is not null && !_settings.PendingFontDeletes.Contains(file, StringComparer.OrdinalIgnoreCase)) {
                    _settings.PendingFontDeletes.Add(file);
                    _settings.Save();
                }
            }
        }
        Refresh();
        return files.Count > 0;
    }

    /// <summary>Re-reads the installed and added fonts.</summary>
    public static void Refresh() {
        _choices = null;
        FontCatalog.Refresh();
        Changed?.Invoke();
    }

    private static List<FontChoice> LoadChoices() {
        Dictionary<string, FontChoice> choices = new(StringComparer.OrdinalIgnoreCase);
        foreach (FontFamily family in Fonts.SystemFontFamilies) {
            string name = Name(family);
            if ((name.Length > 0) && !name.StartsWith('@')) {
                choices.TryAdd(name, new FontChoice(name, family, false));
            }
        }
        if (Directory.Exists(Folder)) {
            HashSet<string> pending = new(_settings?.PendingFontDeletes ?? [], StringComparer.OrdinalIgnoreCase);
            foreach (string path in Directory.EnumerateFiles(Folder).Where(path => !pending.Contains(path))) {
                try {
                    // Each file separately, so a font being removed (still on disk) does not stay in the list.
                    foreach (FontFamily family in Fonts.GetFontFamilies(new Uri(path))) {
                        string name = Name(family);
                        if (name.Length > 0) {
                            choices[name] = new FontChoice(name, family, true);
                        }
                    }
                }
                catch (Exception exception) when (exception is ArgumentException or UriFormatException or IOException) {
                    // Not a font WPF can read.
                }
            }
        }
        return choices.Values.OrderBy(static choice => choice.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    private static string Name(FontFamily family) {
        if (family.FamilyNames.TryGetValue(System.Windows.Markup.XmlLanguage.GetLanguage("en-us"), out string? english)) {
            return english;
        }
        string? first = family.FamilyNames.Values.FirstOrDefault();
        if (first is not null) {
            return first;
        }
        string source = family.Source;
        int hash = source.LastIndexOf('#');
        return hash >= 0 ? source[(hash + 1)..] : source;
    }
}