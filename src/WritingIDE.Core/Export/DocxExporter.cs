using System.Globalization;

using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

using WordText = DocumentFormat.OpenXml.Wordprocessing.Text;

namespace WritingIDE.Core.Export;

/// <summary>
/// Word (.docx) export. Manuscript style follows standard manuscript format (Times New Roman 12 pt, double spaced,
/// 1" margins, half-inch indents, "Surname / TITLE / page" header, each chapter on a new page a third of the way
/// down, "#" between scenes). Book style is single-spaced Georgia with justified text. Chapters use the Heading 1
/// style, so Word's navigation pane and Google Docs' outline list them.
/// </summary>
public static class DocxExporter {
    private const string BodyStyle = "Body";
    private const string FirstStyle = "BodyFirst";
    private const string ChapterStyle = "Heading1";
    private const string BookStyle = "Heading2";
    private const string BreakStyle = "SceneBreak";

    public static void Write(string path, ManuscriptDocument document, ExportOptions options) {
        using WordprocessingDocument package = WordprocessingDocument.Create(path, WordprocessingDocumentType.Document);
        MainDocumentPart main = package.AddMainDocumentPart();
        bool manuscript = options.Style == ExportStyle.Manuscript;
        AddStyles(main, manuscript, options.EffectiveFontFamily);
        package.PackageProperties.Title = document.Title;
        package.PackageProperties.Creator = document.Author;

        Body body = new();
        if (options.IncludeTitlePage) {
            AddTitlePage(body, document, options, manuscript);
        }
        bool first = !options.IncludeTitlePage;
        foreach (ExportBook book in document.Books) {
            if (book.Title is not null) {
                body.Append(Heading(book.Title, BookStyle, pageBreakBefore: !first));
                first = false;
            }
            foreach (ExportChapter chapter in book.Chapters) {
                body.Append(Heading(chapter.Title, ChapterStyle, pageBreakBefore: !first));
                first = false;
                bool afterBreak = true;
                foreach (ExportParagraph paragraph in chapter.Paragraphs) {
                    if (paragraph.IsSceneBreak) {
                        body.Append(new Paragraph(new ParagraphProperties(new ParagraphStyleId { Val = BreakStyle }), new Run(new WordText(options.SceneBreak))));
                        afterBreak = true;
                        continue;
                    }
                    Paragraph element = new(new ParagraphProperties(new ParagraphStyleId { Val = afterBreak ? FirstStyle : BodyStyle }));
                    foreach (InlineRun run in paragraph.Runs) {
                        RunProperties properties = new();
                        if (run.Bold) {
                            properties.Append(new Bold());
                        }
                        if (run.Italic) {
                            // Manuscript convention once was underlining; modern guidelines accept italics.
                            properties.Append(new Italic());
                        }
                        element.Append(new Run(properties, new WordText(run.Text) { Space = SpaceProcessingModeValues.Preserve }));
                    }
                    body.Append(element);
                    afterBreak = false;
                }
            }
        }
        if (manuscript) {
            body.Append(new Paragraph(new ParagraphProperties(new ParagraphStyleId { Val = BreakStyle }), new Run(new WordText("END"))));
        }
        body.Append(SectionProperties(main, document, manuscript));
        main.Document = new Document(body);
        main.Document.Save();
    }

    private static void AddTitlePage(Body body, ManuscriptDocument document, ExportOptions options, bool manuscript) {
        if (manuscript) {
            // Contact block top left, word count top right (as a right-aligned paragraph), title centred halfway down.
            string[] contact = (options.Contact.Trim().Length > 0 ? options.Contact : document.Author).Replace("\r", "").Split('\n');
            body.Append(new Paragraph(
                new ParagraphProperties(new SpacingBetweenLines { Line = "240", LineRule = LineSpacingRuleValues.Auto, After = "0" }, new Justification { Val = JustificationValues.Right }),
                new Run(new WordText($"About {document.RoundedWords.ToString("N0", CultureInfo.InvariantCulture)} words"))));
            foreach (string line in contact) {
                body.Append(new Paragraph(new ParagraphProperties(new SpacingBetweenLines { Line = "240", LineRule = LineSpacingRuleValues.Auto, After = "0" }), new Run(new WordText(line.Trim()))));
            }
            body.Append(new Paragraph(new ParagraphProperties(new SpacingBetweenLines { Before = "4800", Line = "480", LineRule = LineSpacingRuleValues.Auto }, new Justification { Val = JustificationValues.Center }),
                new Run(new WordText(document.Title.ToUpperInvariant()))));
            body.Append(new Paragraph(new ParagraphProperties(new SpacingBetweenLines { Line = "480", LineRule = LineSpacingRuleValues.Auto }, new Justification { Val = JustificationValues.Center }),
                new Run(new WordText($"by {document.Author}"))));
        }
        else {
            body.Append(new Paragraph(new ParagraphProperties(new ParagraphStyleId { Val = "Title" }), new Run(new WordText(document.Title))));
            if (document.Author.Length > 0) {
                body.Append(new Paragraph(new ParagraphProperties(new ParagraphStyleId { Val = "Subtitle" }), new Run(new WordText(document.Author))));
            }
        }
    }

    private static Paragraph Heading(string text, string style, bool pageBreakBefore) {
        ParagraphProperties properties = new(new ParagraphStyleId { Val = style });
        if (pageBreakBefore) {
            properties.Append(new PageBreakBefore());
        }
        return new Paragraph(properties, new Run(new WordText(text)));
    }

    private static SectionProperties SectionProperties(MainDocumentPart main, ManuscriptDocument document, bool manuscript) {
        SectionProperties section = new(
            new PageSize { Width = 12240, Height = 15840 },
            new PageMargin { Top = 1440, Bottom = 1440, Left = 1440, Right = 1440, Header = 720, Footer = 720, Gutter = 0 });
        HeaderPart header = main.AddNewPart<HeaderPart>();
        string label = manuscript ? $"{document.Surname} / {ShortTitle(document.Title).ToUpperInvariant()} / " : $"{document.Title}    ";
        header.Header = new Header(new Paragraph(
            new ParagraphProperties(new Justification { Val = JustificationValues.Right }),
            new Run(new WordText(label) { Space = SpaceProcessingModeValues.Preserve }),
            new Run(new FieldChar { FieldCharType = FieldCharValues.Begin }),
            new Run(new FieldCode(" PAGE ") { Space = SpaceProcessingModeValues.Preserve }),
            new Run(new FieldChar { FieldCharType = FieldCharValues.Separate }),
            new Run(new WordText("1")),
            new Run(new FieldChar { FieldCharType = FieldCharValues.End })));
        header.Header.Save();
        section.InsertAt(new HeaderReference { Type = HeaderFooterValues.Default, Id = main.GetIdOfPart(header) }, 0);
        // No header on the title page.
        section.Append(new TitlePage());
        return section;
    }

    /// <summary>Running heads use a short form of the title (the first three words).</summary>
    private static string ShortTitle(string title) {
        string[] words = title.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return string.Join(' ', words.Take(3));
    }

    private static void AddStyles(MainDocumentPart main, bool manuscript, string font) {
        string size = manuscript ? "24" : "22";
        string line = manuscript ? "480" : "300";
        StyleDefinitionsPart part = main.AddNewPart<StyleDefinitionsPart>();
        Styles styles = new(
            new DocDefaults(
                new RunPropertiesDefault(new RunPropertiesBaseStyle(new RunFonts { Ascii = font, HighAnsi = font, ComplexScript = font, EastAsia = font }, new FontSize { Val = size }, new FontSizeComplexScript { Val = size }, new Languages { Val = "en-US" })),
                new ParagraphPropertiesDefault(new ParagraphPropertiesBaseStyle(new SpacingBetweenLines { After = "0", Line = line, LineRule = LineSpacingRuleValues.Auto }))));
        styles.Append(ParagraphStyle("Normal", "Normal", isDefault: true));
        Style bodyStyle = ParagraphStyle(BodyStyle, "Body Text");
        bodyStyle.StyleParagraphProperties = new StyleParagraphProperties(
            new Indentation { FirstLine = manuscript ? "720" : "360" },
            new Justification { Val = manuscript ? JustificationValues.Left : JustificationValues.Both });
        styles.Append(bodyStyle);
        Style firstStyle = ParagraphStyle(FirstStyle, "Body Text First");
        firstStyle.StyleParagraphProperties = new StyleParagraphProperties(
            new Indentation { FirstLine = manuscript ? "720" : "0" },
            new Justification { Val = manuscript ? JustificationValues.Left : JustificationValues.Both });
        styles.Append(firstStyle);
        Style chapter = ParagraphStyle(ChapterStyle, "heading 1");
        chapter.StyleParagraphProperties = new StyleParagraphProperties(
            new KeepNext(),
            new SpacingBetweenLines { Before = manuscript ? "3600" : "2400", After = manuscript ? "480" : "600" },
            new Justification { Val = JustificationValues.Center },
            new OutlineLevel { Val = 0 });
        chapter.StyleRunProperties = new StyleRunProperties(manuscript ? new Bold { Val = false } : new Bold(), new FontSize { Val = manuscript ? "24" : "36" });
        styles.Append(chapter);
        Style book = ParagraphStyle(BookStyle, "heading 2");
        book.StyleParagraphProperties = new StyleParagraphProperties(new KeepNext(), new SpacingBetweenLines { Before = "4800", After = "480" }, new Justification { Val = JustificationValues.Center }, new OutlineLevel { Val = 0 });
        book.StyleRunProperties = new StyleRunProperties(new Bold(), new FontSize { Val = "40" });
        styles.Append(book);
        Style sceneBreak = ParagraphStyle(BreakStyle, "Scene Break");
        sceneBreak.StyleParagraphProperties = new StyleParagraphProperties(new SpacingBetweenLines { Before = manuscript ? "0" : "240", After = manuscript ? "0" : "240" }, new Justification { Val = JustificationValues.Center });
        styles.Append(sceneBreak);
        Style title = ParagraphStyle("Title", "Title");
        title.StyleParagraphProperties = new StyleParagraphProperties(new SpacingBetweenLines { Before = "4800", After = "240" }, new Justification { Val = JustificationValues.Center });
        title.StyleRunProperties = new StyleRunProperties(new FontSize { Val = "56" });
        styles.Append(title);
        Style subtitle = ParagraphStyle("Subtitle", "Subtitle");
        subtitle.StyleParagraphProperties = new StyleParagraphProperties(new Justification { Val = JustificationValues.Center });
        subtitle.StyleRunProperties = new StyleRunProperties(new FontSize { Val = "32" });
        styles.Append(subtitle);
        part.Styles = styles;
        part.Styles.Save();
    }

    private static Style ParagraphStyle(string id, string name, bool isDefault = false) {
        // Schema order: name, basedOn, qFormat (pPr and rPr are set later through properties, which place them correctly).
        Style style = new(new StyleName { Val = name }) { Type = StyleValues.Paragraph, StyleId = id };
        if (isDefault) {
            style.Default = true;
        }
        else {
            style.Append(new BasedOn { Val = "Normal" });
        }
        style.Append(new PrimaryStyle());
        return style;
    }
}