namespace WritingIDE.Core.Model;

/// <summary>Whether a project is a single novel or a series of books sharing one story bible.</summary>
public enum ProjectKind {
    Novel,
    Series
}

/// <summary>A book in a series. Its chapters reference it through <see cref="Chapter.BookId"/>; story elements are shared by the whole series.</summary>
public sealed class Book(string title) : ObservableObject {
    public Guid Id { get; init; } = Guid.NewGuid();

    public string Title { get; set => SetProperty(ref field, value); } = title;

    public string Synopsis { get; set => SetProperty(ref field, value); } = "";

    public override string ToString() {
        return Title;
    }
}