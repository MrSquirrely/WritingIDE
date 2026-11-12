using System.Text;
using System.Text.RegularExpressions;

using WritingIDE.Core.Model;

namespace WritingIDE.Core.Analysis;

/// <summary>Whole-word search/replace ("rename refactoring") and manuscript export.</summary>
public static class ManuscriptTools {
    public static IReadOnlyList<(int Offset, int Length)> FindWholeWord(string text, string word) {
        if (string.IsNullOrEmpty(word)) {
            return [];
        }
        Regex regex = CreateWholeWordRegex(word);
        return regex.Matches(text).Select(static match => (match.Index, match.Length)).ToList();
    }

    public static string ReplaceWholeWord(string text, string oldValue, string newValue) {
        if (string.IsNullOrEmpty(oldValue)) {
            return text;
        }
        return CreateWholeWordRegex(oldValue).Replace(text, newValue.Replace("$", "$$"));
    }

    /// <summary>
    /// Joins every chapter into a single Markdown manuscript. A series is written as "# Book" sections with
    /// "## Chapter" headings, which the plain-text importer reads back as books and chapters.
    /// </summary>
    public static string Compile(NovelProject project, Func<Chapter, string> getText) {
        StringBuilder builder = new();
        if (project.IsSeries) {
            builder.AppendLine($"*{project.Name}*{(string.IsNullOrWhiteSpace(project.Author) ? "" : $", by {project.Author}")}");
            foreach (Book book in project.Books) {
                builder.AppendLine();
                builder.AppendLine($"# {book.Title}");
                foreach (Chapter chapter in project.GetChapters(book)) {
                    builder.AppendLine();
                    builder.AppendLine($"## {chapter.Title}");
                    builder.AppendLine();
                    builder.AppendLine(getText(chapter).Trim());
                }
            }
            return builder.ToString();
        }
        builder.AppendLine($"# {project.Name}");
        if (!string.IsNullOrWhiteSpace(project.Author)) {
            builder.AppendLine();
            builder.AppendLine($"*by {project.Author}*");
        }
        foreach (Chapter chapter in project.Chapters) {
            builder.AppendLine();
            builder.AppendLine($"## {chapter.Title}");
            builder.AppendLine();
            builder.AppendLine(getText(chapter).Trim());
        }
        return builder.ToString();
    }

    private static Regex CreateWholeWordRegex(string word) {
        return new Regex($@"(?<![\p{{L}}\p{{N}}]){Regex.Escape(word)}(?![\p{{L}}\p{{N}}])", RegexOptions.CultureInvariant);
    }
}