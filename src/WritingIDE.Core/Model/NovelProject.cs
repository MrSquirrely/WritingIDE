using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;

namespace WritingIDE.Core.Model;

/// <summary>A novel project: chapters plus every story element. The novel equivalent of a .csproj.</summary>
public sealed class NovelProject : ObservableObject {
    public NovelProject() {
        Chapters.CollectionChanged += OnCollectionChanged;
        Entities.CollectionChanged += OnCollectionChanged;
        Books.CollectionChanged += OnCollectionChanged;
        CustomDictionary.CollectionChanged += (_, _) => MarkDirty();
        DisabledRules.CollectionChanged += (_, _) => MarkDirty();
    }

    /// <summary>Raised whenever the set of entities or any entity's names change.</summary>
    public event EventHandler? EntitiesChanged;

    public string Name { get; set => SetAndMarkDirty(ref field, value); } = "Untitled Novel";

    public string Author { get; set => SetAndMarkDirty(ref field, value); } = "";

    public string Genre { get; set => SetAndMarkDirty(ref field, value); } = "";

    public string Synopsis { get; set => SetAndMarkDirty(ref field, value); } = "";

    public int TargetWordCount { get; set => SetAndMarkDirty(ref field, value); } = 80000;

    public ProjectKind Kind {
        get;
        set {
            if (SetProperty(ref field, value)) {
                OnPropertyChanged(nameof(IsSeries));
                MarkDirty();
            }
        }
    } = ProjectKind.Novel;

    public bool IsSeries => Kind == ProjectKind.Series;

    /// <summary>The story bible: premise, tone, themes, world rules, plot plan. Free-form text.</summary>
    public string Notes { get; set => SetAndMarkDirty(ref field, value); } = "";

    /// <summary>Full path of the .novproj file. Not serialized.</summary>
    public string FilePath { get; set => SetProperty(ref field, value); } = "";

    public string DirectoryPath => Path.GetDirectoryName(FilePath) ?? "";

    public ObservableCollection<Chapter> Chapters { get; } = [];

    public ObservableCollection<StoryEntity> Entities { get; } = [];

    /// <summary>The books of a series, in reading order. Empty for a single novel.</summary>
    public ObservableCollection<Book> Books { get; } = [];

    public ObservableCollection<string> CustomDictionary { get; } = [];

    public ObservableCollection<string> DisabledRules { get; } = [];

    /// <summary>Chapter files removed since the last save; deleted from disk on save.</summary>
    public List<string> RemovedChapterFiles { get; } = [];

    public bool IsDirty { get; set => SetProperty(ref field, value); }

    public IEnumerable<StoryEntity> GetEntities(EntityKind kind) {
        return Entities.Where(entity => entity.Kind == kind);
    }

    public StoryEntity? FindEntity(Guid id) {
        return Entities.FirstOrDefault(entity => entity.Id == id);
    }

    public void MarkDirty() {
        IsDirty = true;
    }

    public Book? FindBook(Guid? id) {
        return id is null ? null : Books.FirstOrDefault(book => book.Id == id);
    }

    /// <summary>Chapters of one book in manuscript order (all chapters when <paramref name="book"/> is null in a novel).</summary>
    public IEnumerable<Chapter> GetChapters(Book? book) {
        return book is null ? Chapters.Where(static chapter => chapter.BookId is null) : Chapters.Where(chapter => chapter.BookId == book.Id);
    }

    /// <summary>"Book title \u203A Chapter title" in a series, the chapter title in a novel.</summary>
    public string GetDisplayName(Chapter chapter) {
        Book? book = FindBook(chapter.BookId);
        return book is null ? chapter.Title : $"{book.Title} \u203A {chapter.Title}";
    }

    /// <summary>Turns a single novel into a series whose first book holds every existing chapter.</summary>
    public Book ConvertToSeries(string firstBookTitle) {
        Book book = Books.FirstOrDefault() ?? new Book(firstBookTitle);
        if (!Books.Contains(book)) {
            Books.Add(book);
        }
        foreach (Chapter chapter in Chapters.Where(static chapter => chapter.BookId is null)) {
            chapter.BookId = book.Id;
        }
        Kind = ProjectKind.Series;
        return book;
    }

    private void SetAndMarkDirty<T>(ref T storage, T value, [System.Runtime.CompilerServices.CallerMemberName] string? propertyName = null) {
        if (SetProperty(ref storage, value, propertyName)) {
            MarkDirty();
        }
    }

    private void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) {
        if (e.NewItems is not null) {
            foreach (object item in e.NewItems) {
                if (item is INotifyPropertyChanged notifier) {
                    notifier.PropertyChanged += OnItemPropertyChanged;
                }
            }
        }
        if (e.OldItems is not null) {
            foreach (object item in e.OldItems) {
                if (item is INotifyPropertyChanged notifier) {
                    notifier.PropertyChanged -= OnItemPropertyChanged;
                }
            }
        }
        MarkDirty();
        if (sender == Entities) {
            EntitiesChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private void OnItemPropertyChanged(object? sender, PropertyChangedEventArgs e) {
        if (sender is Chapter && (e.PropertyName == nameof(Chapter.Text))) {
            // Text is synchronised from the editor; the editor tracks its own dirty state.
            return;
        }
        MarkDirty();
        if ((sender is StoryEntity) && (e.PropertyName is nameof(StoryEntity.Name) or nameof(StoryEntity.Aliases))) {
            EntitiesChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}