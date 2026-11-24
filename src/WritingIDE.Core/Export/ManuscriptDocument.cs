using WritingIDE.Core.Analysis;
using WritingIDE.Core.Model;
using WritingIDE.Core.Text;

namespace WritingIDE.Core.Export;

public enum ExportFormat {
    Markdown,
    Docx,
    Epub,
    Pdf,
    GoogleDocs,
    AiContext,
    Lulu
}

/// <summary>The size of a printed book's pages after trimming, in inches.</summary>
public sealed record TrimSize(string Name, double WidthInches, double HeightInches) {
    public static TrimSize UsTrade { get; } = new("US Trade", 6, 9);

    /// <summary>The paperback sizes Lulu prints novels in.</summary>
    public static IReadOnlyList<TrimSize> Lulu { get; } = [
        new("Pocket Book", 4.25, 6.875),
        new("Novella", 5, 8),
        new("Digest", 5.5, 8.5),
        new("A5", 5.83, 8.27),
        UsTrade,
        new("Royal", 6.14, 9.21),
        new("US Letter", 8.5, 11)
    ];

    public string Label => string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{Name} ({WidthInches:0.###} x {HeightInches:0.###} in)");

    public override string ToString() {
        return Label;
    }
}

/// <summary>Standard manuscript format (for agents and editors) or a typeset book.</summary>
public enum ExportStyle {
    Manuscript,
    Book
}

public sealed record ExportOptions {
    public ExportFormat Format { get; init; } = ExportFormat.Docx;

    public ExportStyle Style { get; init; } = ExportStyle.Manuscript;

    /// <summary>The chapters to include, in reading order.</summary>
    public IReadOnlyList<Chapter> Chapters { get; init; } = [];

    public bool IncludeTitlePage { get; init; } = true;

    /// <summary>What separates scenes in the output ("#" in manuscript format, "* * *" in a book).</summary>
    public string SceneBreak { get; init; } = "#";

    /// <summary>Contact details for the manuscript title page (name, address, email), one per line.</summary>
    public string Contact { get; init; } = "";

    /// <summary>AI export: include the full chapter text (otherwise synopses only).</summary>
    public bool IncludeFullText { get; init; } = true;

    /// <summary>AI export: what the writer wants from the AI, placed at the top.</summary>
    public string AiInstructions { get; init; } = "";

    /// <summary>The body font for Word, PDF and EPUB; null or empty uses the style's own (Times New Roman or Georgia).</summary>
    public string? FontFamily { get; init; }

    /// <summary>Lulu export: the book's trim size.</summary>
    public TrimSize TrimSize { get; init; } = TrimSize.UsTrade;

    /// <summary>The font the export uses: <see cref="FontFamily"/>, or the style's default.</summary>
    public string EffectiveFontFamily => string.IsNullOrWhiteSpace(FontFamily) ? DefaultFontFamily(Style) : FontFamily.Trim();

    public static string DefaultFontFamily(ExportStyle style) {
        return style == ExportStyle.Manuscript ? "Times New Roman" : "Georgia";
    }
}

/// <summary>A run of text inside a paragraph, with *italic* and **bold** from the manuscript's Markdown-style emphasis.</summary>
public readonly record struct InlineRun(string Text, bool Italic, bool Bold);

/// <summary>A paragraph of prose, or a scene break.</summary>
public sealed record ExportParagraph(IReadOnlyList<InlineRun> Runs, bool IsSceneBreak) {
    public string PlainText => string.Concat(Runs.Select(static run => run.Text));
}

public sealed record ExportChapter(string Title, IReadOnlyList<ExportParagraph> Paragraphs, int Words);

public sealed record ExportBook(string? Title, IReadOnlyList<ExportChapter> Chapters);

/// <summary>Everything an exporter needs, independent of the output format.</summary>
public sealed record ManuscriptDocument(string Title, string Author, IReadOnlyList<ExportBook> Books, int Words) {
    public IEnumerable<ExportChapter> AllChapters => Books.SelectMany(static book => book.Chapters);

    /// <summary>The author's surname, for manuscript page headers.</summary>
    public string Surname => Author.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries).LastOrDefault() ?? "";

    /// <summary>Word count rounded the way manuscript title pages show it (to the nearest 100, or 1,000 above 10,000).</summary>
    public int RoundedWords => Words < 1000 ? Words : (Words < 10000 ? (int)Math.Round(Words / 100.0) * 100 : (int)Math.Round(Words / 1000.0) * 1000);
}

public static class ManuscriptDocumentBuilder {
    /// <summary>Builds the export structure: books (in a series) and chapters, split into paragraphs and scene breaks.</summary>
    public static ManuscriptDocument Build(NovelProject project, IReadOnlyList<Chapter> chapters, Func<Chapter, string> getText) {
        List<ExportBook> books = [];
        foreach (IGrouping<Guid?, Chapter> group in chapters.GroupBy(static chapter => chapter.BookId)) {
            string? bookTitle = project.IsSeries ? project.FindBook(group.Key)?.Title : null;
            List<ExportChapter> exported = group.Select(chapter => {
                string text = getText(chapter);
                return new ExportChapter(chapter.Title, ParseParagraphs(text), TextStatistics.CountWords(text));
            }).ToList();
            books.Add(new ExportBook(bookTitle, exported));
        }
        return new ManuscriptDocument(project.Name, project.Author, books, books.Sum(static book => book.Chapters.Sum(static chapter => chapter.Words)));
    }

    /// <summary>One paragraph per non-empty line; scene break lines become scene breaks (never two in a row, never first or last).</summary>
    public static IReadOnlyList<ExportParagraph> ParseParagraphs(string text) {
        List<ExportParagraph> paragraphs = [];
        foreach (string raw in text.Replace("\r\n", "\n").Split('\n')) {
            string line = raw.Trim();
            if (line.Length == 0) {
                continue;
            }
            if (SceneParser.IsBreakLine(line)) {
                if ((paragraphs.Count > 0) && !paragraphs[^1].IsSceneBreak) {
                    paragraphs.Add(new ExportParagraph([], true));
                }
                continue;
            }
            paragraphs.Add(new ExportParagraph(ParseInline(line), false));
        }
        while ((paragraphs.Count > 0) && paragraphs[^1].IsSceneBreak) {
            paragraphs.RemoveAt(paragraphs.Count - 1);
        }
        return paragraphs;
    }

    /// <summary>Splits *italic*, _italic_, **bold** and ***bold italic*** emphasis into runs; unmatched markers stay as text.</summary>
    public static IReadOnlyList<InlineRun> ParseInline(string line) {
        List<InlineRun> runs = [];
        int position = 0;
        foreach (EmphasisSpan span in Emphasis.Find(line)) {
            if (span.Start > position) {
                runs.Add(new InlineRun(line[position..span.Start], false, false));
            }
            runs.Add(new InlineRun(line.Substring(span.ContentStart, span.ContentLength), span.Italic, span.Bold));
            position = span.End;
        }
        if (position < line.Length) {
            runs.Add(new InlineRun(line[position..], false, false));
        }
        return runs;
    }
}