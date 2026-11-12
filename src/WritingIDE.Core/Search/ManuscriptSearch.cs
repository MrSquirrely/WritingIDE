using System.Text.RegularExpressions;

namespace WritingIDE.Core.Search;

/// <summary>What to look for: literal text or a regular expression, with Visual Studio's Find options.</summary>
public sealed record SearchQuery(string Pattern, bool MatchCase = false, bool WholeWord = false, bool UseRegex = false) {
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(2);

    /// <summary>Builds the regex, or returns false with a readable error for an invalid pattern.</summary>
    public bool TryCreateRegex(out Regex? regex, out string? error) {
        regex = null;
        error = null;
        if (Pattern.Length == 0) {
            error = "Enter something to find.";
            return false;
        }
        string pattern = UseRegex ? Pattern : Regex.Escape(Pattern);
        if (WholeWord) {
            pattern = $@"(?<![\p{{L}}\p{{N}}_])(?:{pattern})(?![\p{{L}}\p{{N}}_])";
        }
        RegexOptions options = RegexOptions.CultureInvariant | RegexOptions.Multiline | (MatchCase ? RegexOptions.None : RegexOptions.IgnoreCase);
        try {
            regex = new Regex(pattern, options, Timeout);
            return true;
        }
        catch (ArgumentException exception) {
            error = $"Invalid regular expression: {exception.Message}";
            return false;
        }
    }
}

/// <summary>A match and, for Replace, the text that will replace it.</summary>
public sealed record SearchMatch(int Offset, int Length, string Value, string Replacement = "");

/// <summary>Find and Replace across manuscript text.</summary>
public static class ManuscriptSearch {
    public static IReadOnlyList<SearchMatch> Find(string text, Regex regex) {
        List<SearchMatch> matches = [];
        try {
            foreach (Match match in regex.Matches(text)) {
                if (match.Length > 0) {
                    matches.Add(new SearchMatch(match.Index, match.Length, match.Value));
                }
            }
        }
        catch (RegexMatchTimeoutException) {
            // A pathological pattern: return what was found so far.
        }
        return matches;
    }

    /// <summary>
    /// Plans every replacement in <paramref name="text"/>. With <paramref name="useRegex"/> the replacement may use
    /// substitutions such as $1; otherwise it is literal.
    /// </summary>
    public static IReadOnlyList<SearchMatch> PlanReplacements(string text, Regex regex, string replacement, bool useRegex) {
        List<SearchMatch> plan = [];
        try {
            foreach (Match match in regex.Matches(text)) {
                if (match.Length > 0) {
                    plan.Add(new SearchMatch(match.Index, match.Length, match.Value, useRegex ? match.Result(replacement) : replacement));
                }
            }
        }
        catch (RegexMatchTimeoutException) {
            // Replace what was planned before the timeout.
        }
        return plan;
    }

    /// <summary>Applies planned replacements (in any order) and returns the new text.</summary>
    public static string Apply(string text, IReadOnlyList<SearchMatch> plan) {
        System.Text.StringBuilder builder = new(text.Length);
        int position = 0;
        foreach (SearchMatch match in plan.OrderBy(static match => match.Offset)) {
            builder.Append(text, position, match.Offset - position);
            builder.Append(match.Replacement);
            position = match.Offset + match.Length;
        }
        builder.Append(text, position, text.Length - position);
        return builder.ToString();
    }

    /// <summary>The line around a match, trimmed, for the results list.</summary>
    public static string CreatePreview(string text, int offset, int length, int context = 60) {
        int start = Math.Max(0, offset - context);
        int end = Math.Min(text.Length, offset + length + context);
        string preview = text[start..end].Replace('\r', ' ').Replace('\n', ' ').Trim();
        return (start > 0 ? "..." : "") + preview + (end < text.Length ? "..." : "");
    }
}