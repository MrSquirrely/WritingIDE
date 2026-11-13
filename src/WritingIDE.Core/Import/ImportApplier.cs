using WritingIDE.Core.Model;

namespace WritingIDE.Core.Import;

/// <summary>Merges a parsed <see cref="ImportDocument"/> into a project.</summary>
public static class ImportApplier {
    public static ImportSummary Apply(NovelProject project, ImportDocument document, ImportConflictMode mode, Action<Chapter, string>? setChapterText = null) {
        return Apply(project, document, new ImportOptions(mode), setChapterText);
    }

    /// <param name="setChapterText">Replaces the text of an existing chapter (the IDE routes this through open editors). Defaults to setting <see cref="Chapter.Text"/>.</param>
    public static ImportSummary Apply(NovelProject project, ImportDocument document, ImportOptions options, Action<Chapter, string>? setChapterText = null) {
        setChapterText ??= static (chapter, text) => chapter.Text = text;
        ImportConflictMode mode = options.Mode;
        int chaptersAdded = 0;
        int chaptersUpdated = 0;
        int entitiesAdded = 0;
        int entitiesUpdated = 0;
        int booksAdded = 0;
        int skipped = 0;
        bool notesUpdated = false;
        List<Chapter> touched = [];

        if (options.ConvertToSeries && !project.IsSeries) {
            project.ConvertToSeries(document.Books.FirstOrDefault()?.Title ?? $"{project.Name}, Book 1");
        }

        if (document.Novel is not null) {
            ApplyNovelInfo(project, document.Novel, overwrite: mode == ImportConflictMode.Update);
            notesUpdated = MergeNotes(project, document.Novel.Notes);
        }

        // Books only exist in a series; in a single novel their chapters are imported flat.
        Dictionary<string, Book> books = new(StringComparer.OrdinalIgnoreCase);
        if (project.IsSeries) {
            foreach (Book book in project.Books) {
                books.TryAdd(book.Title, book);
            }
            foreach (ImportedBook imported in document.Books) {
                if (!books.TryGetValue(imported.Title, out Book? book)) {
                    book = new Book(imported.Title) { Synopsis = imported.Synopsis };
                    project.Books.Add(book);
                    books[imported.Title] = book;
                    booksAdded++;
                }
                else if ((imported.Synopsis.Length > 0) && ((mode == ImportConflictMode.Update) || (book.Synopsis.Length == 0))) {
                    book.Synopsis = imported.Synopsis;
                }
            }
        }
        // A book's own bible material goes into the Series Bible under the book's title.
        foreach (ImportedBook imported in document.Books.Where(static book => book.Notes.Length > 0)) {
            notesUpdated |= MergeNotes(project, $"Book: {imported.Title}{Environment.NewLine}{imported.Notes}");
        }
        Book? defaultBook = !project.IsSeries ? null
            : (options.DefaultBook is not null) && project.Books.Contains(options.DefaultBook) ? options.DefaultBook
            : project.Books.LastOrDefault();
        if (project.IsSeries && (defaultBook is null) && document.Chapters.Any(static chapter => chapter.BookTitle is null)) {
            defaultBook = new Book($"{project.Name}, Book 1");
            project.Books.Add(defaultBook);
            booksAdded++;
        }

        foreach (ImportedEntity imported in document.Entities) {
            StoryEntity? existing = mode == ImportConflictMode.AddNew ? null : FindEntity(project, imported);
            if (existing is null) {
                StoryEntity entity = StoryEntity.CreateWithDefaults(imported.Kind, imported.Name);
                Merge(entity, imported);
                project.Entities.Add(entity);
                entitiesAdded++;
            }
            else if (mode == ImportConflictMode.Skip) {
                skipped++;
            }
            else {
                Merge(existing, imported);
                entitiesUpdated++;
            }
        }

        int planEntries = 0;
        foreach (ImportedChapter imported in document.Chapters) {
            if (options.OutlineChaptersToNotes && IsPlan(imported)) {
                // A planned chapter from a bible is a note in the Story/Series Bible, not a chapter of the manuscript.
                if (MergeNotes(project, FormatChapterPlan(imported.Title, imported.BookTitle, imported.Synopsis, imported.Text))) {
                    planEntries++;
                }
                continue;
            }
            Book? book = null;
            if (project.IsSeries) {
                book = (imported.BookTitle is not null) && books.TryGetValue(imported.BookTitle, out Book? named) ? named : defaultBook;
                if ((book is null) && (imported.BookTitle is not null)) {
                    book = new Book(imported.BookTitle);
                    project.Books.Add(book);
                    books[imported.BookTitle] = book;
                    booksAdded++;
                }
            }
            // Chapter titles repeat across books ("Chapter 1"), so matching is per book.
            Chapter? existing = project.GetChapters(book).FirstOrDefault(chapter => chapter.Title.Equals(imported.Title, StringComparison.OrdinalIgnoreCase));
            if ((existing is not null) && (mode == ImportConflictMode.Skip)) {
                skipped++;
                continue;
            }
            if ((existing is not null) && (mode == ImportConflictMode.Update)) {
                setChapterText(existing, imported.Text);
                if (imported.Synopsis.Length > 0) {
                    existing.Synopsis = imported.Synopsis;
                }
                if (imported.Status is not null) {
                    existing.Status = imported.Status.Value;
                }
                touched.Add(existing);
                chaptersUpdated++;
                continue;
            }
            string title = existing is null ? imported.Title : CreateUniqueTitle(project, book, imported.Title);
            Chapter chapter = new(title) {
                BookId = book?.Id,
                Text = imported.Text,
                Synopsis = imported.Synopsis,
                Status = imported.Status ?? ChapterStatus.Draft
            };
            project.Chapters.Add(chapter);
            touched.Add(chapter);
            chaptersAdded++;
        }

        return new ImportSummary(chaptersAdded, chaptersUpdated, entitiesAdded, entitiesUpdated, skipped) {
            Chapters = touched,
            BooksAdded = booksAdded,
            NotesUpdated = notesUpdated,
            PlanEntries = planEntries
        };
    }

    /// <summary>True for an outline chapter: a plan of beats rather than prose.</summary>
    public static bool IsPlan(ImportedChapter chapter) {
        return chapter.Status == ChapterStatus.Outline;
    }

    /// <summary>Formats a planned chapter as a section of the story notes.</summary>
    public static string FormatChapterPlan(string title, string? bookTitle, string synopsis, string text) {
        List<string> lines = [$"Chapter plan: {title}{(string.IsNullOrWhiteSpace(bookTitle) ? "" : $" ({bookTitle})")}"];
        if (!string.IsNullOrWhiteSpace(synopsis)) {
            lines.Add(synopsis.Trim());
        }
        if (!string.IsNullOrWhiteSpace(text)) {
            // One line per beat: the blank lines between beats are only needed in chapter text.
            lines.AddRange(text.Replace("\r\n", "\n").Split('\n').Select(static line => line.TrimEnd()).Where(static line => line.Length > 0));
        }
        return string.Join(Environment.NewLine, lines);
    }

    /// <summary>Appends bible notes to the project's story notes unless they are already there.</summary>
    private static bool MergeNotes(NovelProject project, string notes) {
        if ((notes.Length == 0) || project.Notes.Contains(notes, StringComparison.Ordinal)) {
            return false;
        }
        project.Notes = project.Notes.Length == 0 ? notes : project.Notes.TrimEnd() + Environment.NewLine + Environment.NewLine + notes;
        return true;
    }

    /// <summary>Finds an existing element of the same kind whose name or alias matches the imported name.</summary>
    public static StoryEntity? FindEntity(NovelProject project, ImportedEntity imported) {
        return project.GetEntities(imported.Kind).FirstOrDefault(entity => entity.Name.Equals(imported.Name, StringComparison.OrdinalIgnoreCase))
            ?? project.GetEntities(imported.Kind).FirstOrDefault(entity => entity.AliasList.Contains(imported.Name, StringComparer.OrdinalIgnoreCase));
    }

    private static void Merge(StoryEntity entity, ImportedEntity imported) {
        IEnumerable<string> aliases = entity.AliasList
            .Concat(imported.Aliases.Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .Where(alias => !alias.Equals(entity.Name, StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase);
        entity.Aliases = string.Join(", ", aliases);
        if (imported.Summary.Length > 0) {
            entity.Summary = imported.Summary;
        }
        if (imported.Notes.Length > 0) {
            if (entity.Notes.Length == 0) {
                entity.Notes = imported.Notes;
            }
            else if (!entity.Notes.Contains(imported.Notes, StringComparison.Ordinal)) {
                entity.Notes = entity.Notes.TrimEnd() + Environment.NewLine + Environment.NewLine + imported.Notes;
            }
        }
        foreach ((string key, string value) in imported.Attributes) {
            EntityAttribute? attribute = entity.Attributes.FirstOrDefault(a => a.Key.Equals(key, StringComparison.OrdinalIgnoreCase));
            if (attribute is null) {
                entity.Attributes.Add(new EntityAttribute(key, value));
            }
            else if (value.Length > 0) {
                attribute.Value = value;
            }
        }
    }

    private static void ApplyNovelInfo(NovelProject project, ImportedNovelInfo info, bool overwrite) {
        static bool ShouldSet(string current, string? incoming, bool overwrite) {
            return !string.IsNullOrWhiteSpace(incoming) && (overwrite || string.IsNullOrWhiteSpace(current));
        }

        // The project name is tied to its file and folder, so it is never changed by an import.
        if (ShouldSet(project.Author, info.Author, overwrite)) {
            project.Author = info.Author!;
        }
        if (ShouldSet(project.Genre, info.Genre, overwrite)) {
            project.Genre = info.Genre!;
        }
        if (ShouldSet(project.Synopsis, info.Synopsis, overwrite)) {
            project.Synopsis = info.Synopsis!;
        }
        if ((info.TargetWordCount is int target) && overwrite) {
            project.TargetWordCount = target;
        }
    }

    private static string CreateUniqueTitle(NovelProject project, Book? book, string title) {
        string candidate = $"{title} (imported)";
        int counter = 2;
        while (project.GetChapters(book).Any(chapter => chapter.Title.Equals(candidate, StringComparison.OrdinalIgnoreCase))) {
            candidate = $"{title} (imported {counter++})";
        }
        return candidate;
    }
}