using System.Windows;

using Microsoft.Win32;

namespace WritingIDE.Infrastructure;

public enum ThemePreference {
    /// <summary>Follow the Windows app mode (Settings > Personalization > Colors).</summary>
    System,
    Light,
    Dark
}

/// <summary>
/// Switches between the light and dark palettes at runtime: swaps Themes/Light.xaml for Themes/Dark.xaml in the
/// application resources (everything uses DynamicResource), switches HandyControls' own theme, and recolours the
/// shared brushes in <see cref="Visuals"/>.
/// </summary>
public static class AppTheme {
    private const string PaletteKey = "PaletteName";

    static AppTheme() {
        SystemEvents.UserPreferenceChanged += (_, e) => {
            if ((Preference == ThemePreference.System) && (e.Category == UserPreferenceCategory.General)) {
                Application.Current?.Dispatcher.BeginInvoke(() => Apply(ThemePreference.System));
            }
        };
    }

    /// <summary>Raised on the UI thread after the theme changed.</summary>
    public static event Action? Changed;

    public static ThemePreference Preference { get; private set; } = ThemePreference.System;

    public static bool IsDark { get; private set; }

    public static ThemePreference Parse(string? value) {
        return Enum.TryParse(value, ignoreCase: true, out ThemePreference preference) ? preference : ThemePreference.System;
    }

    public static void Apply(ThemePreference preference) {
        Preference = preference;
        bool dark = preference switch {
            ThemePreference.Dark => true,
            ThemePreference.Light => false,
            _ => SystemUsesDarkTheme()
        };
        Application application = Application.Current;
        ResourceDictionary palette = new() { Source = new Uri($"pack://application:,,,/WritingIDE;component/Themes/{(dark ? "Dark" : "Light")}.xaml") };
        IList<ResourceDictionary> merged = application.Resources.MergedDictionaries;
        int index = merged.ToList().FindIndex(static dictionary => dictionary.Contains(PaletteKey));
        if (index >= 0) {
            merged[index] = palette;
        }
        else {
            merged.Add(palette);
        }
        HandyControl.Themes.ThemeManager.Current.ApplicationTheme = dark ? HandyControl.Themes.ApplicationTheme.Dark : HandyControl.Themes.ApplicationTheme.Light;
        Visuals.ApplyTheme(palette);
        IsDark = dark;
        Changed?.Invoke();
    }

    /// <summary>Windows stores "AppsUseLightTheme" = 0 when apps should be dark.</summary>
    public static bool SystemUsesDarkTheme() {
        try {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is int value && (value == 0);
        }
        catch (System.Security.SecurityException) {
            return false;
        }
    }
}