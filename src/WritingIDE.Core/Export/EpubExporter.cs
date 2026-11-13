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
        Add(zip, "OEBPS/style.css", """
            body { font-family: Georgia, serif; line-height: 1.5; margin: 0 5%; }
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
                        body.Append(run.Bold ? $"<strong>{text}</strong>" : run.Italic ? $"<em>{text}</em>" : text);
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

        string manifest = string.Concat(spine.Select((entry, index) => $"    <item id=\"s{index}\" href=\"{entry.File}\" media-type=\"application/xhtml+xml\"/>\n"));
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