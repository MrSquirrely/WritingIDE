using System.Globalization;
using System.IO.Compression;
using System.Net;
using System.Text;

namespace WritingIDE.Core.Export;

/// <summary>
/// EPUB 3 export (with an EPUB 2 table of contents for older readers): one XHTML file per chapter, a title page,
/// a navigation document and a simple book stylesheet. Validates with EPUBCheck's structure rules.
/// </summary>
public static class EpubExporter {
    public static void Write(string path, ManuscriptDocument document, ExportOptions options) {
        Write(path, document, options, []);
    }

    /// <param name="warnings">Receives a note when the chosen font is named in the stylesheet but cannot be embedded.</param>
    public static void Write(string path, ManuscriptDocument document, ExportOptions options, List<string> warnings) {
        if (File.Exists(path)) {
            File.Delete(path);
        }
        string id = "urn:uuid:" + Guid.NewGuid().ToString("D");
        using FileStream stream = File.Create(path);
        using ZipArchive zip = new(stream, ZipArchiveMode.Create);

        // The mimetype entry must come first and be stored uncompressed.
        Add(zip, "mimetype", "application/epub+zip", CompressionLevel.NoCompression);
        Add(zip, "META-INF/container.xml", """
            <?xml version="1.0" encoding="UTF-8"?>
            <container version="1.0" xmlns="urn:oasis:names:tc:opendocument:xmlns:container">
              <rootfiles><rootfile full-path="OEBPS/content.opf" media-type="application/oebps-package+xml"/></rootfiles>
            </container>
            """);
        string family = options.EffectiveFontFamily;
        List<(string File, string MediaType)> fonts = EmbedFonts(zip, family, warnings, out string fontFaces);
        Add(zip, "OEBPS/style.css", fontFaces + $$"""
            body { font-family: "{{family.Replace("\"", "")}}", Georgia, serif; line-height: 1.5; margin: 0 5%; }
            h1 { text-align: center; font-weight: normal; margin: 3em 0 1.5em; page-break-before: always; }
            h2.book { text-align: center; font-size: 1.6em; margin-top: 30%; page-break-before: always; }
            p { margin: 0; text-indent: 1.5em; text-align: justify; }
            p.first { text-indent: 0; }
            p.break { text-indent: 0; text-align: center; margin: 1em 0; }
            .title { text-align: center; margin-top: 30%; }
            .title h1 { page-break-before: avoid; font-size: 2em; }
            """);

        List<(string File, string Title)> spine = [];
        if (options.IncludeTitlePage) {
            Add(zip, "OEBPS/title.xhtml", Page(document.Title, $"<div class=\"title\"><h1>{Escape(document.Title)}</h1><p class=\"break\">{Escape(document.Author)}</p></div>"));
            spine.Add(("title.xhtml", document.Title));
        }
        int number = 0;
        foreach (ExportBook book in document.Books) {
            bool bookHeading = book.Title is not null;
            foreach (ExportChapter chapter in book.Chapters) {
                number++;
                string file = $"chapter{number:D3}.xhtml";
                StringBuilder body = new();
                if (bookHeading) {
                    body.Append($"<h2 class=\"book\">{Escape(book.Title!)}</h2>\n");
                    bookHeading = false;
                }
                body.Append($"<h1>{Escape(chapter.Title)}</h1>\n");
                bool first = true;
                foreach (ExportParagraph paragraph in chapter.Paragraphs) {
                    if (paragraph.IsSceneBreak) {
                        body.Append($"<p class=\"break\">{Escape(options.SceneBreak)}</p>\n");
                        first = true;
                        continue;
                    }
                    body.Append(first ? "<p class=\"first\">" : "<p>");
                    foreach (InlineRun run in paragraph.Runs) {
                        string text = Escape(run.Text);
                        body.Append(run.Bold && run.Italic ? $"<strong><em>{text}</em></strong>" : run.Bold ? $"<strong>{text}</strong>" : run.Italic ? $"<em>{text}</em>" : text);
                    }
                    body.Append("</p>\n");
                    first = false;
                }
                Add(zip, "OEBPS/" + file, Page(chapter.Title, body.ToString()));
                spine.Add((file, chapter.Title));
            }
        }

        StringBuilder navItems = new();
        StringBuilder ncxPoints = new();
        int order = 0;
        foreach ((string file, string title) in spine) {
            order++;
            navItems.Append($"      <li><a href=\"{file}\">{Escape(title)}</a></li>\n");
            ncxPoints.Append($"    <navPoint id=\"p{order}\" playOrder=\"{order}\"><navLabel><text>{Escape(title)}</text></navLabel><content src=\"{file}\"/></navPoint>\n");
        }
        Add(zip, "OEBPS/nav.xhtml", $"""
            <?xml version="1.0" encoding="UTF-8"?>
            <!DOCTYPE html>
            <html xmlns="http://www.w3.org/1999/xhtml" xmlns:epub="http://www.idpf.org/2007/ops" lang="en">
            <head><title>Contents</title></head>
            <body>
              <nav epub:type="toc" id="toc"><h1>Contents</h1>
                <ol>
            {navItems}    </ol>
              </nav>
            </body>
            </html>
            """);
        Add(zip, "OEBPS/toc.ncx", $"""
            <?xml version="1.0" encoding="UTF-8"?>
            <ncx xmlns="http://www.daisy.org/z3986/2005/ncx/" version="2005-1">
              <head><meta name="dtb:uid" content="{id}"/></head>
              <docTitle><text>{Escape(document.Title)}</text></docTitle>
              <navMap>
            {ncxPoints}  </navMap>
            </ncx>
            """);

        string manifest = string.Concat(spine.Select((entry, index) => $"    <item id=\"s{index}\" href=\"{entry.File}\" media-type=\"application/xhtml+xml\"/>\n"))
            + string.Concat(fonts.Select((font, index) => $"    <item id=\"f{index}\" href=\"{font.File}\" media-type=\"{font.MediaType}\"/>\n"));
        string itemrefs = string.Concat(spine.Select((_, index) => $"    <itemref idref=\"s{index}\"/>\n"));
        string modified = DateTime.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
        Add(zip, "OEBPS/content.opf", $"""
            <?xml version="1.0" encoding="UTF-8"?>
            <package xmlns="http://www.idpf.org/2007/opf" version="3.0" unique-identifier="bookid">
              <metadata xmlns:dc="http://purl.org/dc/elements/1.1/">
                <dc:identifier id="bookid">{id}</dc:identifier>
                <dc:title>{Escape(document.Title)}</dc:title>
                <dc:creator>{Escape(document.Author)}</dc:creator>
                <dc:language>en</dc:language>
                <meta property="dcterms:modified">{modified}</meta>
              </metadata>
              <manifest>
                <item id="nav" href="nav.xhtml" media-type="application/xhtml+xml" properties="nav"/>
                <item id="ncx" href="toc.ncx" media-type="application/x-dtbncx+xml"/>
                <item id="css" href="style.css" media-type="text/css"/>
            {manifest}  </manifest>
              <spine toc="ncx">
            {itemrefs}  </spine>
            </package>
            """);
    }

    /// <summary>
    /// Puts the font's files into the book with @font-face rules, so readers show it even when it is not installed.
    /// Georgia and other fonts whose licence restricts embedding are only named in the stylesheet.
    /// </summary>
    private static List<(string File, string MediaType)> EmbedFonts(ZipArchive zip, string family, List<string> warnings, out string css) {
        css = "";
        List<(string File, string MediaType)> files = [];
        FontFamilyFaces? faces = FontCatalog.Installed.Find(family);
        if (faces is null) {
            warnings.Add($"The font '{family}' was not found on this PC, so the e-book only names it; readers without it use their own font.");
            return files;
        }
        if (faces.All.Any(static face => face.IsEmbeddingRestricted)) {
            warnings.Add($"The licence of '{family}' does not allow embedding it, so the e-book only names it; readers without it use their own font.");
            return files;
        }
        // Read every face first, so a damaged file leaves no half-embedded font behind.
        List<(bool Bold, bool Italic, string Style, FontFace Face, byte[] Data)> embedded = [];
        try {
            foreach ((bool bold, bool italic, string style) in new[] { (false, false, "regular"), (true, false, "bold"), (false, true, "italic"), (true, true, "bolditalic") }) {
                FontFace? face = faces.Get(bold, italic);
                if (face is not null) {
                    embedded.Add((bold, italic, style, face, FontCatalog.ExtractFace(face)));
                }
            }
        }
        catch (Exception exception) when (exception is InvalidDataException or IOException or UnauthorizedAccessException) {
            warnings.Add($"{exception.Message.TrimEnd('.')}, so the e-book only names '{family}'; readers without it use their own font.");
            return files;
        }
        StringBuilder rules = new();
        foreach ((bool bold, bool italic, string style, FontFace face, byte[] data) in embedded) {
            string extension = face.HasTrueTypeOutlines ? "ttf" : "otf";
            string file = $"fonts/{style}.{extension}";
            using (Stream entry = zip.CreateEntry("OEBPS/" + file, CompressionLevel.Optimal).Open()) {
                entry.Write(data);
            }
            files.Add((file, extension == "ttf" ? "font/ttf" : "font/otf"));
            rules.Append($$"""@font-face { font-family: "{{family.Replace("\"", "")}}"; font-weight: {{(bold ? "bold" : "normal")}}; font-style: {{(italic ? "italic" : "normal")}}; src: url("{{file}}"); }""").Append('\n');
        }
        css = rules.ToString();
        return files;
    }

    private static string Page(string title, string body) {
        return $"""
            <?xml version="1.0" encoding="UTF-8"?>
            <!DOCTYPE html>
            <html xmlns="http://www.w3.org/1999/xhtml" lang="en">
            <head><title>{Escape(title)}</title><link rel="stylesheet" type="text/css" href="style.css"/></head>
            <body>
            {body}
            </body>
            </html>
            """;
    }

    private static string Escape(string text) {
        return WebUtility.HtmlEncode(text);
    }

    private static void Add(ZipArchive zip, string name, string content, CompressionLevel level = CompressionLevel.Optimal) {
        ZipArchiveEntry entry = zip.CreateEntry(name, level);
        using Stream stream = entry.Open();
        byte[] bytes = new UTF8Encoding(false).GetBytes(content);
        stream.Write(bytes);
    }
}