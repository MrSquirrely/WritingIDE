using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;

namespace WritingIDE.Core.Model;

public enum ChapterStatus {
    Outline,
    Draft,
    Revised,
    Final
}

/// <summary>A manuscript document. The novel equivalent of a source file.</summary>
public sealed class Chapter : ObservableObject {
    public Chapter(string title) {
        Title = title;
        Comments.CollectionChanged += OnCommentsChanged;
    }

    public Guid Id { get; init; } = Guid.NewGuid();

    public string Title { get; set => SetProperty(ref field, value); }

    /// <summary>The book this chapter belongs to in a series; null in a single-novel project.</summary>
    public Guid? BookId { get; set => SetProperty(ref field, value); }

    /// <summary>Path of the text file relative to the project directory.</summary>
    public string FileName { get; set => SetProperty(ref field, value); } = "";

    public string Synopsis { get; set => SetProperty(ref field, value); } = "";

    public ChapterStatus Status { get; set => SetProperty(ref field, value); } = ChapterStatus.Draft;

    /// <summary>Whose point of view the chapter is told from (a character name or "First person"); empty to detect it.</summary>
    public string PointOfView { get; set => SetProperty(ref field, value); } = "";

    /// <summary>When the chapter happens in story time, free text ("Day 3", "Spring 1204", "12 May 2024"). Drives the timeline.</summary>
    public string StoryDate { get; set => SetProperty(ref field, value); } = "";

    /// <summary>Margin comments anchored to ranges of the text.</summary>
    public ObservableCollection<ChapterComment> Comments { get; } = [];

    /// <summary>The chapter text as last synchronised from the editor (or loaded from disk).</summary>
    public string Text { get; set => SetProperty(ref field, value); } = "";

    public override string ToString() {
        return Title;
    }

    private void OnCommentsChanged(object? sender, NotifyCollectionChangedEventArgs e) {
        if (e.NewItems is not null) {
            foreach (ChapterComment comment in e.NewItems) {
                comment.PropertyChanged += OnCommentPropertyChanged;
            }
        }
        if (e.OldItems is not null) {
            foreach (ChapterComment comment in e.OldItems) {
                comment.PropertyChanged -= OnCommentPropertyChanged;
            }
        }
        OnPropertyChanged(nameof(Comments));
    }

    private void OnCommentPropertyChanged(object? sender, PropertyChangedEventArgs e) {
        OnPropertyChanged(nameof(Comments));
    }
}

/// <summary>
/// A comment (margin note) on a range of a chapter. The range is tracked live by the editor; on disk it is stored as
/// an offset plus the commented text, so it can be found again if the chapter was edited elsewhere.
/// </summary>
public sealed class ChapterComment : ObservableObject {
    public Guid Id { get; init; } = Guid.NewGuid();

    /// <summary>Start of the commented text. Updated without change notification (the editor owns the live position).</summary>
    public int Offset { get; set; }

    public int Length { get; set; }

    /// <summary>The commented text when the position was last recorded, used to re-anchor the comment.</summary>
    public string AnchorText { get; set; } = "";

    public string Text { get; set => SetProperty(ref field, value); } = "";

    public string Author { get; set; } = "";

    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

    public bool IsResolved { get; set => SetProperty(ref field, value); }

    /// <summary>True when the commented text could not be found any more (it was deleted or rewritten).</summary>
    public bool IsOrphaned { get; set => SetProperty(ref field, value); }
}