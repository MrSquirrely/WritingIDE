using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

using WritingIDE.Core.Model;
using WritingIDE.Infrastructure;

namespace WritingIDE.Editor;

/// <summary>One IntelliSense entry: a story element name or alias.</summary>
public sealed class CompletionItem(StoryEntity entity, string text, bool isAlias) {
    public StoryEntity Entity { get; } = entity;

    public string Text { get; } = text;

    public bool IsAlias { get; } = isAlias;

    public string Glyph => Entity.KindInfo.Glyph;

    public Brush GlyphBrush => Visuals.GetKindBrush(Entity.Kind);

    public string Detail => IsAlias ? $"alias of {Entity.Name}" : Entity.KindInfo.DisplayName;
}

public partial class CompletionListView : UserControl {
    public CompletionListView() {
        InitializeComponent();
        Items.SelectionChanged += (_, _) => UpdateDetails();
    }

    public ListBox List => Items;

    public CompletionItem? SelectedItem => Items.SelectedItem as CompletionItem;

    public void SetItems(string header, IReadOnlyList<CompletionItem> items, string emptyMessage) {
        HeaderText.Text = header;
        Items.ItemsSource = items;
        bool empty = items.Count == 0;
        EmptyText.Text = emptyMessage;
        EmptyText.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;
        Items.Visibility = empty ? Visibility.Collapsed : Visibility.Visible;
        HintText.Text = empty ? "Esc to close" : "Tab/Enter to insert";
        UpdateDetails();
    }

    public void Select(int index) {
        if (Items.Items.Count == 0) {
            return;
        }
        Items.SelectedIndex = Math.Clamp(index, -1, Items.Items.Count - 1);
        if (Items.SelectedItem is not null) {
            Items.ScrollIntoView(Items.SelectedItem);
        }
    }

    private void UpdateDetails() {
        CompletionItem? item = SelectedItem ?? (Items.Items.Count > 0 ? Items.Items[0] as CompletionItem : null);
        if (item is null) {
            DetailsPanel.Visibility = Visibility.Collapsed;
            return;
        }
        DetailsPanel.Visibility = Visibility.Visible;
        StoryEntity entity = item.Entity;
        DetailsTitle.Text = $"{entity.KindInfo.DisplayName} {entity.Name}";
        List<string> lines = [];
        if (!string.IsNullOrWhiteSpace(entity.Summary)) {
            lines.Add(entity.Summary);
        }
        string attributes = string.Join("  \u00B7  ", entity.Attributes.Where(static a => !string.IsNullOrWhiteSpace(a.Value)).Take(4).Select(static a => $"{a.Key}: {a.Value}"));
        if (attributes.Length > 0) {
            lines.Add(attributes);
        }
        if (entity.AliasList.Count > 0) {
            lines.Add("Also known as: " + string.Join(", ", entity.AliasList));
        }
        DetailsText.Text = lines.Count > 0 ? string.Join("\n", lines) : "No description yet.";
    }
}