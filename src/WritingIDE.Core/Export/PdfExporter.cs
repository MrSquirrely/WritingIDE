using System.Globalization;

using PdfSharp.Drawing;
using PdfSharp.Fonts;
using PdfSharp.Pdf;

namespace WritingIDE.Core.Export;

/// <summary>
/// PDF export with PDFsharp. Manuscript style: US Letter, Times New Roman 12 pt, double spaced, ragged right, header
/// "Surname / TITLE / page". Book style: 6 x 9 in trim, Georgia 11 pt, justified, page numbers at the foot.
/// Each chapter starts on a new page. Uses the fonts installed in Windows.
/// </summary>
public static class PdfExporter {
    private static readonly Lock FontLock = new();
    private static bool _fontsConfigured;

    private sealed class Layout {
        public required double Width { get; init; }
        public required double Height { get; init; }
        public required double Margin { get; init; }
        public required string FontName { get; init; }
        public required double FontSize { get; init; }
        public required double LineHeight { get; init; }
        public required double Indent { get; init; }
        public required bool Justify { get; init; }
        public required bool Manuscript { get; init; }
    }

    public static void Write(string path, ManuscriptDocument document, ExportOptions options) {
        Write(path, document, options, []);
    }

    /// <param name="warnings">Receives a note when the chosen font cannot be used and the style's font is used instead.</param>
    public static void Write(string path, ManuscriptDocument document, ExportOptions options, List<string> warnings) {
        lock (FontLock) {
            // PDFsharp allows this to be set once, and reading it first counts: so set it once, without reading.
            if (!_fontsConfigured) {
                _fontsConfigured = true;
                try {
                    GlobalFontSettings.FontResolver ??= new WindowsFontResolver();
                }
                catch (InvalidOperationException) {
                    // Already configured (by an extension, say): keep that configuration.
                }
            }
        }
        bool manuscript = options.Style == ExportStyle.Manuscript;
        string font = ChooseFont(options, warnings);
        Layout layout = manuscript
            ? new Layout { Width = 612, Height = 792, Margin = 72, FontName = font, FontSize = 12, LineHeight = 28.8, Indent = 36, Justify = false, Manuscript = true }
            : new Layout { Width = 432, Height = 648, Margin = 54, FontName = font, FontSize = 10.5, LineHeight = 15, Indent = 18, Justify = true, Manuscript = false };

        using PdfDocument pdf = new();
        pdf.Info.Title = document.Title;
        pdf.Info.Author = document.Author;
        Writer writer = new(pdf, layout, document);
        if (options.IncludeTitlePage) {
            writer.TitlePage(options);
        }
        foreach (ExportBook book in document.Books) {
            if (book.Title is not null) {
                writer.BookPage(book.Title);
            }
            foreach (ExportChapter chapter in book.Chapters) {
                writer.Chapter(chapter, options);
            }
        }
        writer.Finish();
        pdf.Save(path);
    }

    /// <summary>The chosen font when a PDF can embed it, otherwise the style's font (with a warning saying why).</summary>
    private static string ChooseFont(ExportOptions options, List<string> warnings) {
        string family = options.EffectiveFontFamily;
        string fallback = ExportOptions.DefaultFontFamily(options.Style);
        if (family.Equals(fallback, StringComparison.OrdinalIgnoreCase)) {
            return family;
        }
        FontFamilyFaces? faces = FontCatalog.Installed.Find(family);
        string? problem = faces is null ? $"The font '{family}' was not found"
            : faces.Regular.IsEmbeddingRestricted ? $"The licence of '{family}' does not allow embedding it in documents"
            : !faces.Regular.HasTrueTypeOutlines ? $"'{family}' is a PostScript-flavoured OpenType font, which the PDF exporter cannot embed"
            : Damaged(faces);
        if (problem is null) {
            return family;
        }
        warnings.Add($"{problem}, so the PDF uses {fallback}.");
        return fallback;
    }

    /// <summary>A problem description when one of the family's files cannot be read, otherwise null.</summary>
    private static string? Damaged(FontFamilyFaces faces) {
        try {
            foreach (FontFace face in faces.All) {
                FontCatalog.ExtractFace(face);
            }
            return null;
        }
        catch (Exception exception) when (exception is InvalidDataException or IOException or UnauthorizedAccessException) {
            return exception.Message.TrimEnd('.');
        }
    }

    /// <summary>Lays text out page by page.</summary>
    private sealed class Writer(PdfDocument pdf, Layout layout, ManuscriptDocument document) {
        private XGraphics? _graphics;
        private double _y;
        private int _pageNumber;
        private readonly XFont _regular = new(layout.FontName, layout.FontSize, XFontStyleEx.Regular);
        private readonly XFont _italic = new(layout.FontName, layout.FontSize, XFontStyleEx.Italic);
        private readonly XFont _bold = new(layout.FontName, layout.FontSize, XFontStyleEx.Bold);
        private readonly XFont _boldItalic = new(layout.FontName, layout.FontSize, XFontStyleEx.BoldItalic);
        private readonly XFont _small = new(layout.FontName, layout.Manuscript ? 12 : 8.5, XFontStyleEx.Regular);

        private double Bottom => layout.Height - layout.Margin;

        private double TextWidth => layout.Width - (2 * layout.Margin);

        public void TitlePage(ExportOptions options) {
            NewPage(numbered: false);
            if (layout.Manuscript) {
                string[] contact = (options.Contact.Trim().Length > 0 ? options.Contact : document.Author).Replace("\r", "").Split('\n');
                double y = layout.Margin;
                foreach (string line in contact) {
                    _graphics!.DrawString(line.Trim(), _regular, XBrushes.Black, new XPoint(layout.Margin, y + layout.FontSize));
                    y += layout.FontSize * 1.2;
                }
                string words = $"About {document.RoundedWords.ToString("N0", CultureInfo.InvariantCulture)} words";
                _graphics!.DrawString(words, _regular, XBrushes.Black, new XPoint(layout.Width - layout.Margin - _graphics.MeasureString(words, _regular).Width, layout.Margin + layout.FontSize));
                Centered(document.Title.ToUpperInvariant(), _regular, layout.Height / 2);
                Centered($"by {document.Author}", _regular, (layout.Height / 2) + layout.LineHeight);
            }
            else {
                XFont title = new(layout.FontName, 24, XFontStyleEx.Regular);
                Centered(document.Title, title, layout.Height * 0.35);
                Centered(document.Author, new XFont(layout.FontName, 14, XFontStyleEx.Regular), (layout.Height * 0.35) + 40);
            }
        }

        public void BookPage(string title) {
            NewPage(numbered: true);
            Centered(title, new XFont(layout.FontName, layout.Manuscript ? 14 : 20, XFontStyleEx.Bold), layout.Height * 0.35);
        }

        public void Chapter(ExportChapter chapter, ExportOptions options) {
            NewPage(numbered: true);
            // Chapters open a third of the way down the page.
            _y = layout.Height / 3;
            XFont heading = layout.Manuscript ? _regular : new XFont(layout.FontName, 16, XFontStyleEx.Regular);
            Centered(chapter.Title, heading, _y);
            _y += layout.LineHeight * 2;
            bool afterBreak = true;
            foreach (ExportParagraph paragraph in chapter.Paragraphs) {
                if (paragraph.IsSceneBreak) {
                    EnsureSpace(layout.LineHeight);
                    Centered(options.SceneBreak, _regular, _y);
                    _y += layout.LineHeight;
                    afterBreak = true;
                    continue;
                }
                Paragraph(paragraph, indent: layout.Manuscript || !afterBreak);
                afterBreak = false;
            }
        }

        public void Finish() {
            _graphics?.Dispose();
        }

        private void Paragraph(ExportParagraph paragraph, bool indent) {
            List<(string Word, XFont Font, bool SpaceBefore)> words = [];
            foreach (InlineRun run in paragraph.Runs) {
                XFont font = run.Bold ? (run.Italic ? _boldItalic : _bold) : run.Italic ? _italic : _regular;
                string text = run.Text;
                int position = 0;
                while (position < text.Length) {
                    bool space = false;
                    while ((position < text.Length) && char.IsWhiteSpace(text[position])) {
                        space = true;
                        position++;
                    }
                    int start = position;
                    while ((position < text.Length) && !char.IsWhiteSpace(text[position])) {
                        position++;
                    }
                    if (position > start) {
                        // Leading space of a run counts; a run that starts mid-word (after emphasis) joins the previous word.
                        words.Add((text[start..position], font, space || ((start == 0) && (words.Count > 0) && char.IsWhiteSpace(text[0]))));
                    }
                }
            }
            if (words.Count == 0) {
                return;
            }
            double spaceWidth = _graphics!.MeasureString(" ", _regular).Width;
            int index = 0;
            bool firstLine = true;
            while (index < words.Count) {
                double available = TextWidth - (firstLine && indent ? layout.Indent : 0);
                List<(string Word, XFont Font, bool SpaceBefore, double Width)> line = [];
                double used = 0;
                while (index < words.Count) {
                    (string word, XFont font, bool spaceBefore) = words[index];
                    double width = _graphics.MeasureString(word, font).Width;
                    double gap = (line.Count > 0) && spaceBefore ? spaceWidth : 0;
                    if ((line.Count > 0) && (used + gap + width > available)) {
                        break;
                    }
                    line.Add((word, font, spaceBefore, width));
                    used += gap + width;
                    index++;
                }
                EnsureSpace(layout.LineHeight);
                bool last = index >= words.Count;
                int gaps = line.Skip(1).Count(static entry => entry.SpaceBefore);
                double extra = layout.Justify && !last && (gaps > 0) ? (available - used) / gaps : 0;
                double x = layout.Margin + (firstLine && indent ? layout.Indent : 0);
                double baseline = _y + layout.FontSize;
                for (int i = 0; i < line.Count; i++) {
                    if ((i > 0) && line[i].SpaceBefore) {
                        x += spaceWidth + extra;
                    }
                    _graphics.DrawString(line[i].Word, line[i].Font, XBrushes.Black, new XPoint(x, baseline));
                    x += line[i].Width;
                }
                _y += layout.LineHeight;
                firstLine = false;
            }
        }

        private void Centered(string text, XFont font, double y) {
            double width = _graphics!.MeasureString(text, font).Width;
            _graphics.DrawString(text, font, XBrushes.Black, new XPoint((layout.Width - width) / 2, y + font.Size));
        }

        private void EnsureSpace(double height) {
            if (_y + height > Bottom) {
                NewPage(numbered: true);
            }
        }

        private void NewPage(bool numbered) {
            _graphics?.Dispose();
            PdfPage page = pdf.AddPage();
            page.Width = XUnit.FromPoint(layout.Width);
            page.Height = XUnit.FromPoint(layout.Height);
            _graphics = XGraphics.FromPdfPage(page);
            _pageNumber++;
            _y = layout.Margin;
            if (!numbered) {
                return;
            }
            if (layout.Manuscript) {
                string header = $"{document.Surname} / {string.Join(' ', document.Title.Split(' ', StringSplitOptions.RemoveEmptyEntries).Take(3)).ToUpperInvariant()} / {_pageNumber}";
                double width = _graphics.MeasureString(header, _small).Width;
                _graphics.DrawString(header, _small, XBrushes.Black, new XPoint(layout.Width - layout.Margin - width, layout.Margin / 2 + 6));
            }
            else {
                string number = _pageNumber.ToString(CultureInfo.InvariantCulture);
                double width = _graphics.MeasureString(number, _small).Width;
                _graphics.DrawString(number, _small, XBrushes.Black, new XPoint((layout.Width - width) / 2, layout.Height - (layout.Margin / 2)));
            }
        }
    }
}

/// <summary>
/// Gives PDFsharp the font files from <see cref="FontCatalog"/>: installed fonts, fonts installed for this user and
/// fonts added to WritingIDE, including faces inside .ttc collections (PDFsharp's own lookup misses many, Georgia
/// among them). A missing bold or italic face is simulated; an unknown family falls back to Times New Roman.
/// </summary>
internal sealed class WindowsFontResolver : IFontResolver {
    private const string Fallback = "Times New Roman";
    private readonly Dictionary<string, byte[]> _fonts = new(StringComparer.OrdinalIgnoreCase);
    private readonly Lock _gate = new();

    public FontResolverInfo? ResolveTypeface(string familyName, bool isBold, bool isItalic) {
        FontFamilyFaces? faces = Usable(FontCatalog.Installed.Find(familyName)) ?? Usable(FontCatalog.Installed.Find(Fallback));
        if (faces is null) {
            return null;
        }
        FontFace? face = faces.Get(isBold, isItalic);
        if ((face is not null) && face.HasTrueTypeOutlines && !face.IsEmbeddingRestricted) {
            return new FontResolverInfo(Key(face));
        }
        // Simulate the missing style from the regular face.
        return new FontResolverInfo(Key(faces.Regular), isBold, isItalic);
    }

    public byte[]? GetFont(string faceName) {
        lock (_gate) {
            if (_fonts.TryGetValue(faceName, out byte[]? cached)) {
                return cached;
            }
            int hash = faceName.LastIndexOf('#');
            if ((hash < 0) || !int.TryParse(faceName.AsSpan(hash + 1), CultureInfo.InvariantCulture, out int index)) {
                return null;
            }
            string path = faceName[..hash];
            FontFace? face = FontCatalog.Read(path).FirstOrDefault(candidate => candidate.FaceIndex == index);
            if (face is null) {
                return null;
            }
            try {
                byte[] data = FontCatalog.ExtractFace(face);
                _fonts[faceName] = data;
                return data;
            }
            catch (Exception exception) when (exception is InvalidDataException or IOException or UnauthorizedAccessException) {
                return null;
            }
        }
    }

    private static FontFamilyFaces? Usable(FontFamilyFaces? faces) {
        return (faces is not null) && faces.Regular.HasTrueTypeOutlines && !faces.Regular.IsEmbeddingRestricted ? faces : null;
    }

    private static string Key(FontFace face) {
        return $"{face.Path}#{face.FaceIndex.ToString(CultureInfo.InvariantCulture)}";
    }
}