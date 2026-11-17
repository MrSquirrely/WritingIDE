using System.Windows;

using WritingIDE.Core.Analysis;
using WritingIDE.Core.Model;
using WritingIDE.Sdk;
using WritingIDE.ViewModels;

namespace WritingIDE.Services;

/// <summary>
/// The open project as seen by extensions and the local REST API. Every call runs on the UI thread, and text edits to
/// open chapters go through their editors so the writer can undo them.
/// </summary>
public sealed class ProjectApi(MainViewModel shell) : IProjectApi {
    public bool IsOpen => OnUi(() => shell.Project is not null);

    public string Name => OnUi(() => shell.Project?.Name ?? "");

    public string Author => OnUi(() => shell.Project?.Author ?? "");

    public string Genre => OnUi(() => shell.Project?.Genre ?? "");

    public WritingStats GetWritingStats() {
        return OnUi(() => {
            NovelProject? project = shell.Project;
            if (project is null) {
                return new WritingStats(0, 0, 0, 0, 0, 0, 0);
            }
            DateOnly today = DateOnly.FromDateTime(DateTime.Now);
            return new WritingStats(shell.TotalWords, project.TargetWordCount, WritingProgress.WrittenOn(project.WritingLog, today), project.DailyWordGoal,
                WritingProgress.Streak(project.WritingLog, today, project.DailyWordGoal), WritingProgress.BestStreak(project.WritingLog, project.DailyWordGoal),
                project.WritingLog.Count(static day => day.Written > 0));
        });
    }

    public IReadOnlyList<(Guid TargetId, string Label)> GetEntityRelationships(Guid entityId) {
        return OnUi(() => (IReadOnlyList<(Guid TargetId, string Label)>)FindEntity(entityId).Relationships.Select(static relationship => (relationship.TargetId, relationship.Label)).ToList());
    }

    public string DirectoryPath => OnUi(() => shell.Project?.DirectoryPath ?? "");

    public IReadOnlyList<ChapterInfo> GetChapters() {
        return OnUi(() => {
            NovelProject? project = shell.Project;
            if (project is null) {
                return (IReadOnlyList<ChapterInfo>)[];
            }
            return shell.OrderedChapters.Select(chapter => new ChapterInfo(chapter.Id, chapter.Title, chapter.BookId, project.FindBook(chapter.BookId)?.Title,
                chapter.Status.ToString(), chapter.Synopsis, chapter.PointOfView, chapter.StoryDate, shell.GetWordCount(chapter))).ToList();
        });
    }

    public string GetChapterText(Guid chapterId) {
        return OnUi(() => shell.GetChapterText(FindChapter(chapterId)));
    }

    public void SetChapterText(Guid chapterId, string text) {
        OnUi(() => {
            Chapter chapter = FindChapter(chapterId);
            ChapterDocumentViewModel? open = shell.Documents.OfType<ChapterDocumentViewModel>().FirstOrDefault(document => document.Chapter == chapter);
            if (open is null) {
                chapter.Text = text;
                shell.Project!.MarkDirty();
            }
            else {
                open.Document.Replace(0, open.Document.TextLength, text);
            }
            return 0;
        });
    }

    public Guid AddChapter(string title, string text, Guid? bookId = null) {
        return OnUi(() => {
            NovelProject project = RequireProject();
            if ((bookId is Guid requested) && (project.FindBook(requested) is null)) {
                throw new ArgumentException($"No book with id {requested}.");
            }
            Chapter chapter = new(title) { BookId = bookId ?? (project.IsSeries ? project.Books.LastOrDefault()?.Id : null), Text = text };
            Chapter? last = project.GetChapters(project.FindBook(chapter.BookId)).LastOrDefault();
            project.Chapters.Insert(last is null ? project.Chapters.Count : project.Chapters.IndexOf(last) + 1, chapter);
            return chapter.Id;
        });
    }

    public void UpdateChapter(Guid chapterId, string? title = null, string? synopsis = null, string? pointOfView = null, string? storyDate = null) {
        OnUi(() => {
            Chapter chapter = FindChapter(chapterId);
            if (title is not null) {
                chapter.Title = string.IsNullOrWhiteSpace(title) ? throw new ArgumentException("A chapter needs a title.") : title.Trim();
            }
            chapter.Synopsis = synopsis ?? chapter.Synopsis;
            chapter.PointOfView = pointOfView ?? chapter.PointOfView;
            chapter.StoryDate = storyDate ?? chapter.StoryDate;
            shell.Project!.MarkDirty();
            return 0;
        });
    }

    public string GetEntityNotes(Guid entityId) {
        return OnUi(() => FindEntity(entityId).Notes);
    }

    public void UpdateEntity(Guid entityId, string? summary = null, string? notes = null) {
        OnUi(() => {
            StoryEntity entity = FindEntity(entityId);
            entity.Summary = summary ?? entity.Summary;
            entity.Notes = notes ?? entity.Notes;
            shell.Project!.MarkDirty();
            return 0;
        });
    }

    public void SetEntityAttribute(Guid entityId, string key, string? value) {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        OnUi(() => {
            StoryEntity entity = FindEntity(entityId);
            List<EntityAttribute> existing = entity.Attributes.Where(attribute => attribute.Key.Equals(key, StringComparison.OrdinalIgnoreCase)).ToList();
            if (value is null) {
                foreach (EntityAttribute attribute in existing) {
                    entity.Attributes.Remove(attribute);
                }
            }
            else if (existing.Count > 0) {
                existing[0].Value = value;
            }
            else {
                entity.Attributes.Add(new EntityAttribute(key, value));
            }
            shell.Project!.MarkDirty();
            return 0;
        });
    }

    public bool RemoveComment(Guid chapterId, Guid commentId) {
        return OnUi(() => {
            Chapter chapter = FindChapter(chapterId);
            ChapterComment? comment = chapter.Comments.FirstOrDefault(comment => comment.Id == commentId);
            if (comment is null) {
                return false;
            }
            chapter.Comments.Remove(comment);
            shell.Project!.MarkDirty();
            return true;
        });
    }

    public IReadOnlyList<EntityInfo> GetEntities() {
        return OnUi(() => (IReadOnlyList<EntityInfo>)(shell.Project?.Entities.Select(static entity => new EntityInfo(entity.Id, entity.Kind, entity.Name, entity.AliasList, entity.Summary,
            entity.Attributes.Where(static attribute => attribute.Key.Length > 0).GroupBy(static attribute => attribute.Key).ToDictionary(static group => group.Key, static group => group.First().Value))).ToList() ?? []));
    }

    public Guid AddEntity(EntityKind kind, string name, string summary = "") {
        return OnUi(() => {
            RequireProject();
            StoryEntity entity = shell.CreateEntity(kind, name, open: false);
            entity.Summary = summary;
            return entity.Id;
        });
    }

    public IReadOnlyList<CommentInfo> GetComments(Guid chapterId) {
        return OnUi(() => {
            Chapter chapter = FindChapter(chapterId);
            shell.Documents.OfType<ChapterDocumentViewModel>().FirstOrDefault(document => document.Chapter == chapter)?.SyncComments();
            return (IReadOnlyList<CommentInfo>)chapter.Comments.Select(comment => new CommentInfo(comment.Id, chapter.Id, comment.Kind.ToString(), comment.Offset, comment.Length,
                comment.AnchorText, comment.Text, comment.IsResolved)).ToList();
        });
    }

    public Guid AddComment(Guid chapterId, int offset, int length, string text) {
        return OnUi(() => {
            Chapter chapter = FindChapter(chapterId);
            string chapterText = shell.GetChapterText(chapter);
            offset = Math.Clamp(offset, 0, chapterText.Length);
            length = Math.Clamp(length, 0, chapterText.Length - offset);
            ChapterComment comment = new() {
                Offset = offset,
                Length = length,
                AnchorText = chapterText.Substring(offset, length),
                Text = text,
                Author = "API",
                CreatedUtc = DateTime.UtcNow
            };
            chapter.Comments.Add(comment);
            return comment.Id;
        });
    }

    public void OpenEntity(Guid entityId) {
        OnUi(() => {
            shell.OpenEntity(FindEntity(entityId));
            return 0;
        });
    }

    public void Reanalyze() {
        OnUi(() => {
            if (shell.Project is not null) {
                shell.ReanalyzeOpenDocuments();
            }
            return 0;
        });
    }

    public IReadOnlyList<DiagnosticInfo> GetDiagnostics() {
        return OnUi(() => (IReadOnlyList<DiagnosticInfo>)shell.ErrorItemsUnfiltered.Select(static item => new DiagnosticInfo(item.Chapter.Id, item.Document, item.Line, item.Column,
            item.Code, item.Severity.ToString(), item.Category, item.Description)).ToList());
    }

    public void Save() {
        OnUi(() => {
            shell.SaveAll(automatic: true);
            return 0;
        });
    }

    private NovelProject RequireProject() {
        return shell.Project ?? throw new InvalidOperationException("No project is open.");
    }

    private Chapter FindChapter(Guid chapterId) {
        return RequireProject().Chapters.FirstOrDefault(chapter => chapter.Id == chapterId) ?? throw new KeyNotFoundException($"No chapter with id {chapterId}.");
    }

    private StoryEntity FindEntity(Guid entityId) {
        return RequireProject().FindEntity(entityId) ?? throw new KeyNotFoundException($"No story element with id {entityId}.");
    }

    internal static T OnUi<T>(Func<T> action) {
        return Application.Current.Dispatcher.CheckAccess() ? action() : Application.Current.Dispatcher.Invoke(action);
    }
}