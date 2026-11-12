using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace WritingIDE.Infrastructure;

/// <summary>bool to Visibility, optionally inverted.</summary>
public sealed class BoolToVisibilityConverter(bool invert) : IValueConverter {
    public static BoolToVisibilityConverter Normal { get; } = new(false);

    public static BoolToVisibilityConverter Inverted { get; } = new(true);

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) {
        bool flag = (value is true) ^ invert;
        return flag ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) {
        throw new NotSupportedException();
    }
}

/// <summary>false/true to selected index 0/1.</summary>
public sealed class BoolToIndexConverter : IValueConverter {
    public static BoolToIndexConverter Instance { get; } = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) {
        return value is true ? 1 : 0;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) {
        return value is 1;
    }
}