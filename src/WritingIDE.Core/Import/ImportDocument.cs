using WritingIDE.Core.Model;

namespace WritingIDE.Core.Import;

/// <summary>Project-level metadata found in a NOVEL or SERIES block.</summary>
public sealed record ImportedNovelInfo(string? Title, string? Author, string? Genre, string? Synopsis, int? TargetWordCount) {
    /// <summary>Story-bible notes: premise, tone, themes, world rules, plot plan.</summary>
    public string Notes { get; init; } = "";

    /// <summary>True when the block was a SERIES block.</summary>
    public bool IsSeries { get; init; }
}

/// <summary>A BOOK block. Chapter blocks that follow it belong to it.</summary>
public sealed record ImportedBook(string Title, string Synopsis);

public sealed record ImportedChapter(string Title, string Synopsis, ChapterStatus? Status, string Text) {
    /// <summary>Title of the BOOK block this chapter follows, if any.</summary>
    public string? BookTitle { get; init; }
}

public sealed record ImportedEntity(EntityKind Kind, string Name, string Aliases, string Summary, string Notes, IReadOnlyList<KeyValuePair<string, string>> Attributes);

/// <summary>A problem found while parsing; the line is 1-based.</summary>
public sealed record ImportWarning(int Line, string Message);

/// <summary>The parsed content of an import, before it is applied to a project.</summary>
public sealed class ImportDocument {
    public ImportedNovelInfo? Novel { get; set; }

    public List<ImportedBook> Books { get; } = [];

    public List<ImportedChapter> Chapters { get; } = [];

    public List<ImportedEntity> Entities { get; } = [];

    public List<ImportWarning> Warnings { get; } = [];

    /// <summary>True when the text had no WritingIDE blocks and was imported as a plain manuscript.</summary>
    public bool IsPlainText { get; set; }

    /// <summary>True when the reply ended with a CONTINUE marker: more parts are expected.</summary>
    public bool IsPartial { get; set; }

    public bool IsEmpty => (Novel is null) && (Books.Count == 0) && (Chapters.Count == 0) && (Entities.Count == 0);
}

/// <summary>What to do when an imported item has the same name as an existing one.</summary>
public enum ImportConflictMode {
    /// <summary>Merge story elements (fill and overwrite fields) and replace chapter text.</summary>
    Update,

    /// <summary>Keep the existing item and ignore the imported one.</summary>
    Skip,

    /// <summary>Add the imported item alongside the existing one.</summary>
    AddNew
}

/// <summary>How an import is applied.</summary>
public sealed record ImportOptions(ImportConflictMode Mode) {
    /// <summary>In a series, the book that receives chapters which do not name a book.</summary>
    public Book? DefaultBook { get; init; }

    /// <summary>Convert a single-novel project into a series before importing (when the import contains books).</summary>
    public bool ConvertToSeries { get; init; }

    /// <summary>Put chapters with Status: Outline into the Story/Series Bible notes as a chapter plan instead of creating chapters.</summary>
    public bool OutlineChaptersToNotes { get; init; }
}

/// <summary>Counts reported after an import.</summary>
public sealed record ImportSummary(int ChaptersAdded, int ChaptersUpdated, int EntitiesAdded, int EntitiesUpdated, int Skipped) {
    public int BooksAdded { get; init; }

    public bool NotesUpdated { get; init; }

    /// <summary>Outline chapters added to the story notes as a chapter plan.</summary>
    public int PlanEntries { get; init; }

    public int Total => ChaptersAdded + ChaptersUpdated + EntitiesAdded + EntitiesUpdated + BooksAdded + PlanEntries + (NotesUpdated ? 1 : 0);

    /// <summary>Chapters that were added or whose text was replaced, in import order.</summary>
    public IReadOnlyList<Chapter> Chapters { get; init; } = [];

    public override string ToString() {
        List<string> parts = [];
        if (BooksAdded > 0) {
            parts.Add($"{BooksAdded} book(s) added");
        }
        if (ChaptersAdded > 0) {
            parts.Add($"{ChaptersAdded} chapter(s) added");
        }
        if (ChaptersUpdated > 0) {
            parts.Add($"{ChaptersUpdated} chapter(s) updated");
        }
        if (EntitiesAdded > 0) {
            parts.Add($"{EntitiesAdded} story element(s) added");
        }
        if (EntitiesUpdated > 0) {
            parts.Add($"{EntitiesUpdated} story element(s) updated");
        }
        if (NotesUpdated) {
            parts.Add("story notes updated");
        }
        if (PlanEntries > 0) {
            parts.Add($"{PlanEntries} chapter plan(s) added to the story notes");
        }
        if (Skipped > 0) {
            parts.Add($"{Skipped} skipped");
        }
        return parts.Count == 0 ? "Nothing imported" : string.Join(", ", parts);
    }
}