using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

using WritingIDE.Core.Model;

namespace WritingIDE.Core.Persistence;

/// <summary>Reads and writes .novproj files. Story elements live in the project file, chapter text in Manuscript/*.txt.</summary>
public static class ProjectSerializer {
    public const string Extension = ".novproj";
    public const string ManuscriptFolder = "Manuscript";

    private static readonly JsonSerializerOptions JsonOptions = new() {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public static NovelProject Load(string filePath) {
        string json = File.ReadAllText(filePath);
        ProjectFileDto dto = JsonSerializer.Deserialize<ProjectFileDto>(json, JsonOptions)
            ?? throw new InvalidDataException("The project file is empty or invalid.");
        string directory = Path.GetDirectoryName(filePath) ?? "";

        NovelProject project = new() {
            FilePath = filePath,
            Name = dto.Name,
            Author = dto.Author,
            Genre = dto.Genre,
            Synopsis = dto.Synopsis,
            TargetWordCount = dto.TargetWordCount,
            Kind = dto.Kind,
            Notes = dto.Notes
        };
        foreach (BookDto bookDto in dto.Books) {
            project.Books.Add(new Book(bookDto.Title) { Id = bookDto.Id, Synopsis = bookDto.Synopsis });
        }
        foreach (ChapterDto chapterDto in dto.Chapters) {
            string chapterPath = Path.Combine(directory, chapterDto.File);
            project.Chapters.Add(new Chapter(chapterDto.Title) {
                Id = chapterDto.Id,
                BookId = chapterDto.BookId,
                FileName = chapterDto.File,
                Synopsis = chapterDto.Synopsis,
                Status = chapterDto.Status,
                Text = File.Exists(chapterPath) ? File.ReadAllText(chapterPath) : ""
            });
        }
        foreach (EntityDto entityDto in dto.Entities) {
            StoryEntity entity = new(entityDto.Kind, entityDto.Name) {
                Id = entityDto.Id,
                Aliases = entityDto.Aliases,
                Summary = entityDto.Summary,
                Notes = entityDto.Notes
            };
            foreach (AttributeDto attribute in entityDto.Attributes) {
                entity.Attributes.Add(new EntityAttribute(attribute.Key, attribute.Value));
            }
            project.Entities.Add(entity);
        }
        foreach (string word in dto.Dictionary) {
            project.CustomDictionary.Add(word);
        }
        foreach (string rule in dto.DisabledRules) {
            project.DisabledRules.Add(rule);
        }
        project.IsDirty = false;
        return project;
    }

    public static void Save(NovelProject project) {
        if (string.IsNullOrEmpty(project.FilePath)) {
            throw new InvalidOperationException("The project has no file path.");
        }
        string directory = project.DirectoryPath;
        Directory.CreateDirectory(Path.Combine(directory, ManuscriptFolder));

        HashSet<string> usedFiles = new(StringComparer.OrdinalIgnoreCase);
        foreach (Chapter chapter in project.Chapters) {
            if (string.IsNullOrEmpty(chapter.FileName) || !usedFiles.Add(chapter.FileName)) {
                // Series keep each book's chapters in their own folder: Manuscript/<Book>/<Chapter>.txt
                Book? book = project.FindBook(chapter.BookId);
                string folder = book is null ? ManuscriptFolder : Path.Combine(ManuscriptFolder, Sanitize(book.Title, "Book"));
                chapter.FileName = CreateChapterFileName(chapter.Title, folder, usedFiles);
                usedFiles.Add(chapter.FileName);
            }
            string chapterPath = Path.Combine(directory, chapter.FileName);
            Directory.CreateDirectory(Path.GetDirectoryName(chapterPath)!);
            File.WriteAllText(chapterPath, chapter.Text, Encoding.UTF8);
        }
        foreach (string removed in project.RemovedChapterFiles) {
            string removedPath = Path.Combine(directory, removed);
            if (!usedFiles.Contains(removed) && File.Exists(removedPath)) {
                File.Delete(removedPath);
                // Drop a book folder that is now empty (Manuscript/<Book>/), but never Manuscript itself.
                string? folder = Path.GetDirectoryName(removedPath);
                if ((folder is not null) && !string.Equals(Path.GetFullPath(folder), Path.GetFullPath(Path.Combine(directory, ManuscriptFolder)), StringComparison.OrdinalIgnoreCase)
                    && Directory.Exists(folder) && !Directory.EnumerateFileSystemEntries(folder).Any()) {
                    Directory.Delete(folder);
                }
            }
        }
        project.RemovedChapterFiles.Clear();

        ProjectFileDto dto = new() {
            Name = project.Name,
            Author = project.Author,
            Genre = project.Genre,
            Synopsis = project.Synopsis,
            TargetWordCount = project.TargetWordCount,
            Kind = project.Kind,
            Notes = project.Notes,
            Books = project.Books.Select(static book => new BookDto { Id = book.Id, Title = book.Title, Synopsis = book.Synopsis }).ToList(),
            Chapters = project.Chapters.Select(static chapter => new ChapterDto {
                Id = chapter.Id,
                BookId = chapter.BookId,
                Title = chapter.Title,
                File = chapter.FileName,
                Synopsis = chapter.Synopsis,
                Status = chapter.Status
            }).ToList(),
            Entities = project.Entities.Select(static entity => new EntityDto {
                Id = entity.Id,
                Kind = entity.Kind,
                Name = entity.Name,
                Aliases = entity.Aliases,
                Summary = entity.Summary,
                Notes = entity.Notes,
                Attributes = entity.Attributes.Select(static attribute => new AttributeDto { Key = attribute.Key, Value = attribute.Value }).ToList()
            }).ToList(),
            Dictionary = project.CustomDictionary.ToList(),
            DisabledRules = project.DisabledRules.ToList()
        };
        File.WriteAllText(project.FilePath, JsonSerializer.Serialize(dto, JsonOptions), Encoding.UTF8);
        project.IsDirty = false;
    }

    /// <summary>Creates a new project folder (parent/name/name.novproj) from a template and saves it.</summary>
    public static NovelProject Create(string parentDirectory, string name, string author, ProjectTemplate template) {
        string directory = Path.Combine(parentDirectory, name);
        string filePath = Path.Combine(directory, name + Extension);
        if (File.Exists(filePath)) {
            throw new IOException($"A project already exists at '{filePath}'.");
        }
        Directory.CreateDirectory(directory);
        NovelProject project = template.Create(name, author);
        project.FilePath = filePath;
        Save(project);
        return project;
    }

    private static string CreateChapterFileName(string title, string folder, HashSet<string> usedFiles) {
        string baseName = Sanitize(title, "Chapter");
        string candidate = Path.Combine(folder, baseName + ".txt");
        int counter = 2;
        while (usedFiles.Contains(candidate)) {
            candidate = Path.Combine(folder, $"{baseName} ({counter++}).txt");
        }
        return candidate;
    }

    private static string Sanitize(string name, string fallback) {
        StringBuilder builder = new();
        foreach (char c in name) {
            builder.Append(Path.GetInvalidFileNameChars().Contains(c) ? '_' : c);
        }
        string sanitized = builder.ToString().Trim().TrimEnd('.');
        return sanitized.Length == 0 ? fallback : sanitized;
    }

    private sealed class ProjectFileDto {
        public int FormatVersion { get; set; } = 2;
        public ProjectKind Kind { get; set; } = ProjectKind.Novel;
        public string Name { get; set; } = "";
        public string Author { get; set; } = "";
        public string Genre { get; set; } = "";
        public string Synopsis { get; set; } = "";
        public string Notes { get; set; } = "";
        public int TargetWordCount { get; set; } = 80000;
        public List<BookDto> Books { get; set; } = [];
        public List<ChapterDto> Chapters { get; set; } = [];
        public List<EntityDto> Entities { get; set; } = [];
        public List<string> Dictionary { get; set; } = [];
        public List<string> DisabledRules { get; set; } = [];
    }

    private sealed class BookDto {
        public Guid Id { get; set; }
        public string Title { get; set; } = "";
        public string Synopsis { get; set; } = "";
    }

    private sealed class ChapterDto {
        public Guid Id { get; set; }
        public Guid? BookId { get; set; }
        public string Title { get; set; } = "";
        public string File { get; set; } = "";
        public string Synopsis { get; set; } = "";
        public ChapterStatus Status { get; set; }
    }

    private sealed class EntityDto {
        public Guid Id { get; set; }
        public EntityKind Kind { get; set; }
        public string Name { get; set; } = "";
        public string Aliases { get; set; } = "";
        public string Summary { get; set; } = "";
        public string Notes { get; set; } = "";
        public List<AttributeDto> Attributes { get; set; } = [];
    }

    private sealed class AttributeDto {
        public string Key { get; set; } = "";
        public string Value { get; set; } = "";
    }
}