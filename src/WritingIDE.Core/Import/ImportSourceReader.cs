using System.Text;

using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

using WordText = DocumentFormat.OpenXml.Wordprocessing.Text;

namespace WritingIDE.Core.Import;

/// <summary>Reads a manuscript or bible file as plain text for the import prompt: .txt, .md, or a Word .docx.</summary>
public static class ImportSourceReader {
    public const string FileFilter = "Manuscripts and bibles (*.txt;*.md;*.markdown;*.docx)|*.txt;*.md;*.markdown;*.docx|All files (*.*)|*.*";

    public static ImportSource Read(string path) {
        string text = Path.GetExtension(path).Equals(".docx", StringComparison.OrdinalIgnoreCase) ? ReadDocx(path) : File.ReadAllText(path);
        return new ImportSource(Path.GetFileName(path), text.Replace("\r\n", "\n").Trim());
    }

    /// <summary>
    /// One line per paragraph with a blank line between paragraphs, the way the import format wants prose. Word
    /// headings become Markdown headings ("# Book", "## Chapter") so chapter breaks survive.
    /// </summary>
    public static string ReadDocx(string path) {
        using WordprocessingDocument document = WordprocessingDocument.Open(path, false);
        Body? body = document.MainDocumentPart?.Document?.Body;
        if (body is null) {
            return "";
        }
        StringBuilder text = new();
        foreach (Paragraph paragraph in body.Descendants<Paragraph>()) {
            string line = string.Concat(paragraph.Descendants().Select(static element => element switch {
                WordText run => run.Text,
                TabChar => "\t",
                Break => " ",
                _ => ""
            })).Trim();
            if (line.Length == 0) {
                continue;
            }
            int level = HeadingLevel(paragraph.ParagraphProperties?.ParagraphStyleId?.Val?.Value);
            text.Append(level > 0 ? new string('#', level) + " " : "").Append(line).Append("\n\n");
        }
        return text.ToString().Trim();
    }

    /// <summary>1 for "Heading1" or "Title", 2 for "Heading2" and so on; 0 for body text.</summary>
    private static int HeadingLevel(string? style) {
        if (style is null) {
            return 0;
        }
        if (style.Equals("Title", StringComparison.OrdinalIgnoreCase)) {
            return 1;
        }
        return style.StartsWith("Heading", StringComparison.OrdinalIgnoreCase) && int.TryParse(style.AsSpan(7), out int level) && (level is >= 1 and <= 6) ? level : 0;
    }
}