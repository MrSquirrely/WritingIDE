using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows.Media;

using WritingIDE.Core;
using WritingIDE.Core.Model;
using WritingIDE.Infrastructure;

namespace WritingIDE.ViewModels;

/// <summary>A node in Solution Explorer.</summary>
public abstract class TreeNode : ObservableObject {
    public bool IsExpanded { get; set => SetProperty(ref field, value); }

    public bool IsSelected { get; set => SetProperty(ref field, value); }

    /// <summary>False when the Solution Explorer search filters this node out.</summary>
    public bool IsShown { get; set => SetProperty(ref field, value); } = true;

    public abstract string Glyph { get; }

    public abstract Brush GlyphBrush { get; }

    public ObservableCollection<TreeNode> Children { get; } = [];
}

/// <summary>
/// The root of Solution Explorer. A novel shows a Manuscript folder; a series shows one node per book
/// (like the projects of a Visual Studio solution). Story element folders are shared by every book.
/// </summary>
public sealed class ProjectNode : TreeNode {
    private readonly HashSet<Chapter> _watchedChapters = [];

    public ProjectNode(NovelProject project) {
        Project = project;
        IsExpanded = true;
        StoryBible = new StoryBibleNode(project);
        ManuscriptFolder = new FolderNode("Manuscript", null) { IsExpanded = true };
        foreach (EntityKindInfo info in EntityKindInfo.All) {
            EntityFolders[info.Kind] = new FolderNode(info.PluralName, info.Kind) { IsExpanded = true };
        }
        project.Chapters.CollectionChanged += (_, _) => RefreshManuscript();
        project.Books.CollectionChanged += (_, _) => RefreshManuscript();
        project.PropertyChanged += (_, e) => {
            if (e.PropertyName == nameof(NovelProject.Kind)) {
                RefreshManuscript();
                OnPropertyChanged(nameof(Glyph));
                OnPropertyChanged(nameof(KindLabel));
            }
        };
        project.Entities.CollectionChanged += OnEntitiesChanged;
        RefreshManuscript();
        foreach (EntityKind kind in EntityFolders.Keys) {
            SyncEntities(kind);
        }
        ApplyFilter(Filter);
    }

    public NovelProject Project { get; }

    public StoryBibleNode StoryBible { get; }

    public FolderNode ManuscriptFolder { get; }

    public Dictionary<EntityKind, FolderNode> EntityFolders { get; } = new();

    public override string Glyph => Project.IsSeries ? Visuals.SeriesGlyph : Visuals.ProjectGlyph;

    public override Brush GlyphBrush => Visuals.ProjectBrush;

    public string KindLabel => Project.IsSeries ? "Series" : "Novel";

    public string Filter { get; private set; } = "";

    /// <summary>Rebuilds the manuscript part of the tree (chapters, books) while reusing existing nodes.</summary>
    public void RefreshManuscript() {
        foreach (Chapter chapter in Project.Chapters.Where(chapter => _watchedChapters.Add(chapter))) {
            chapter.PropertyChanged += OnChapterPropertyChanged;
        }
        List<TreeNode> top = [StoryBible];
        if (Project.IsSeries) {
            Dictionary<Book, BookNode> existingBooks = Children.OfType<BookNode>().ToDictionary(static node => node.Book);
            foreach (Book book in Project.Books) {
                BookNode node = existingBooks.TryGetValue(book, out BookNode? found) ? found : new BookNode(book) { IsExpanded = true };
                Sync(node, Project.GetChapters(book).Cast<object>().ToList(), static item => new ChapterNode((Chapter)item));
                top.Add(node);
            }
            List<object> loose = Project.Chapters.Where(chapter => Project.FindBook(chapter.BookId) is null).Cast<object>().ToList();
            if (loose.Count > 0) {
                Sync(ManuscriptFolder, loose, static item => new ChapterNode((Chapter)item));
                top.Add(ManuscriptFolder);
            }
        }
        else {
            Sync(ManuscriptFolder, Project.Chapters.Cast<object>().ToList(), static item => new ChapterNode((Chapter)item));
            top.Add(ManuscriptFolder);
        }
        top.AddRange(EntityKindInfo.All.Select(info => EntityFolders[info.Kind]));
        if (!top.SequenceEqual(Children)) {
            Children.Clear();
            foreach (TreeNode node in top) {
                Children.Add(node);
            }
        }
        ApplyFilter(Filter);
    }

    /// <summary>Shows only chapters, books and story elements whose title, name or alias contains the text. Returns the number of matches.</summary>
    public int ApplyFilter(string filter) {
        Filter = filter.Trim();
        int matches = 0;
        foreach (TreeNode container in Children) {
            if (container is StoryBibleNode) {
                container.IsShown = (Filter.Length == 0) || ((StoryBibleNode)container).Title.Contains(Filter, StringComparison.CurrentCultureIgnoreCase);
                continue;
            }
            string name = container switch {
                FolderNode folder => folder.Name,
                BookNode bookNode => bookNode.Book.Title,
                _ => ""
            };
            bool containerMatches = (Filter.Length == 0) || name.Contains(Filter, StringComparison.CurrentCultureIgnoreCase);
            int childMatches = 0;
            foreach (TreeNode child in container.Children) {
                bool shown = containerMatches || Matches(child, Filter);
                child.IsShown = shown;
                if (shown) {
                    childMatches++;
                }
            }
            container.IsShown = containerMatches || (childMatches > 0);
            if ((Filter.Length > 0) && (childMatches > 0)) {
                container.IsExpanded = true;
            }
            matches += childMatches + (containerMatches && (Filter.Length > 0) ? 1 : 0);
        }
        return matches;
    }

    private static bool Matches(TreeNode node, string filter) {
        return node switch {
            ChapterNode chapterNode => chapterNode.Chapter.Title.Contains(filter, StringComparison.CurrentCultureIgnoreCase),
            EntityNode entityNode => entityNode.Entity.AllNames.Any(name => name.Contains(filter, StringComparison.CurrentCultureIgnoreCase)),
            _ => false
        };
    }

    public IEnumerable<TreeNode> Descendants() {
        foreach (TreeNode container in Children) {
            yield return container;
            foreach (TreeNode child in container.Children) {
                yield return child;
            }
        }
    }

    private void OnChapterPropertyChanged(object? sender, PropertyChangedEventArgs e) {
        if (e.PropertyName == nameof(Chapter.BookId)) {
            RefreshManuscript();
        }
    }

    private void OnEntitiesChanged(object? sender, NotifyCollectionChangedEventArgs e) {
        foreach (EntityKind kind in EntityFolders.Keys) {
            SyncEntities(kind);
        }
        ApplyFilter(Filter);
    }

    private void SyncEntities(EntityKind kind) {
        List<object> entities = Project.GetEntities(kind).OrderBy(static entity => entity.Name, StringComparer.CurrentCultureIgnoreCase).Cast<object>().ToList();
        Sync(EntityFolders[kind], entities, static item => new EntityNode((StoryEntity)item));
    }

    /// <summary>Rebuilds a container's children while reusing existing nodes (keeps selection state).</summary>
    private static void Sync(TreeNode container, List<object> models, Func<object, TreeNode> create) {
        Dictionary<object, TreeNode> existing = container.Children.ToDictionary(static node => node switch {
            ChapterNode chapterNode => (object)chapterNode.Chapter,
            EntityNode entityNode => entityNode.Entity,
            _ => node
        });
        List<TreeNode> ordered = models.Select(model => existing.TryGetValue(model, out TreeNode? node) ? node : create(model)).ToList();
        if (ordered.SequenceEqual(container.Children)) {
            return;
        }
        container.Children.Clear();
        foreach (TreeNode node in ordered) {
            container.Children.Add(node);
        }
    }
}

/// <summary>A book of a series; its children are the book's chapters.</summary>
public sealed class BookNode(Book book) : TreeNode {
    public Book Book { get; } = book;

    public override string Glyph => Visuals.BookGlyph;

    public override Brush GlyphBrush => Visuals.BookBrush;
}

/// <summary>Opens the project's story notes (the bible: premise, tone, themes, plot plan).</summary>
public sealed class StoryBibleNode : TreeNode {
    public StoryBibleNode(NovelProject project) {
        Project = project;
        project.PropertyChanged += (_, e) => {
            if (e.PropertyName == nameof(NovelProject.Kind)) {
                OnPropertyChanged(nameof(Title));
            }
        };
    }

    public NovelProject Project { get; }

    public string Title => Project.IsSeries ? "Series Bible" : "Story Bible";

    public override string Glyph => Visuals.BibleGlyph;

    public override Brush GlyphBrush => Visuals.ProjectBrush;
}

public sealed class FolderNode(string name, EntityKind? kind) : TreeNode {
    public string Name { get; } = name;

    /// <summary>The kind of story element stored here, or null for the Manuscript folder.</summary>
    public EntityKind? Kind { get; } = kind;

    public override string Glyph => Kind is null ? Visuals.ManuscriptGlyph : Visuals.FolderGlyph;

    public override Brush GlyphBrush => Kind is null ? Visuals.ChapterBrush : Visuals.FolderBrush;
}

public sealed class ChapterNode(Chapter chapter) : TreeNode {
    public Chapter Chapter { get; } = chapter;

    public override string Glyph => Visuals.ChapterGlyph;

    public override Brush GlyphBrush => Visuals.ChapterBrush;
}

public sealed class EntityNode : TreeNode {
    public EntityNode(StoryEntity entity) {
        Entity = entity;
        entity.PropertyChanged += OnEntityPropertyChanged;
    }

    public StoryEntity Entity { get; }

    public override string Glyph => Entity.KindInfo.Glyph;

    public override Brush GlyphBrush => Visuals.GetKindBrush(Entity.Kind);

    public string ToolTip => string.IsNullOrWhiteSpace(Entity.Summary) ? Entity.KindInfo.DisplayName : Entity.Summary;

    private void OnEntityPropertyChanged(object? sender, PropertyChangedEventArgs e) {
        if (e.PropertyName == nameof(StoryEntity.Summary)) {
            OnPropertyChanged(nameof(ToolTip));
        }
    }
}