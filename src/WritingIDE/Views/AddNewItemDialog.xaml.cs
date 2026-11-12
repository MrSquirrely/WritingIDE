using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

using WritingIDE.Core.Model;
using WritingIDE.Infrastructure;

namespace WritingIDE.Views;

/// <summary>Visual Studio style "Add New Item" dialog: chapters and every kind of story element.</summary>
public partial class AddNewItemDialog : HandyControl.Controls.Window {
    private readonly NovelProject _project;
    private string _defaultName = "";

    public AddNewItemDialog(NovelProject project, EntityKind? kind, bool chapter, Book? targetBook = null) {
        InitializeComponent();
        _project = project;
        List<ItemTemplate> templates = [
            new(null, "Chapter", "Manuscript", Visuals.ChapterGlyph, Visuals.ChapterBrush,
                project.IsSeries ? "A new chapter in one of the series' books." : "A new chapter (a text file in the Manuscript folder).", "")
        ];
        if (project.IsSeries) {
            templates.Add(new ItemTemplate(null, "Book", "Series", Visuals.BookGlyph, Visuals.BookBrush,
                "A new book in the series. It shares every character, place and lore entry with the other books.", "") { IsBook = true });
            BookBox.ItemsSource = project.Books;
            BookBox.SelectedItem = targetBook ?? project.Books.LastOrDefault();
        }
        foreach (EntityKindInfo info in EntityKindInfo.All) {
            templates.Add(new ItemTemplate(info.Kind, info.DisplayName, "Story element", info.Glyph, Visuals.GetKindBrush(info.Kind), info.Description,
                "IntelliSense: " + string.Join("  ", info.TriggerKeywords.Select(static keyword => keyword + "."))));
        }
        ItemList.ItemsSource = templates;
        ItemList.SelectedItem = chapter ? templates[0] : templates.FirstOrDefault(template => template.Kind == kind) ?? templates[1];
        Loaded += (_, _) => {
            NameBox.Focus();
            NameBox.SelectAll();
        };
    }

    /// <summary>The selected story element kind, or null for a chapter or book.</summary>
    public EntityKind? SelectedKind => ((ItemTemplate)ItemList.SelectedItem).Kind;

    public bool IsBook => ((ItemTemplate)ItemList.SelectedItem).IsBook;

    /// <summary>The book a new chapter goes into (series only).</summary>
    public Book? TargetBook => BookBox.SelectedItem as Book;

    public string ItemName => NameBox.Text.Trim();

    private void OnSelectionChanged(object sender, SelectionChangedEventArgs e) {
        if (ItemList.SelectedItem is not ItemTemplate template) {
            return;
        }
        DetailTitle.Text = template.Title;
        DetailDescription.Text = template.Description;
        DetailTrigger.Text = template.Trigger;
        BookPanel.Visibility = _project.IsSeries && (template.Kind is null) && !template.IsBook ? Visibility.Visible : Visibility.Collapsed;
        // Keep a name the user typed; only replace the suggested default.
        if ((NameBox.Text.Length == 0) || (NameBox.Text == _defaultName)) {
            _defaultName = template.IsBook ? $"{_project.Name}, Book {_project.Books.Count + 1}"
                : template.Kind is null ? $"Chapter {_project.GetChapters(TargetBook).Count() + 1}"
                : CreateDefaultName(template.Kind.Value);
            NameBox.Text = _defaultName;
            NameBox.SelectAll();
        }
        ErrorText.Text = "";
    }

    private string CreateDefaultName(EntityKind kind) {
        string baseName = "New " + EntityKindInfo.Get(kind).DisplayName;
        string name = baseName;
        int counter = 2;
        while (_project.Entities.Any(entity => string.Equals(entity.Name, name, StringComparison.OrdinalIgnoreCase))) {
            name = $"{baseName} {counter++}";
        }
        return name;
    }

    private void OnAdd(object sender, RoutedEventArgs e) {
        if (ItemName.Length == 0) {
            ErrorText.Text = "Enter a name.";
            return;
        }
        if ((SelectedKind is not null) && _project.Entities.Any(entity => string.Equals(entity.Name, ItemName, StringComparison.OrdinalIgnoreCase))) {
            ErrorText.Text = $"A story element named '{ItemName}' already exists.";
            return;
        }
        DialogResult = true;
    }

    public sealed record ItemTemplate(EntityKind? Kind, string Title, string Category, string Glyph, Brush Brush, string Description, string Trigger) {
        public bool IsBook { get; init; }
    }
}