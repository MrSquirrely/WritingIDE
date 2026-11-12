using System.Globalization;
using System.Text.RegularExpressions;

namespace WritingIDE.Core.Analysis;

/// <summary>
/// Turns a free-text story date into a number that can be compared, so the timeline can spot chapters set earlier
/// than the one before them. Understands calendar dates ("12 May 2024"), relative counts ("Day 3", "Year 2, Day 14",
/// "Week 6") and plain numbers ("1204"). Anything else is left unordered.
/// </summary>
public static partial class StoryTime {
    public static double? TryGetSortKey(string? value) {
        if (string.IsNullOrWhiteSpace(value)) {
            return null;
        }
        string text = value.Trim();
        if (DateTime.TryParse(text, CultureInfo.CurrentCulture, DateTimeStyles.AllowWhiteSpaces, out DateTime date)
            || DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out date)) {
            return date.Ticks / (double)TimeSpan.TicksPerDay;
        }
        MatchCollection units = Unit().Matches(text);
        if (units.Count > 0) {
            double total = 0;
            foreach (Match match in units) {
                double amount = double.Parse(match.Groups["n"].Value, CultureInfo.InvariantCulture);
                total += match.Groups["unit"].Value.ToLowerInvariant() switch {
                    "year" => (amount - 1) * 365,
                    "month" => (amount - 1) * 30,
                    "week" => (amount - 1) * 7,
                    "hour" => amount / 24.0,
                    _ => amount
                };
            }
            return total;
        }
        // "1204", "Spring 1204": a single number orders the chapter; several numbers are ambiguous.
        MatchCollection numbers = Number().Matches(text);
        return numbers.Count == 1 ? double.Parse(numbers[0].Value, CultureInfo.InvariantCulture) : null;
    }

    [GeneratedRegex(@"\b(?<unit>year|month|week|day|hour)\s*(?<n>\d+(?:\.\d+)?)\b", RegexOptions.IgnoreCase)]
    private static partial Regex Unit();

    [GeneratedRegex(@"(?<![\w.])-?\d+(?:\.\d+)?(?![\w.])")]
    private static partial Regex Number();
}