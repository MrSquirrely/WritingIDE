using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

using WritingIDE.Core.Model;

namespace WritingIDE.Core.Import;

/// <summary>
/// Parses the WritingIDE import format:
/// <code>
/// === SERIES: The Saga ===          (or NOVEL)
/// Author: ...
/// Notes:
/// Premise, tone, world rules...
///
/// === CHARACTER: Elena Marsh ===
/// Aliases: Elena
/// Summary: Keeper of the light.
/// Role: Protagonist
/// Notes:
/// Free text...
///
/// === BOOK: Book One ===            (series only; chapters below belong to it)
/// === CHAPTER: Chapter 1 ===
/// Synopsis: ...
/// ---
/// Chapter text...
///
/// === CONTINUE ===                  (the AI stopped; more parts follow)
/// </code>
/// Text without any blocks is imported as a plain manuscript: Markdown "# Book" / "## Chapter" headings
/// (or lines such as "Chapter 3" and "Prologue") become books and chapters.
/// </summary>
public static partial class ImportParser {
    private enum BlockType {
        Novel,
        Series,
        Book,
        Chapter,
        Entity,
        Continue
    }

    private sealed record Block(BlockType Type, EntityKind Kind, string Name, int Line, List<string> Lines);

    private static readonly Dictionary<string, (BlockType Type, EntityKind Kind)> BlockTypes = new(StringComparer.OrdinalIgnoreCase) {
        ["novel"] = (BlockType.Novel, default),
        ["project"] = (BlockType.Novel, default),
        ["story"] = (BlockType.Novel, default),
        ["series"] = (BlockType.Series, default),
        ["saga"] = (BlockType.Series, default),
        ["book"] = (BlockType.Book, default),
        ["volume"] = (BlockType.Book, default),
        ["chapter"] = (BlockType.Chapter, default),
        ["scene"] = (BlockType.Chapter, default),
        ["prologue"] = (BlockType.Chapter, default),
        ["epilogue"] = (BlockType.Chapter, default),
        ["continue"] = (BlockType.Continue, default),
        ["continued"] = (BlockType.Continue, default),
        ["character"] = (BlockType.Entity, EntityKind.Character),
        ["person"] = (BlockType.Entity, EntityKind.Character),
        ["place"] = (BlockType.Entity, EntityKind.Place),
        ["location"] = (BlockType.Entity, EntityKind.Place),
        ["setting"] = (BlockType.Entity, EntityKind.Place),
        ["item"] = (BlockType.Entity, EntityKind.Item),
        ["object"] = (BlockType.Entity, EntityKind.Item),
        ["artifact"] = (BlockType.Entity, EntityKind.Item),
        ["artefact"] = (BlockType.Entity, EntityKind.Item),
        ["faction"] = (BlockType.Entity, EntityKind.Faction),
        ["group"] = (BlockType.Entity, EntityKind.Faction),
        ["organization"] = (BlockType.Entity, EntityKind.Faction),
        ["organisation"] = (BlockType.Entity, EntityKind.Faction),
        ["family"] = (BlockType.Entity, EntityKind.Faction),
        ["event"] = (BlockType.Entity, EntityKind.Event),
        ["lore"] = (BlockType.Entity, EntityKind.Lore),
        ["concept"] = (BlockType.Entity, EntityKind.Lore),
        ["term"] = (BlockType.Entity, EntityKind.Lore),
        ["magic"] = (BlockType.Entity, EntityKind.Lore),
        ["creature"] = (BlockType.Entity, EntityKind.Lore),
        ["custom"] = (BlockType.Entity, EntityKind.Lore)
    };

    private static readonly HashSet<string> AliasKeys = new(StringComparer.OrdinalIgnoreCase) { "aliases", "alias", "aka", "also known as", "nicknames", "nickname", "other names" };

    private static readonly HashSet<string> SummaryKeys = new(StringComparer.OrdinalIgnoreCase) { "summary", "description", "short description", "one-line summary", "logline" };

    private static readonly HashSet<string> NotesKeys = new(StringComparer.OrdinalIgnoreCase) { "notes", "note", "details", "backstory", "background" };

    public static ImportDocument Parse(string text, string fallbackChapterTitle = "Imported Chapter") {
        ImportDocument document = new();
        List<string> lines = Normalize(text);
        List<Block> blocks = [];
        List<string> preamble = [];
        Block? current = null;
        bool hasEqualsHeaders = lines.Any(line => line.TrimStart().StartsWith("==", StringComparison.Ordinal) && BlockHeader().IsMatch(line));

        for (int i = 0; i < lines.Count; i++) {
            Match header = BlockHeader().Match(lines[i]);
            if (header.Success && !IsAcceptedHeader(lines[i], header, hasEqualsHeaders)) {
                header = Match.Empty;
            }
            if (header.Success) {
                string typeName = header.Groups["type"].Value;
                if (BlockTypes.TryGetValue(typeName, out (BlockType Type, EntityKind Kind) type)) {
                    current = new Block(type.Type, type.Kind, CleanName(header.Groups["name"].Value), i + 1, []);
                    blocks.Add(current);
                    continue;
                }
                if (lines[i].TrimStart().StartsWith('=')) {
                    document.Warnings.Add(new ImportWarning(i + 1, $"Unknown block type '{typeName}'; its content was ignored."));
                    current = null;
                    continue;
                }
            }
            if (current is null) {
                preamble.Add(lines[i]);
            }
            else {
                current.Lines.Add(lines[i]);
            }
        }

        if (blocks.Count == 0) {
            ParsePlainText(document, lines, fallbackChapterTitle);
            return document;
        }
        if (preamble.Any(static line => !string.IsNullOrWhiteSpace(line))) {
            document.Warnings.Add(new ImportWarning(1, "Text before the first block was ignored."));
        }

        string? currentBook = null;
        foreach (Block block in blocks) {
            switch (block.Type) {
                case BlockType.Novel:
                case BlockType.Series:
                    ParseNovel(document, block);
                    break;
                case BlockType.Book:
                    currentBook = ParseBook(document, block);
                    break;
                case BlockType.Chapter:
                    ParseChapter(document, block, currentBook);
                    break;
                case BlockType.Continue:
                    document.IsPartial = true;
                    break;
                default:
                    ParseEntity(document, block);
                    break;
            }
        }
        return document;
    }

    /// <summary>
    /// "=== TYPE: Name ===" headers are always accepted. Markdown-style "### TYPE: Name" headers need the colon, and
    /// chapter-like ones ("## Prologue: ...") only count when the text also uses "===" headers, so ordinary
    /// Markdown manuscripts are not mistaken for the block format.
    /// </summary>
    private static bool IsAcceptedHeader(string line, Match header, bool hasEqualsHeaders) {
        if (line.TrimStart().StartsWith('=')) {
            return true;
        }
        if (!header.Groups["colon"].Success) {
            return false;
        }
        if (hasEqualsHeaders) {
            return true;
        }
        return !BlockTypes.TryGetValue(header.Groups["type"].Value, out (BlockType Type, EntityKind Kind) type)
            || type.Type is not (BlockType.Chapter or BlockType.Continue);
    }

    private static void ParseNovel(ImportDocument document, Block block) {
        string? title = block.Name.Length > 0 ? block.Name : null;
        string? author = null;
        string? genre = null;
        string? synopsis = null;
        int? target = null;
        StringBuilder notes = new();
        bool inNotes = false;
        foreach (string line in block.Lines) {
            if (inNotes) {
                notes.AppendLine(line.TrimEnd());
                continue;
            }
            if (!TryParseField(line, out string key, out string value)) {
                if (!string.IsNullOrWhiteSpace(line)) {
                    notes.AppendLine(line.TrimEnd());
                }
                continue;
            }
            switch (key.ToLowerInvariant()) {
                case "title":
                case "name":
                    title = value;
                    break;
                case "author":
                    author = value;
                    break;
                case "genre":
                    genre = value;
                    break;
                case "synopsis":
                case "summary":
                case "logline":
                    synopsis = value;
                    break;
                case "notes":
                case "bible":
                case "story notes":
                    inNotes = true;
                    if (value.Length > 0) {
                        notes.AppendLine(value);
                    }
                    break;
                case "target":
                case "target words":
                case "target word count":
                case "word count":
                case "words":
                    string digits = new(value.Where(char.IsDigit).ToArray());
                    if (int.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out int parsed) && (parsed > 0)) {
                        target = parsed;
                    }
                    else {
                        document.Warnings.Add(new ImportWarning(block.Line, $"Could not read the target word count '{value}'."));
                    }
                    break;
                default:
                    // Unknown "Key: value" lines (Tone, Themes, Heat level...) are part of the bible notes.
                    notes.AppendLine($"{key}: {value}");
                    break;
            }
        }
        string notesText = notes.ToString().Trim();
        if (document.Novel is not null) {
            // Several NOVEL/SERIES blocks (e.g. one per reply part) are merged.
            ImportedNovelInfo previous = document.Novel;
            document.Novel = new ImportedNovelInfo(title ?? previous.Title, author ?? previous.Author, genre ?? previous.Genre, synopsis ?? previous.Synopsis, target ?? previous.TargetWordCount) {
                Notes = string.Join(Environment.NewLine + Environment.NewLine, new[] { previous.Notes, notesText }.Where(static part => part.Length > 0)),
                IsSeries = previous.IsSeries || (block.Type == BlockType.Series)
            };
            return;
        }
        document.Novel = new ImportedNovelInfo(title, author, genre, synopsis, target) {
            Notes = notesText,
            IsSeries = block.Type == BlockType.Series
        };
    }

    private static string? ParseBook(ImportDocument document, Block block) {
        if (block.Name.Length == 0) {
            document.Warnings.Add(new ImportWarning(block.Line, "A BOOK block has no title; its chapters go to the default book."));
            return null;
        }
        string synopsis = "";
        foreach (string line in block.Lines) {
            if (TryParseField(line, out string key, out string value) && (SummaryKeys.Contains(key) || key.Equals("synopsis", StringComparison.OrdinalIgnoreCase))) {
                synopsis = value;
            }
        }
        if (!document.Books.Any(book => book.Title.Equals(block.Name, StringComparison.OrdinalIgnoreCase))) {
            document.Books.Add(new ImportedBook(block.Name, synopsis));
        }
        return block.Name;
    }

    private static void ParseChapter(ImportDocument document, Block block, string? bookTitle) {
        string title = block.Name.Length > 0 ? block.Name : $"Chapter {document.Chapters.Count + 1}";
        string synopsis = "";
        ChapterStatus? status = null;
        int bodyStart = 0;
        bool hasSeparator = block.Lines.Any(static line => IsSeparator(line));
        for (int i = 0; i < block.Lines.Count; i++) {
            string line = block.Lines[i];
            if (IsSeparator(line)) {
                bodyStart = i + 1;
                break;
            }
            if (string.IsNullOrWhiteSpace(line)) {
                bodyStart = i + 1;
                continue;
            }
            if (TryParseField(line, out string key, out string value)) {
                if (key.Equals("synopsis", StringComparison.OrdinalIgnoreCase) || key.Equals("summary", StringComparison.OrdinalIgnoreCase)) {
                    synopsis = value;
                    bodyStart = i + 1;
                    continue;
                }
                if (key.Equals("status", StringComparison.OrdinalIgnoreCase)) {
                    if (Enum.TryParse(value.Trim(), ignoreCase: true, out ChapterStatus parsed)) {
                        status = parsed;
                    }
                    else {
                        document.Warnings.Add(new ImportWarning(block.Line, $"Unknown status '{value}' for '{title}'. Use Outline, Draft, Revised or Final."));
                    }
                    bodyStart = i + 1;
                    continue;
                }
                if (key.Equals("book", StringComparison.OrdinalIgnoreCase)) {
                    bookTitle = value;
                    bodyStart = i + 1;
                    continue;
                }
                if (hasSeparator) {
                    document.Warnings.Add(new ImportWarning(block.Line, $"Unknown chapter field '{key}' in '{title}' was ignored."));
                    bodyStart = i + 1;
                    continue;
                }
            }
            bodyStart = i;
            break;
        }
        string body = JoinBody(block.Lines.Skip(bodyStart));
        if (body.Length == 0) {
            document.Warnings.Add(new ImportWarning(block.Line, $"Chapter '{title}' has no text."));
        }
        document.Chapters.Add(new ImportedChapter(title, synopsis, status, body) { BookTitle = bookTitle });
    }

    private static void ParseEntity(ImportDocument document, Block block) {
        string kindName = EntityKindInfo.Get(block.Kind).DisplayName;
        if (block.Name.Length == 0) {
            document.Warnings.Add(new ImportWarning(block.Line, $"A {kindName.ToLowerInvariant()} block has no name and was skipped."));
            return;
        }
        List<string> aliases = [];
        string summary = "";
        StringBuilder notes = new();
        List<KeyValuePair<string, string>> attributes = [];
        bool inNotes = false;
        foreach (string line in block.Lines) {
            if (inNotes) {
                notes.AppendLine(line.TrimEnd());
                continue;
            }
            if (string.IsNullOrWhiteSpace(line)) {
                continue;
            }
            if (!TryParseField(line, out string key, out string value)) {
                notes.AppendLine(StripMarkdown(line).Trim());
                continue;
            }
            if (AliasKeys.Contains(key)) {
                aliases.AddRange(value.Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
            }
            else if (SummaryKeys.Contains(key)) {
                summary = value;
            }
            else if (NotesKeys.Contains(key)) {
                inNotes = true;
                if (value.Length > 0) {
                    notes.AppendLine(value);
                }
            }
            else {
                attributes.Add(new KeyValuePair<string, string>(key, value));
            }
        }
        string aliasText = string.Join(", ", aliases.Where(alias => !alias.Equals(block.Name, StringComparison.OrdinalIgnoreCase)).Distinct(StringComparer.OrdinalIgnoreCase));
        document.Entities.Add(new ImportedEntity(block.Kind, block.Name, aliasText, summary, notes.ToString().Trim(), attributes));
    }

    /// <summary>
    /// Plain manuscripts. When a file has both "#" and "##" headings, "#" headings are books and "##" headings are chapters
    /// (a single "# Title" over "## Chapter" headings is the usual shape of an AI-written or exported manuscript).
    /// Otherwise the shallowest heading level, and lines such as "Chapter 3" or "Prologue", start chapters.
    /// </summary>
    private static void ParsePlainText(ImportDocument document, List<string> lines, string fallbackTitle) {
        document.IsPlainText = true;
        List<int> levels = lines.Select(static line => MarkdownHeading().Match(line)).Where(static match => match.Success)
            .Select(static match => match.Groups["hashes"].Length).Distinct().Order().ToList();
        int? bookLevel = null;
        int chapterLevel = levels.Count > 0 ? levels[0] : 1;
        if ((levels.Count >= 2) && (levels[1] == levels[0] + 1)) {
            bookLevel = levels[0];
            chapterLevel = levels[1];
        }

        string title = fallbackTitle;
        string? book = null;
        bool inChapter = false;
        int ignoredLines = 0;
        int ignoredAt = 0;
        List<string> body = [];

        void Flush() {
            string text = JoinBody(body);
            if (inChapter || (text.Length > 0)) {
                if (inChapter || (bookLevel is null)) {
                    document.Chapters.Add(new ImportedChapter(title, "", null, text) { BookTitle = book });
                }
                else if (text.Length > 0) {
                    ignoredLines += body.Count(static line => !string.IsNullOrWhiteSpace(line));
                }
            }
            body.Clear();
        }

        for (int i = 0; i < lines.Count; i++) {
            string line = lines[i];
            Match markdown = MarkdownHeading().Match(line);
            if (markdown.Success) {
                int level = markdown.Groups["hashes"].Length;
                string heading = markdown.Groups["title"].Value.Trim().TrimEnd('#').Trim();
                if (level == bookLevel) {
                    Flush();
                    book = heading;
                    if (!document.Books.Any(existing => existing.Title.Equals(heading, StringComparison.OrdinalIgnoreCase))) {
                        document.Books.Add(new ImportedBook(heading, ""));
                    }
                    inChapter = false;
                    ignoredAt = i + 1;
                    continue;
                }
                if (level == chapterLevel) {
                    Flush();
                    title = heading;
                    inChapter = true;
                    continue;
                }
            }
            else if ((line.Length <= 100) && PlainHeading().IsMatch(line)) {
                Flush();
                title = line.Trim();
                inChapter = true;
                continue;
            }
            body.Add(line);
        }
        Flush();

        if (ignoredLines > 0) {
            document.Warnings.Add(new ImportWarning(ignoredAt, $"{ignoredLines} line(s) between a book heading and its first chapter (title page, byline) were not imported."));
        }
        if (document.Chapters.Count > 0) {
            string shape = document.Books.Count > 0
                ? $"{document.Books.Count} book(s) and {document.Chapters.Count} chapter(s) from its headings"
                : document.Chapters.Count == 1 ? "one chapter" : $"{document.Chapters.Count} chapters split at its headings";
            document.Warnings.Add(new ImportWarning(1, $"No WritingIDE blocks found; the text is imported as a plain manuscript: {shape}."));
        }
    }

    /// <summary>Recognises "Key: value" lines, tolerating Markdown bullets and bold ("- **Role:** Hero").</summary>
    internal static bool TryParseField(string line, out string key, out string value) {
        Match match = FieldLine().Match(StripMarkdown(line).Trim());
        if (!match.Success || (match.Groups["key"].Value.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length > 5)) {
            key = "";
            value = "";
            return false;
        }
        key = match.Groups["key"].Value.Trim();
        value = match.Groups["value"].Value.Trim();
        return true;
    }

    private static string StripMarkdown(string line) {
        string stripped = line.Replace("**", "").Replace("__", "");
        string trimmed = stripped.TrimStart();
        if (trimmed.StartsWith("- ", StringComparison.Ordinal) || trimmed.StartsWith("* ", StringComparison.Ordinal) || trimmed.StartsWith("\u2022 ", StringComparison.Ordinal)) {
            return trimmed[2..];
        }
        return stripped;
    }

    private static List<string> Normalize(string text) {
        return text.Replace("\r\n", "\n").Replace('\r', '\n')
            .Split('\n')
            .Where(static line => !line.TrimStart().StartsWith("```", StringComparison.Ordinal))
            .ToList();
    }

    private static string JoinBody(IEnumerable<string> lines) {
        List<string> body = lines.Select(static line => line.TrimEnd()).ToList();
        while ((body.Count > 0) && (body[0].Length == 0)) {
            body.RemoveAt(0);
        }
        while ((body.Count > 0) && (body[^1].Length == 0)) {
            body.RemoveAt(body.Count - 1);
        }
        return string.Join(Environment.NewLine, body);
    }

    private static string CleanName(string name) {
        return name.Replace("**", "").Trim().Trim('"', '\'', '*', '_').Trim();
    }

    private static bool IsSeparator(string line) {
        string trimmed = line.Trim();
        return (trimmed.Length >= 3) && trimmed.All(static c => c == '-');
    }

    [GeneratedRegex(@"^\s*(?:={2,}|#{1,4})\s*(?<type>[A-Za-z]+)\s*(?:(?<colon>:)\s*(?<name>.*?))?\s*(?:={2,})?\s*$")]
    private static partial Regex BlockHeader();

    [GeneratedRegex(@"^(?<key>[A-Za-z][A-Za-z0-9 '/()&-]{0,40}?)\s*:\s*(?<value>.*)$")]
    private static partial Regex FieldLine();

    [GeneratedRegex(@"^\s{0,3}(?<hashes>#{1,6})\s+(?<title>.+?)\s*$")]
    private static partial Regex MarkdownHeading();

    [GeneratedRegex(@"^\s*(?:(?:chapter|part)\s+(?:\d+|[ivxlcdm]+|one|two|three|four|five|six|seven|eight|nine|ten|eleven|twelve|thirteen|fourteen|fifteen|sixteen|seventeen|eighteen|nineteen|twenty)\b.*|(?:prologue|epilogue|interlude)\b.*)\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex PlainHeading();
}