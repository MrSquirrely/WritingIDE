using System.Text.RegularExpressions;

namespace WritingIDE.Core.Analysis;

/// <summary>
/// The analysed chapter's place in reading order, plus every chapter title, so rules can resolve references such as
/// "Died: Chapter 12" or "Died: The Stag" to a position.
/// </summary>
public sealed partial record ManuscriptPosition(int Index, IReadOnlyList<string> ChapterTitles) {
    /// <summary>The index of the chapter a reference names, or null if it names none.</summary>
    public int? Resolve(string reference) {
        string value = reference.Trim().Trim('"', '\'', '.');
        if (value.Length == 0) {
            return null;
        }
        for (int i = 0; i < ChapterTitles.Count; i++) {
            if (ChapterTitles[i].Equals(value, StringComparison.OrdinalIgnoreCase)) {
                return i;
            }
        }
        // "Chapter 12", "Ch. 12", "ch 12" or just "12".
        Match number = ChapterNumber().Match(value);
        if (number.Success) {
            string digits = number.Groups["number"].Value;
            for (int i = 0; i < ChapterTitles.Count; i++) {
                Match title = ChapterNumber().Match(ChapterTitles[i]);
                if (title.Success && (title.Groups["number"].Value == digits)) {
                    return i;
                }
            }
        }
        // A part of the title ("The Stag" for "Chapter 12: The Stag").
        for (int i = 0; i < ChapterTitles.Count; i++) {
            if ((value.Length >= 4) && ChapterTitles[i].Contains(value, StringComparison.OrdinalIgnoreCase)) {
                return i;
            }
        }
        return null;
    }

    [GeneratedRegex(@"^\s*(?:(?:chapter|ch\.?)\s*)?(?<number>\d{1,3})\b", RegexOptions.IgnoreCase)]
    private static partial Regex ChapterNumber();
}