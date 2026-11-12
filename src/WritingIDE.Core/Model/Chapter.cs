namespace WritingIDE.Core.Model;

public enum ChapterStatus {
    Outline,
    Draft,
    Revised,
    Final
}

/// <summary>A manuscript document. The novel equivalent of a source file.</summary>
public sealed class Chapter(string title) : ObservableObject {
    public Guid Id { get; init; } = Guid.NewGuid();

    public string Title { get; set => SetProperty(ref field, value); } = title;

    /// <summary>The book this chapter belongs to in a series; null in a single-novel project.</summary>
    public Guid? BookId { get; set => SetProperty(ref field, value); }

    /// <summary>Path of the text file relative to the project directory.</summary>
    public string FileName { get; set => SetProperty(ref field, value); } = "";

    public string Synopsis { get; set => SetProperty(ref field, value); } = "";

    public ChapterStatus Status { get; set => SetProperty(ref field, value); } = ChapterStatus.Draft;

    /// <summary>The chapter text as last synchronised from the editor (or loaded from disk).</summary>
    public string Text { get; set => SetProperty(ref field, value); } = "";

    public override string ToString() {
        return Title;
    }
}