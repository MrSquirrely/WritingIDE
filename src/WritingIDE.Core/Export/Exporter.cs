using System.Text;

using WritingIDE.Core.Analysis;
using WritingIDE.Core.Model;

namespace WritingIDE.Core.Export;

/// <summary>Exports a project in any supported format.</summary>
public static class Exporter {
    public static string Extension(ExportFormat format) {
        return format switch {
            ExportFormat.Docx or ExportFormat.GoogleDocs => ".docx",
            ExportFormat.Epub => ".epub",
            ExportFormat.Pdf => ".pdf",
            _ => ".md"
        };
    }

    public static string FileFilter(ExportFormat format) {
        return format switch {
            ExportFormat.Docx or ExportFormat.GoogleDocs => "Word document (*.docx)|*.docx",
            ExportFormat.Epub => "EPUB e-book (*.epub)|*.epub",
            ExportFormat.Pdf => "PDF (*.pdf)|*.pdf",
            ExportFormat.AiContext => "Markdown (*.md)|*.md|Plain text (*.txt)|*.txt",
            _ => "Markdown (*.md)|*.md|Plain text (*.txt)|*.txt"
        };
    }

    /// <summary>
    /// The file name an export is offered under, without the extension. One chapter reads "Series - Book - Chapter"
    /// ("Novel - Chapter" in a novel), one whole book of a series "Series - Book", and anything else the project's name.
    /// </summary>
    public static string DefaultFileName(NovelProject project, IReadOnlyList<Chapter> chapters) {
        List<string> parts = [project.Name];
        if (chapters.Count == 1) {
            if (project.FindBook(chapters[0].BookId) is Book book) {
                parts.Add(book.Title);
            }
            parts.Add(chapters[0].Title);
        }
        else if (project.IsSeries && (chapters.Count > 0) && (project.FindBook(chapters[0].BookId) is Book book)
            && chapters.All(chapter => chapter.BookId == book.Id) && (chapters.Count == project.GetChapters(book).Count())) {
            parts.Add(book.Title);
        }
        char[] invalid = Path.GetInvalidFileNameChars();
        IEnumerable<string> clean = parts
            .Select(part => string.Join(" ", new string(part.Select(character => invalid.Contains(character) ? ' ' : character).ToArray()).Split(' ', StringSplitOptions.RemoveEmptyEntries)).Trim('.', ' '))
            .Where(static part => part.Length > 0);
        string name = string.Join(" - ", clean);
        return name.Length == 0 ? "Export" : name;
    }

    /// <returns>Notes about the export, such as a font that could not be embedded; usually empty.</returns>
    public static IReadOnlyList<string> Export(string path, NovelProject project, ExportOptions options, Func<Chapter, string> getText) {
        List<string> warnings = [];
        switch (options.Format) {
            case ExportFormat.AiContext:
                File.WriteAllText(path, AiContextExporter.Build(project, options, getText), Encoding.UTF8);
                return warnings;
            case ExportFormat.Markdown:
                File.WriteAllText(path, Markdown(project, options, getText), Encoding.UTF8);
                return warnings;
        }
        ManuscriptDocument document = ManuscriptDocumentBuilder.Build(project, options.Chapters, getText);
        switch (options.Format) {
            case ExportFormat.Docx:
                DocxExporter.Write(path, document, options);
                WarnIfNotInstalled(options, warnings);
                break;
            case ExportFormat.GoogleDocs:
                // Google Docs keeps Heading 1 chapters (its outline) and page breaks; book styling converts best.
                DocxExporter.Write(path, document, options with { Style = ExportStyle.Book });
                WarnIfNotInstalled(options with { Style = ExportStyle.Book }, warnings);
                break;
            case ExportFormat.Epub:
                EpubExporter.Write(path, document, options, warnings);
                break;
            case ExportFormat.Pdf:
                PdfExporter.Write(path, document, options, warnings);
                break;
        }
        return warnings;
    }

    /// <summary>A Word file only names its font: say so when the font exists only inside WritingIDE (or not at all).</summary>
    private static void WarnIfNotInstalled(ExportOptions options, List<string> warnings) {
        string family = options.EffectiveFontFamily;
        FontFamilyFaces? faces = FontCatalog.Installed.Find(family);
        if (faces is null) {
            warnings.Add($"The font '{family}' was not found on this PC; Word shows the document in its default font until it is installed.");
        }
        else if (faces.All.All(static face => face.Path.StartsWith(FontCatalog.AppFontsFolder, StringComparison.OrdinalIgnoreCase))) {
            warnings.Add($"'{family}' was added to WritingIDE only. Word shows it on PCs where the font is installed; elsewhere it uses a default font.");
        }
    }

    private static string Markdown(NovelProject project, ExportOptions options, Func<Chapter, string> getText) {
        StringBuilder builder = new();
        builder.AppendLine($"# {project.Name}");
        if (project.Author.Length > 0) {
            builder.AppendLine().AppendLine($"*by {project.Author}*");
        }
        Guid? book = Guid.Empty;
        foreach (Chapter chapter in options.Chapters) {
            if (project.IsSeries && (chapter.BookId != book)) {
                book = chapter.BookId;
                builder.AppendLine().AppendLine($"# {project.FindBook(book)?.Title}");
            }
            builder.AppendLine().AppendLine($"## {chapter.Title}").AppendLine().AppendLine(getText(chapter).Trim());
        }
        return builder.ToString();
    }
}

/// <summary>
/// "Export for AI": one Markdown file that gives an AI chat everything it needs about the book (the story bible,
/// every story element with its attributes and relationships, plot threads, chapter synopses, story dates and POV,
/// and optionally the full text), with instructions at the top. Paste it into a chat or attach it.
/// </summary>
public static class AiContextExporter {
    public static string Build(NovelProject project, ExportOptions options, Func<Chapter, string> getText) {
        StringBuilder builder = new();
        builder.AppendLine($"# {project.Name}: project context for an AI assistant");
        builder.AppendLine();
        builder.AppendLine("You are helping the author of this novel. Below is the story bible, every story element, the plot threads and the "
            + (options.IncludeFullText ? "full manuscript" : "chapter synopses")
            + ". Treat it as canon: point out contradictions with it rather than inventing new facts, and quote the text when you refer to it.");
        if (options.AiInstructions.Trim().Length > 0) {
            builder.AppendLine().AppendLine("## What I want from you").AppendLine().AppendLine(options.AiInstructions.Trim());
        }

        builder.AppendLine().AppendLine("## Overview").AppendLine();
        builder.AppendLine($"- Title: {project.Name}");
        if (project.Author.Length > 0) {
            builder.AppendLine($"- Author: {project.Author}");
        }
        if (project.Genre.Length > 0) {
            builder.AppendLine($"- Genre: {project.Genre}");
        }
        builder.AppendLine($"- {(project.IsSeries ? $"Series of {project.Books.Count} books" : "Single novel")}, {options.Chapters.Count} chapters, about {options.Chapters.Sum(chapter => TextStatistics.CountWords(getText(chapter))):N0} words");
        if (project.Synopsis.Length > 0) {
            builder.AppendLine().AppendLine(project.Synopsis.Trim());
        }
        if (project.Notes.Trim().Length > 0) {
            builder.AppendLine().AppendLine(project.IsSeries ? "## Series bible" : "## Story bible").AppendLine().AppendLine(project.Notes.Trim());
        }

        foreach (EntityKindInfo kind in EntityKindInfo.All) {
            List<StoryEntity> entities = project.GetEntities(kind.Kind).ToList();
            if (entities.Count == 0) {
                continue;
            }
            builder.AppendLine().AppendLine($"## {kind.PluralName}");
            foreach (StoryEntity entity in entities) {
                builder.AppendLine().AppendLine($"### {entity.Name}");
                if (entity.AliasList.Count > 0) {
                    builder.AppendLine($"- Also called: {string.Join(", ", entity.AliasList)}");
                }
                foreach (EntityAttribute attribute in entity.Attributes.Where(static attribute => attribute.Value.Trim().Length > 0)) {
                    builder.AppendLine($"- {attribute.Key}: {attribute.Value.Trim()}");
                }
                foreach (EntityRelationship relationship in entity.Relationships) {
                    string target = project.FindEntity(relationship.TargetId)?.Name ?? "?";
                    builder.AppendLine($"- {(relationship.Label.Length > 0 ? relationship.Label : "related to")} {target}");
                }
                if (entity.Summary.Trim().Length > 0) {
                    builder.AppendLine().AppendLine(entity.Summary.Trim());
                }
                if (entity.Notes.Trim().Length > 0) {
                    builder.AppendLine().AppendLine(entity.Notes.Trim());
                }
            }
        }

        if (project.PlotThreads.Count > 0) {
            builder.AppendLine().AppendLine("## Plot threads and foreshadowing").AppendLine();
            foreach (PlotThread thread in project.PlotThreads) {
                builder.AppendLine($"- **{thread.Name}** ({thread.Status}){(thread.Description.Length > 0 ? ": " + thread.Description.Trim() : "")}");
                foreach (Chapter chapter in options.Chapters) {
                    foreach (ChapterComment marker in chapter.Comments.Where(marker => (marker.Kind == MarkerKind.PlotThread) && (marker.ThreadId == thread.Id))) {
                        builder.AppendLine($"  - {marker.ThreadRole} in {chapter.Title}: \u201C{marker.AnchorText.Replace('\n', ' ').Trim()}\u201D");
                    }
                }
            }
        }

        builder.AppendLine().AppendLine(options.IncludeFullText ? "## Manuscript" : "## Chapters");
        Guid? book = Guid.Empty;
        int number = 0;
        foreach (Chapter chapter in options.Chapters) {
            number++;
            if (project.IsSeries && (chapter.BookId != book)) {
                book = chapter.BookId;
                builder.AppendLine().AppendLine($"### Book: {project.FindBook(book)?.Title}");
            }
            builder.AppendLine().AppendLine($"### {number}. {chapter.Title}");
            List<string> facts = [];
            if (chapter.StoryDate.Length > 0) {
                facts.Add($"Story date: {chapter.StoryDate}");
            }
            if (chapter.PointOfView.Length > 0) {
                facts.Add($"POV: {chapter.PointOfView}");
            }
            facts.Add($"Status: {chapter.Status}");
            builder.AppendLine($"*{string.Join(" \u00B7 ", facts)}*");
            if (chapter.Synopsis.Trim().Length > 0) {
                builder.AppendLine().AppendLine($"Synopsis: {chapter.Synopsis.Trim()}");
            }
            if (options.IncludeFullText) {
                builder.AppendLine().AppendLine(getText(chapter).Trim());
            }
        }
        return builder.ToString();
    }

    /// <summary>A rough token estimate (about four characters per token for English prose).</summary>
    public static int EstimateTokens(string text) {
        return text.Length / 4;
    }
}