using System.Windows;
using System.Windows.Media;

using WritingIDE.Core.Grammar;
using WritingIDE.Core.Model;

namespace WritingIDE.Infrastructure;

/// <summary>
/// Shared glyphs and theme brushes for story element kinds, project items and diagnostic severities.
/// The brushes are single, mutable instances whose colours are copied from the active palette by
/// <see cref="ApplyTheme"/>, so everything bound to them (tree icons, error list, highlighting) updates in place.
/// They belong to the UI thread.
/// </summary>
public static class Visuals {
    public const string FolderGlyph = "\uE8B7";
    public const string ChapterGlyph = "\uE8A5";
    public const string ProjectGlyph = "\uE82D";
    public const string ManuscriptGlyph = "\uE736";
    public const string StartPageGlyph = "\uE80F";
    public const string SeriesGlyph = "\uE8F1";
    public const string BookGlyph = "\uE82D";
    public const string BibleGlyph = "\uE8A4";
    public const string PluginGlyph = "\uEA86";

    private static readonly Dictionary<EntityKind, SolidColorBrush> KindBrushes = EntityKindInfo.All.ToDictionary(
        static info => info.Kind,
        static info => new SolidColorBrush((Color)ColorConverter.ConvertFromString(info.ColorHex)));

    private static readonly Dictionary<EntityKind, string> KindResourceKeys = new() {
        [EntityKind.Character] = "CharacterBrush",
        [EntityKind.Place] = "PlaceBrush",
        [EntityKind.Item] = "ItemBrush",
        [EntityKind.Faction] = "FactionBrush",
        [EntityKind.Event] = "EventBrush",
        [EntityKind.Lore] = "LoreBrush"
    };

    public static SolidColorBrush BookBrush { get; } = CreateThemeBrush("#8E5BB5");

    public static SolidColorBrush ChapterBrush { get; } = CreateThemeBrush("#5C5C5C");

    public static SolidColorBrush FolderBrush { get; } = CreateThemeBrush("#DCB67A");

    public static SolidColorBrush ProjectBrush { get; } = CreateThemeBrush("#68217A");

    public static SolidColorBrush ErrorBrush { get; } = CreateThemeBrush("#E51400");

    public static SolidColorBrush WarningBrush { get; } = CreateThemeBrush("#C88A00");

    public static SolidColorBrush MessageBrush { get; } = CreateThemeBrush("#1E7BC4");

    /// <summary>Neutral colour for menu and tool icons.</summary>
    public static SolidColorBrush IconBrush { get; } = CreateThemeBrush("#5C5C5C");

    /// <summary>Secondary text, such as hints in tooltips.</summary>
    public static SolidColorBrush SubtleBrush { get; } = CreateThemeBrush("#717171");

    private static readonly Dictionary<EntityKind, Brush> FrozenKindBrushes = [];

    public static Brush GetKindBrush(EntityKind kind) {
        return KindBrushes[kind];
    }

    /// <summary>A frozen snapshot of the kind colour, safe to hand to renderers that freeze or cache brushes (AvalonEdit).</summary>
    public static Brush GetFrozenKindBrush(EntityKind kind) {
        if (!FrozenKindBrushes.TryGetValue(kind, out Brush? brush)) {
            brush = KindBrushes[kind].CloneCurrentValue();
            brush.Freeze();
            FrozenKindBrushes[kind] = brush;
        }
        return brush;
    }

    public static string GetSeverityGlyph(DiagnosticSeverity severity) {
        return severity switch {
            DiagnosticSeverity.Error => "\uEA39",
            DiagnosticSeverity.Warning => "\uE7BA",
            _ => "\uE946"
        };
    }

    public static Brush GetSeverityBrush(DiagnosticSeverity severity) {
        return severity switch {
            DiagnosticSeverity.Error => ErrorBrush,
            DiagnosticSeverity.Warning => WarningBrush,
            _ => MessageBrush
        };
    }

    /// <summary>Copies colours from the active palette (Light.xaml or Dark.xaml) into the shared brushes.</summary>
    public static void ApplyTheme(ResourceDictionary palette) {
        foreach ((EntityKind kind, string key) in KindResourceKeys) {
            Copy(palette, key, KindBrushes[kind]);
        }
        FrozenKindBrushes.Clear();
        Copy(palette, "BookBrush", BookBrush);
        Copy(palette, "IconGreyBrush", ChapterBrush);
        Copy(palette, "FolderBrush", FolderBrush);
        Copy(palette, "BrandBrush", ProjectBrush);
        Copy(palette, "ErrorBrush", ErrorBrush);
        Copy(palette, "WarningBrush", WarningBrush);
        Copy(palette, "MessageBrush", MessageBrush);
        Copy(palette, "IconGreyBrush", IconBrush);
        Copy(palette, "SubtleTextBrush", SubtleBrush);
    }

    /// <summary>A frozen brush for fixed colours that look the same in both themes.</summary>
    public static SolidColorBrush CreateBrush(string hex) {
        SolidColorBrush brush = new((Color)ColorConverter.ConvertFromString(hex));
        brush.Freeze();
        return brush;
    }

    private static SolidColorBrush CreateThemeBrush(string hex) {
        return new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
    }

    private static void Copy(ResourceDictionary palette, string key, SolidColorBrush target) {
        if (palette[key] is SolidColorBrush source) {
            target.Color = source.Color;
        }
    }
}