using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;

using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Editing;
using ICSharpCode.AvalonEdit.Rendering;

using WritingIDE.Core.Model;

namespace WritingIDE.Editor;

/// <summary>
/// IntelliSense for prose. Typing a trigger keyword followed by a dot ("person.", "place.", "item.", ...)
/// lists story elements of that kind; Ctrl+Space lists everything; typing the start of a known name offers to complete it.
/// </summary>
public sealed class CompletionController {
    private readonly TextArea _textArea;
    private readonly Func<IReadOnlyList<StoryEntity>> _getEntities;
    private readonly Func<bool> _autoSuggestNames;
    private readonly Popup _popup;
    private readonly CompletionListView _view;

    private int _replaceStart;
    private int _filterStart;
    private EntityKind? _kind;
    private string _header = "";
    private bool _hardSelection;

    public CompletionController(TextArea textArea, Func<IReadOnlyList<StoryEntity>> getEntities, Func<bool> autoSuggestNames) {
        _textArea = textArea;
        _getEntities = getEntities;
        _autoSuggestNames = autoSuggestNames;
        _view = new CompletionListView();
        _popup = new Popup {
            Child = _view,
            StaysOpen = true,
            AllowsTransparency = true,
            Placement = PlacementMode.Relative,
            PlacementTarget = textArea.TextView
        };
        _view.List.PreviewMouseLeftButtonDown += (_, e) => {
            if ((System.Windows.Controls.ItemsControl.ContainerFromElement(_view.List, e.OriginalSource as DependencyObject) as System.Windows.Controls.ListBoxItem)?.Content is CompletionItem item) {
                Commit(item);
                e.Handled = true;
            }
        };

        _textArea.TextEntered += OnTextEntered;
        _textArea.PreviewKeyDown += OnPreviewKeyDown;
        _textArea.Caret.PositionChanged += (_, _) => {
            if (IsOpen && ((_textArea.Caret.Offset < _filterStart) || (_textArea.Caret.Offset > _textArea.Document.TextLength))) {
                Close();
            }
        };
        _textArea.LostKeyboardFocus += (_, _) => Close();
        _textArea.TextView.ScrollOffsetChanged += (_, _) => Close();
    }

    public bool IsOpen => _popup.IsOpen;

    /// <summary>Ctrl+Space: list every story element, filtered by the word at the caret.</summary>
    public void ShowAll() {
        int caret = _textArea.Caret.Offset;
        int wordStart = caret;
        while ((wordStart > 0) && IsNameCharacter(_textArea.Document.GetCharAt(wordStart - 1))) {
            wordStart--;
        }
        Open(null, wordStart, wordStart, "All story elements", hardSelection: true);
    }

    private void OnTextEntered(object? sender, TextCompositionEventArgs e) {
        if (e.Text.Length == 0) {
            return;
        }
        char typed = e.Text[^1];
        int caret = _textArea.Caret.Offset;

        if (typed == '.') {
            Close();
            if (TryGetTriggerBeforeDot(caret, out int keywordStart, out EntityKind kind)) {
                Open(kind, keywordStart, caret, $"{EntityKindInfo.Get(kind).PluralName}", hardSelection: false);
            }
            return;
        }

        if (IsOpen) {
            if (IsNameCharacter(typed)) {
                _hardSelection = true;
                Refresh();
            }
            else {
                Close();
            }
            return;
        }

        if (_autoSuggestNames() && char.IsLetter(typed)) {
            int wordStart = caret;
            while ((wordStart > 0) && IsNameCharacter(_textArea.Document.GetCharAt(wordStart - 1))) {
                wordStart--;
            }
            string prefix = _textArea.Document.GetText(wordStart, caret - wordStart);
            if ((prefix.Length >= 2) && char.IsUpper(prefix[0]) && HasNameCompletion(prefix)) {
                Open(null, wordStart, wordStart, "Story elements", hardSelection: true);
            }
        }
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e) {
        if (!IsOpen) {
            return;
        }
        int count = _view.List.Items.Count;
        switch (e.Key) {
            case Key.Escape:
                Close();
                e.Handled = true;
                break;
            case Key.Down:
                _hardSelection = true;
                _view.Select(_view.List.SelectedIndex + 1);
                e.Handled = true;
                break;
            case Key.Up:
                _hardSelection = true;
                _view.Select(Math.Max(0, _view.List.SelectedIndex - 1));
                e.Handled = true;
                break;
            case Key.PageDown:
                _hardSelection = true;
                _view.Select(_view.List.SelectedIndex + 8);
                e.Handled = true;
                break;
            case Key.PageUp:
                _hardSelection = true;
                _view.Select(Math.Max(0, _view.List.SelectedIndex - 8));
                e.Handled = true;
                break;
            case Key.Tab:
                if (count > 0) {
                    Commit(_view.SelectedItem ?? (CompletionItem)_view.List.Items[0]);
                    e.Handled = true;
                }
                else {
                    Close();
                }
                break;
            case Key.Enter:
                // A soft selection (nothing typed after "place.") lets Enter start a new paragraph as usual.
                if (_view.SelectedItem is not null) {
                    Commit(_view.SelectedItem);
                    e.Handled = true;
                }
                else {
                    Close();
                }
                break;
            case Key.Left:
            case Key.Right:
            case Key.Home:
            case Key.End:
                Close();
                break;
        }
    }

    private void Open(EntityKind? kind, int replaceStart, int filterStart, string header, bool hardSelection) {
        _kind = kind;
        _replaceStart = replaceStart;
        _filterStart = filterStart;
        _header = header;
        _hardSelection = hardSelection;
        if (!Refresh()) {
            return;
        }
        PositionPopup();
        _popup.IsOpen = true;
    }

    /// <summary>Re-filters the list. Returns false (and closes) when there is nothing to show.</summary>
    private bool Refresh() {
        int caret = _textArea.Caret.Offset;
        if (caret < _filterStart) {
            Close();
            return false;
        }
        string filter = _textArea.Document.GetText(_filterStart, caret - _filterStart);
        List<CompletionItem> items = BuildItems(filter);
        if ((items.Count == 0) && (filter.Length > 0)) {
            Close();
            return false;
        }
        string emptyMessage = _kind is null
            ? "No story elements yet. Add characters and places in Solution Explorer."
            : $"No {EntityKindInfo.Get(_kind.Value).PluralName.ToLowerInvariant()} yet. Right-click the {EntityKindInfo.Get(_kind.Value).PluralName} folder in Solution Explorer to add one.";
        _view.SetItems(_header, items, emptyMessage);
        _view.Select(_hardSelection && (items.Count > 0) ? 0 : -1);
        return true;
    }

    private List<CompletionItem> BuildItems(string filter) {
        List<CompletionItem> items = [];
        foreach (StoryEntity entity in _getEntities()) {
            if ((_kind is not null) && (entity.Kind != _kind)) {
                continue;
            }
            if (!string.IsNullOrWhiteSpace(entity.Name)) {
                items.Add(new CompletionItem(entity, entity.Name, false));
            }
            foreach (string alias in entity.AliasList) {
                items.Add(new CompletionItem(entity, alias, true));
            }
        }
        if (filter.Length == 0) {
            return items.Where(static item => !item.IsAlias).OrderBy(static item => item.Text, StringComparer.CurrentCultureIgnoreCase)
                .Concat(items.Where(static item => item.IsAlias).OrderBy(static item => item.Text, StringComparer.CurrentCultureIgnoreCase))
                .ToList();
        }
        return items
            .Where(item => item.Text.Contains(filter, StringComparison.CurrentCultureIgnoreCase))
            .OrderBy(item => item.Text.StartsWith(filter, StringComparison.CurrentCultureIgnoreCase) ? 0 : 1)
            .ThenBy(static item => item.IsAlias)
            .ThenBy(static item => item.Text, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    private bool HasNameCompletion(string prefix) {
        return _getEntities()
            .SelectMany(static entity => entity.AllNames)
            .Any(name => name.StartsWith(prefix, StringComparison.CurrentCultureIgnoreCase) && !string.Equals(name, prefix, StringComparison.CurrentCultureIgnoreCase));
    }

    private void Commit(CompletionItem item) {
        int caret = _textArea.Caret.Offset;
        int start = Math.Min(_replaceStart, caret);
        Close();
        _textArea.Document.Replace(start, caret - start, item.Text);
        _textArea.Caret.Offset = start + item.Text.Length;
    }

    public void Close() {
        _popup.IsOpen = false;
    }

    private void PositionPopup() {
        TextView textView = _textArea.TextView;
        TextViewPosition position = new(_textArea.Document.GetLocation(_replaceStart));
        Point point = textView.GetVisualPosition(position, VisualYPosition.LineBottom) - textView.ScrollOffset;
        _popup.HorizontalOffset = Math.Max(0, point.X - 20);
        _popup.VerticalOffset = point.Y + 2;
    }

    private bool TryGetTriggerBeforeDot(int caret, out int keywordStart, out EntityKind kind) {
        keywordStart = 0;
        kind = default;
        int dot = caret - 1;
        int start = dot;
        while ((start > 0) && char.IsLetter(_textArea.Document.GetCharAt(start - 1))) {
            start--;
        }
        if ((start == dot) || ((start > 0) && IsNameCharacter(_textArea.Document.GetCharAt(start - 1)))) {
            return false;
        }
        string keyword = _textArea.Document.GetText(start, dot - start);
        if (!EntityKindInfo.TryGetKindForKeyword(keyword, out kind)) {
            return false;
        }
        keywordStart = start;
        return true;
    }

    private static bool IsNameCharacter(char c) {
        return char.IsLetterOrDigit(c) || c is '\'' or '\u2019' or '-';
    }
}