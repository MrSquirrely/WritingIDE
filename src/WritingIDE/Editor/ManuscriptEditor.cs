using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Rendering;
using ICSharpCode.AvalonEdit.Search;

using WritingIDE.Core.Analysis;
using WritingIDE.Core.Grammar;
using WritingIDE.Core.Model;
using WritingIDE.Core.Text;
using WritingIDE.Infrastructure;
using WritingIDE.ViewModels;

namespace WritingIDE.Editor;

/// <summary>
/// The manuscript editor: AvalonEdit plus grammar squiggles, story element highlighting, IntelliSense,
/// Quick Info tooltips, quick fixes (Ctrl+.), Go To Definition (F12), Find All References (Shift+F12) and Rename (F2).
/// </summary>
public sealed class ManuscriptEditor : TextEditor {
    private static readonly FontFamily IconFont = new("Segoe MDL2 Assets");

    private readonly ChapterDocumentViewModel _owner;
    private readonly DiagnosticRenderer _diagnostics;
    private readonly EntityHighlighter _highlighter;
    private readonly CompletionController _completion;
    private readonly CommentRenderer _comments;
    private readonly NameLensGenerator _lenses;
    private readonly ToolTip _toolTip = new() { Placement = PlacementMode.Mouse, Padding = new Thickness(8, 6, 8, 6) };
    private IReadOnlyList<EntityMention> _lastMentions = [];
    private bool _focusMode;
    private readonly RevisionRenderer _revisions = new();
    private readonly DeletionGenerator _deletions = new();
    private readonly BookmarkMargin _bookmarks;
    private bool _chordPending;

    public ManuscriptEditor(ChapterDocumentViewModel owner) {
        _owner = owner;
        Document = owner.Document;

        FontFamily = new FontFamily("Cambria, Georgia, Times New Roman");
        FontSize = 16;
        WordWrap = true;
        ShowLineNumbers = true;
        HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
        Options.HighlightCurrentLine = true;
        Options.EnableHyperlinks = false;
        Options.EnableEmailHyperlinks = false;
        Options.AllowScrollBelowDocument = true;
        Options.InheritWordWrapIndentation = false;
        TextArea.TextView.Margin = new Thickness(12, 8, 24, 8);
        TextArea.SelectionForeground = null;
        TextArea.SelectionBorder = null;
        ApplyTheme();
        AppTheme.Changed += ApplyTheme;

        _diagnostics = new DiagnosticRenderer(Document);
        TextArea.TextView.BackgroundRenderers.Add(_diagnostics);
        _highlighter = new EntityHighlighter(Document) { IsEnabled = owner.Shell.HighlightStoryElements };
        TextArea.TextView.LineTransformers.Add(_highlighter);
        _completion = new CompletionController(
            TextArea,
            () => _owner.Shell.Project?.Entities.ToList() ?? [],
            () => _owner.Shell.AutoCompleteNames);
        SearchPanel.Install(TextArea);
        _comments = new CommentRenderer(owner.CommentSegments);
        TextArea.TextView.BackgroundRenderers.Add(_comments);
        MainViewModel shell = owner.Shell;
        _lenses = new NameLensGenerator((lenses, width) => NameLensGenerator.CreateLensVisual(lenses, width,
            entity => shell.OpenEntity(entity), shell.FindReferences, chapter => shell.OpenChapter(chapter)));
        TextArea.TextView.ElementGenerators.Add(_lenses);
        TextArea.TextView.ElementGenerators.Add(_deletions);
        // Lenses are as wide as the text area: rebuild them when it is resized (or first laid out, in a new pane).
        TextArea.TextView.SizeChanged += (_, e) => {
            if (e.WidthChanged && _lenses.HasLenses) {
                Dispatcher.InvokeAsync(() => TextArea.TextView.Redraw(), System.Windows.Threading.DispatcherPriority.Background);
            }
        };
        TextArea.TextView.BackgroundRenderers.Add(_revisions);
        TextArea.TextView.LineTransformers.Add(new ManuscriptMarkupColorizer());
        _bookmarks = new BookmarkMargin(owner.CommentSegments, line => owner.ToggleBookmark(line));
        TextArea.LeftMargins.Insert(0, _bookmarks);
        ApplyRevisions(owner.RevisionChanges);
        TextArea.TextEntered += (_, _) => KeepCaretCentered();
        SizeChanged += (_, _) => UpdateFocusLayout();

        TextArea.Caret.PositionChanged += (_, _) => UpdateCaretStatus();
        TextArea.GotKeyboardFocus += (_, _) => UpdateCaretStatus();
        MouseHover += OnMouseHover;
        MouseHoverStopped += (_, _) => _toolTip.IsOpen = false;
        MouseLeave += (_, _) => _toolTip.IsOpen = false;
        TextArea.TextEntering += (_, _) => _toolTip.IsOpen = false;
        PreviewKeyDown += OnPreviewKeyDown;
        PreviewMouseLeftButtonDown += OnPreviewMouseLeftButtonDown;
        ContextMenu = new ContextMenu();
        ContextMenuOpening += OnContextMenuOpening;
    }

    /// <summary>Takes the editor colours from the active palette (called again whenever the theme changes).</summary>
    private void ApplyTheme() {
        Brush Resource(string key) {
            return Application.Current.TryFindResource(key) as Brush ?? Brushes.Transparent;
        }

        Background = Resource(_focusMode ? "FocusBackgroundBrush" : "EditorBackgroundBrush");
        Foreground = Resource("EditorForegroundBrush");
        LineNumbersForeground = Resource("EditorLineNumberBrush");
        TextArea.TextView.CurrentLineBackground = Resource("EditorCurrentLineBrush");
        TextArea.TextView.CurrentLineBorder = new Pen(Resource("EditorCurrentLineBorderBrush"), 1);
        TextArea.SelectionBrush = Resource("EditorSelectionBrush");
        TextArea.TextView.Redraw();
    }

    /// <summary>Releases the theme subscription when the chapter is closed.</summary>
    public void Detach() {
        AppTheme.Changed -= ApplyTheme;
        _completion.Close();
    }

    public void ApplyAnalysis(AnalysisResult result) {
        _diagnostics.SetDiagnostics(result.Diagnostics, Document.TextLength);
        _highlighter.SetMentions(result.Mentions, Document.TextLength);
        _lastMentions = result.Mentions;
        UpdateLenses();
        TextArea.TextView.Redraw();
    }

    // ------------------------------------------------------------------
    // Name lenses (CodeLens), comments and focus mode
    // ------------------------------------------------------------------

    /// <summary>Recomputes the name lenses (another chapter's analysis may have changed the counts) and redraws if needed.</summary>
    public void RefreshLenses() {
        if (UpdateLenses()) {
            TextArea.TextView.Redraw();
        }
    }

    /// <summary>One lens line above the first mention of each story element in this chapter (at most four per line).</summary>
    private bool UpdateLenses() {
        Dictionary<int, IReadOnlyList<NameLensInfo>> byLine = [];
        if (_owner.Shell.ShowNameLenses && !_focusMode) {
            Dictionary<int, List<NameLensInfo>> lines = [];
            HashSet<StoryEntity> seen = [];
            foreach (EntityMention mention in _lastMentions.OrderBy(static mention => mention.Offset)) {
                if ((mention.End > Document.TextLength) || !seen.Add(mention.Entity)) {
                    continue;
                }
                int line = Document.GetLineByOffset(mention.Offset).LineNumber;
                if (!lines.TryGetValue(line, out List<NameLensInfo>? entries)) {
                    entries = [];
                    lines[line] = entries;
                }
                if (entries.Count < 4) {
                    entries.Add(_owner.Shell.GetNameLens(mention.Entity, _owner.Chapter));
                }
            }
            byLine = lines.ToDictionary(static pair => pair.Key, static pair => (IReadOnlyList<NameLensInfo>)pair.Value);
        }
        bool changed = (byLine.Count > 0) || _lenses.HasLenses;
        _lenses.SetLenses(byLine);
        return changed;
    }

    /// <summary>Distraction-free writing: no line numbers or lenses, a centred column of larger text, typewriter scrolling.</summary>
    public void SetFocusMode(bool enabled) {
        if (_focusMode == enabled) {
            return;
        }
        _focusMode = enabled;
        ShowLineNumbers = !enabled;
        Options.HighlightCurrentLine = !enabled;
        FontSize = enabled ? 19 : 16;
        ApplyTheme();
        UpdateFocusLayout();
        UpdateLenses();
        TextArea.TextView.Redraw();
        if (enabled) {
            Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, () => {
                TextArea.Focus();
                KeepCaretCentered();
            });
        }
    }

    private void UpdateFocusLayout() {
        if (_focusMode) {
            double side = Math.Max(32, (ActualWidth - 760) / 2);
            TextArea.TextView.Margin = new Thickness(side, 48, side, 48);
        }
        else {
            TextArea.TextView.Margin = new Thickness(12, 8, 24, 8);
        }
    }

    /// <summary>Typewriter scrolling in focus mode: the line being written stays in the middle of the screen.</summary>
    private void KeepCaretCentered() {
        if (!_focusMode || !TextArea.TextView.VisualLinesValid) {
            return;
        }
        double y = TextArea.TextView.GetVisualPosition(TextArea.Caret.Position, VisualYPosition.LineMiddle).Y;
        ScrollToVerticalOffset(Math.Max(0, y - (TextArea.TextView.ActualHeight / 2)));
    }

    public void SetActiveComment(ChapterComment? comment) {
        if (_comments.ActiveComment != comment) {
            _comments.ActiveComment = comment;
            TextArea.TextView.Redraw();
        }
    }

    public void SetCommentsVisible(bool visible) {
        _comments.IsEnabled = visible;
        TextArea.TextView.Redraw();
    }

    /// <summary>Selects the commented text and scrolls to it.</summary>
    public void RevealComment(ChapterComment comment) {
        CommentSegment? segment = _owner.CommentSegments.FirstOrDefault(segment => segment.Comment == comment);
        if (segment is not null) {
            SelectAndReveal(segment.StartOffset, segment.Length);
        }
    }

    /// <summary>Shows revision-mode changes (inserted text shaded, deleted text struck through in place).</summary>
    public void ApplyRevisions(IReadOnlyList<RevisionChange> changes) {
        _revisions.Changes = changes;
        _deletions.Changes = changes;
        TextArea.TextView.Redraw();
    }

    public void RedrawMarkers() {
        _bookmarks.InvalidateVisual();
        TextArea.TextView.Redraw();
    }

    private RevisionChange? GetChangeAt(int offset) {
        return _owner.RevisionChanges.FirstOrDefault(change => (offset >= change.Offset) && (offset <= change.End));
    }

    private void AddComment() {
        _owner.AddComment(SelectionStart, SelectionLength);
    }

    public void RefreshHighlighting() {
        _highlighter.IsEnabled = _owner.Shell.HighlightStoryElements;
        TextArea.TextView.Redraw();
    }

    public void SelectAndReveal(int offset, int length) {
        offset = Math.Clamp(offset, 0, Document.TextLength);
        length = Math.Clamp(length, 0, Document.TextLength - offset);
        Select(offset, length);
        TextLocation location = Document.GetLocation(offset);
        ScrollTo(location.Line, location.Column);
        TextArea.Caret.BringCaretToView();
        TextArea.Focus();
    }

    private void UpdateCaretStatus() {
        _owner.Shell.CaretLine = TextArea.Caret.Line;
        _owner.Shell.CaretColumn = TextArea.Caret.Column;
    }

    // ------------------------------------------------------------------
    // Keyboard and mouse
    // ------------------------------------------------------------------

    private void OnPreviewKeyDown(object sender, KeyEventArgs e) {
        ModifierKeys modifiers = Keyboard.Modifiers;
        Key key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (_chordPending && (key is not (Key.LeftCtrl or Key.RightCtrl))) {
            // Visual Studio's bookmark chords: Ctrl+K then K (toggle), N (next), P (previous), L (clear all).
            _chordPending = false;
            _owner.Shell.StatusText = "Ready";
            switch (key) {
                case Key.K:
                    _owner.ToggleBookmark(TextArea.Caret.Line);
                    break;
                case Key.N:
                    _owner.GoToBookmark(forward: true);
                    break;
                case Key.P:
                    _owner.GoToBookmark(forward: false);
                    break;
                case Key.L:
                    _owner.ClearBookmarks();
                    break;
                default:
                    _owner.Shell.StatusText = "That key combination is not a command.";
                    break;
            }
            e.Handled = true;
            return;
        }
        if ((key == Key.K) && (modifiers == ModifierKeys.Control)) {
            _chordPending = true;
            _owner.Shell.StatusText = "(Ctrl+K) was pressed. Waiting for the second key of the chord...";
            e.Handled = true;
        }
        else if ((key == Key.Enter) && (modifiers == (ModifierKeys.Control | ModifierKeys.Shift))) {
            _owner.InsertSceneBreak();
            e.Handled = true;
        }
        else if ((key == Key.Space) && (modifiers == ModifierKeys.Control)) {
            _completion.ShowAll();
            e.Handled = true;
        }
        else if ((key == Key.M) && (modifiers == (ModifierKeys.Control | ModifierKeys.Alt))) {
            AddComment();
            e.Handled = true;
        }
        else if ((key == Key.OemPeriod) && (modifiers == ModifierKeys.Control)) {
            ShowQuickActions();
            e.Handled = true;
        }
        else if (key == Key.F12) {
            StoryEntity? entity = GetEntityAt(TextArea.Caret.Offset);
            if (entity is null) {
                _owner.Shell.StatusText = "No story element at the caret";
            }
            else if (modifiers == ModifierKeys.Shift) {
                _owner.Shell.FindReferences(entity);
            }
            else {
                _owner.Shell.OpenEntity(entity);
            }
            e.Handled = true;
        }
        else if ((key == Key.F2) && (modifiers == ModifierKeys.None)) {
            StoryEntity? entity = GetEntityAt(TextArea.Caret.Offset);
            if (entity is not null) {
                _owner.Shell.RenameEntity(entity);
            }
            else {
                _owner.Shell.StatusText = "Rename works on story element names";
            }
            e.Handled = true;
        }
    }

    /// <summary>Clicks on a name lens run its action; Ctrl+Click on a name goes to its definition.</summary>
    private void OnPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e) {
        for (DependencyObject? element = e.OriginalSource as DependencyObject; (element is not null) && (element != this); element = VisualTreeHelper.GetParent(element)) {
            if ((element is FrameworkElement { Tag: Action action })) {
                action();
                e.Handled = true;
                return;
            }
            if (element is not Visual) {
                break;
            }
        }
        if (Keyboard.Modifiers != ModifierKeys.Control) {
            return;
        }
        int? offset = GetOffsetFromPoint(e.GetPosition(this));
        StoryEntity? entity = offset is null ? null : GetEntityAt(offset.Value);
        if (entity is not null) {
            _owner.Shell.OpenEntity(entity);
            e.Handled = true;
        }
    }

    private int? GetOffsetFromPoint(Point point) {
        TextViewPosition? position = GetPositionFromPoint(point);
        return position is null ? null : Document.GetOffset(position.Value.Location);
    }

    private StoryEntity? GetEntityAt(int offset) {
        MentionSegment? mention = _highlighter.GetMentionAt(offset);
        if (mention is not null) {
            return mention.Mention.Entity;
        }
        (int start, int length) = Tokenizer.GetWordAt(Document.Text, offset);
        if (length == 0) {
            return null;
        }
        string word = Tokenizer.StripPossessive(Document.GetText(start, length));
        return _owner.Shell.GrammarOptions.Entities.FindByName(word);
    }

    // ------------------------------------------------------------------
    // Quick Info
    // ------------------------------------------------------------------

    private void OnMouseHover(object sender, MouseEventArgs e) {
        int? offset = GetOffsetFromPoint(e.GetPosition(this));
        if (offset is null) {
            return;
        }
        StackPanel? content = BuildQuickInfo(offset.Value);
        if (content is null) {
            return;
        }
        _toolTip.PlacementTarget = this;
        _toolTip.Content = content;
        _toolTip.IsOpen = true;
        e.Handled = true;
    }

    private StackPanel? BuildQuickInfo(int offset) {
        StackPanel panel = new() { MaxWidth = 440 };
        MentionSegment? mention = _highlighter.GetMentionAt(offset);
        if (mention is not null) {
            StoryEntity entity = mention.Mention.Entity;
            panel.Children.Add(CreateGlyphRow(entity.KindInfo.Glyph, Visuals.GetKindBrush(entity.Kind),
                new TextBlock {
                    Inlines = {
                        new System.Windows.Documents.Run(entity.KindInfo.DisplayName.ToLowerInvariant() + " ") { Foreground = Visuals.MessageBrush },
                        new System.Windows.Documents.Run(entity.Name) { FontWeight = FontWeights.SemiBold, Foreground = Visuals.GetKindBrush(entity.Kind) }
                    }
                }));
            if (!string.IsNullOrWhiteSpace(entity.Summary)) {
                panel.Children.Add(new TextBlock { Text = entity.Summary, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0) });
            }
            foreach (EntityAttribute attribute in entity.Attributes.Where(static a => !string.IsNullOrWhiteSpace(a.Value))) {
                panel.Children.Add(new TextBlock {
                    TextWrapping = TextWrapping.Wrap,
                    Inlines = {
                        new System.Windows.Documents.Run(attribute.Key + ": ") { Foreground = Visuals.SubtleBrush },
                        new System.Windows.Documents.Run(attribute.Value)
                    }
                });
            }
            if (entity.AliasList.Count > 0) {
                panel.Children.Add(new TextBlock { Text = "Also known as: " + string.Join(", ", entity.AliasList), Foreground = Visuals.SubtleBrush, Margin = new Thickness(0, 2, 0, 0) });
            }
            panel.Children.Add(new TextBlock { Text = "F12 Go To Definition  \u00B7  Shift+F12 Find All References  \u00B7  F2 Rename", Foreground = Visuals.SubtleBrush, FontSize = 11, Margin = new Thickness(0, 6, 0, 0) });
        }
        foreach (DiagnosticMarker marker in _diagnostics.GetMarkersAt(offset)) {
            GrammarDiagnostic diagnostic = marker.Diagnostic;
            if (panel.Children.Count > 0) {
                panel.Children.Add(new Separator { Margin = new Thickness(0, 4, 0, 4) });
            }
            panel.Children.Add(CreateGlyphRow(Visuals.GetSeverityGlyph(diagnostic.Severity), Visuals.GetSeverityBrush(diagnostic.Severity),
                new TextBlock {
                    TextWrapping = TextWrapping.Wrap,
                    Inlines = {
                        new System.Windows.Documents.Run(diagnostic.RuleId) { Foreground = Visuals.MessageBrush },
                        new System.Windows.Documents.Run(": " + diagnostic.Message)
                    }
                }));
            if (diagnostic.Fixes.Count > 0) {
                panel.Children.Add(new TextBlock { Text = "Show potential fixes (Ctrl+.)", Foreground = Visuals.SubtleBrush, FontSize = 11, Margin = new Thickness(22, 2, 0, 0) });
            }
        }
        return panel.Children.Count > 0 ? panel : null;
    }

    private static DockPanel CreateGlyphRow(string glyph, Brush brush, UIElement content) {
        DockPanel row = new();
        TextBlock icon = new() { Text = glyph, FontFamily = IconFont, Foreground = brush, Width = 22, FontSize = 14, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 1, 0, 0) };
        DockPanel.SetDock(icon, Dock.Left);
        row.Children.Add(icon);
        row.Children.Add(content);
        return row;
    }

    // ------------------------------------------------------------------
    // Context menu and Quick Actions (Ctrl+.)
    // ------------------------------------------------------------------

    private void OnContextMenuOpening(object sender, ContextMenuEventArgs e) {
        int offset = TextArea.Caret.Offset;
        if ((e.CursorLeft >= 0) && (e.CursorTop >= 0)) {
            int? clicked = GetOffsetFromPoint(new Point(e.CursorLeft, e.CursorTop));
            if (clicked is not null) {
                offset = clicked.Value;
                bool insideSelection = (SelectionLength > 0) && (offset >= SelectionStart) && (offset <= SelectionStart + SelectionLength);
                if (!insideSelection) {
                    TextArea.Caret.Offset = offset;
                }
            }
        }
        PopulateMenu(ContextMenu, offset, includeClipboard: true);
    }

    private void ShowQuickActions() {
        ContextMenu menu = new();
        PopulateMenu(menu, TextArea.Caret.Offset, includeClipboard: false);
        if (menu.Items.Count == 0) {
            menu.Items.Add(new MenuItem { Header = "No quick actions available here", IsEnabled = false });
        }
        Point point = TextArea.TextView.GetVisualPosition(new TextViewPosition(TextArea.Caret.Location), VisualYPosition.LineBottom) - TextArea.TextView.ScrollOffset;
        menu.PlacementTarget = TextArea.TextView;
        menu.Placement = PlacementMode.Relative;
        menu.HorizontalOffset = point.X;
        menu.VerticalOffset = point.Y;
        // Like Visual Studio's light bulb: the first fix is focused, so Enter applies it.
        menu.Opened += (_, _) => menu.Items.OfType<MenuItem>().FirstOrDefault(static item => item.IsEnabled)?.Focus();
        menu.IsOpen = true;
    }

    private void PopulateMenu(ContextMenu menu, int offset, bool includeClipboard) {
        menu.Items.Clear();
        MainViewModel shell = _owner.Shell;

        foreach (DiagnosticMarker marker in _diagnostics.GetMarkersAt(offset)) {
            GrammarDiagnostic diagnostic = marker.Diagnostic;
            menu.Items.Add(new MenuItem {
                Header = CreateHeaderText($"{diagnostic.RuleId}: {diagnostic.Message}"),
                Icon = CreateIcon(Visuals.GetSeverityGlyph(diagnostic.Severity), Visuals.GetSeverityBrush(diagnostic.Severity)),
                IsEnabled = false
            });
            foreach (TextFix fix in diagnostic.Fixes) {
                TextBlock header = CreateHeaderText(fix.Title);
                header.FontWeight = FontWeights.SemiBold;
                MenuItem item = new() { Header = header, Icon = CreateIcon("\uE82F", Brushes.Goldenrod) };
                item.Click += (_, _) => ApplyFix(marker, fix);
                menu.Items.Add(item);
            }
            if (diagnostic.Word is not null) {
                string word = Tokenizer.StripPossessive(diagnostic.Word);
                menu.Items.Add(CreateMenuItem($"Add '{word}' to project dictionary", "\uE8AC", () => shell.AddToDictionary(word)));
                if (char.IsUpper(word[0])) {
                    menu.Items.Add(CreateNewEntityMenu(word));
                }
            }
            menu.Items.Add(CreateMenuItem($"Suppress {diagnostic.RuleId} in this project", "\uE8D8", () => shell.DisableRule(diagnostic.RuleId)));
            menu.Items.Add(new Separator());
        }

        StoryEntity? entity = GetEntityAt(offset);
        if (entity is not null) {
            menu.Items.Add(CreateMenuItem($"Go To Definition of '{entity.Name}'", "\uE8A7", () => shell.OpenEntity(entity), "F12"));
            menu.Items.Add(CreateMenuItem("Find All References", "\uE721", () => shell.FindReferences(entity), "Shift+F12"));
            menu.Items.Add(CreateMenuItem("Rename...", "\uE8AC", () => shell.RenameEntity(entity), "F2"));
            menu.Items.Add(new Separator());
        }
        else if (shell.Project is not null) {
            (int start, int length) = Tokenizer.GetWordAt(Document.Text, offset);
            string word = length > 0 ? Tokenizer.StripPossessive(Document.GetText(start, length)) : "";
            bool alreadyOffered = _diagnostics.GetMarkersAt(offset).Any(static marker => marker.Diagnostic.Word is not null);
            if ((word.Length > 1) && char.IsUpper(word[0]) && !alreadyOffered) {
                menu.Items.Add(CreateNewEntityMenu(word));
                menu.Items.Add(new Separator());
            }
        }

        // Revision mode: accept or reject the change under the cursor.
        RevisionChange? change = GetChangeAt(offset);
        if (change is not null) {
            menu.Items.Add(new MenuItem { Header = CreateHeaderText(change.Description), IsEnabled = false, Icon = CreateIcon("\uE70F", Visuals.MessageBrush) });
            menu.Items.Add(CreateMenuItem("Accept Change", "\uE73E", () => _owner.AcceptChange(change)));
            menu.Items.Add(CreateMenuItem("Reject Change", "\uE711", () => _owner.RejectChange(change)));
            menu.Items.Add(new Separator());
        }

        menu.Items.Add(CreateMenuItem(SelectionLength > 0 ? "Add Comment on Selection" : "Add Comment", "\uE90A", AddComment, "Ctrl+Alt+M"));
        CommentSegment? commented = _owner.CommentSegments.FindSegmentsContaining(offset).FirstOrDefault(static segment => segment.Comment.Kind == MarkerKind.Comment);
        if (commented is not null) {
            ChapterComment comment = commented.Comment;
            menu.Items.Add(CreateMenuItem(comment.IsResolved ? "Reopen Comment" : "Resolve Comment", "\uE73E", () => comment.IsResolved = !comment.IsResolved));
            menu.Items.Add(CreateMenuItem("Delete Comment", "\uE74D", () => _owner.DeleteComment(comment)));
        }
        menu.Items.Add(CreatePlotThreadMenu());
        menu.Items.Add(CreateMenuItem("Toggle Bookmark", "\uE8A4", () => _owner.ToggleBookmark(Document.GetLineByOffset(offset).LineNumber), "Ctrl+K, Ctrl+K"));
        menu.Items.Add(new Separator());
        menu.Items.Add(CreateMenuItem("Insert Scene Break", "\uE76F", _owner.InsertSceneBreak, "Ctrl+Shift+Enter"));
        menu.Items.Add(CreateMenuItem("Split Chapter Here...", "\uE8C6", () => shell.SplitChapter(_owner, offset)));
        MenuItem ai = CreateAiMenu();
        if (ai.Items.Count > 0) {
            menu.Items.Add(ai);
        }
        menu.Items.Add(new Separator());

        if (includeClipboard) {
            menu.Items.Add(new MenuItem { Command = ApplicationCommands.Cut, CommandTarget = TextArea, Icon = CreateIcon("\uE8C6", Visuals.IconBrush) });
            menu.Items.Add(new MenuItem { Command = ApplicationCommands.Copy, CommandTarget = TextArea, Icon = CreateIcon("\uE8C8", Visuals.IconBrush) });
            menu.Items.Add(new MenuItem { Command = ApplicationCommands.Paste, CommandTarget = TextArea, Icon = CreateIcon("\uE77F", Visuals.IconBrush) });
        }
        while ((menu.Items.Count > 0) && (menu.Items[^1] is Separator)) {
            menu.Items.RemoveAt(menu.Items.Count - 1);
        }
    }

    /// <summary>"Mark for Plot Thread": the selection (or the sentence at the caret) as a setup, development or payoff.</summary>
    private MenuItem CreatePlotThreadMenu() {
        MenuItem parent = new() { Header = CreateHeaderText("Mark for Plot Thread"), Icon = CreateIcon("\uE8F1", Visuals.GetKindBrush(EntityKind.Event)) };
        NovelProject? project = _owner.Shell.Project;
        foreach (PlotThread thread in project?.PlotThreads ?? []) {
            MenuItem threadItem = new() { Header = CreateHeaderText(thread.Name) };
            foreach (ThreadRole role in Enum.GetValues<ThreadRole>()) {
                threadItem.Items.Add(CreateMenuItem(role.ToString(), "\uE8F1", () => _owner.MarkPlotThread(thread, role, SelectionStart, SelectionLength)));
            }
            parent.Items.Add(threadItem);
        }
        if (parent.Items.Count > 0) {
            parent.Items.Add(new Separator());
        }
        parent.Items.Add(CreateMenuItem("New Plot Thread...", "\uE710", () => {
            PlotThread? thread = _owner.Shell.CreatePlotThread();
            if (thread is not null) {
                _owner.MarkPlotThread(thread, ThreadRole.Setup, SelectionStart, SelectionLength);
            }
        }));
        return parent;
    }

    /// <summary>AI tools on the selection (only when an AI provider is set up).</summary>
    private MenuItem CreateAiMenu() {
        MenuItem parent = new() { Header = CreateHeaderText("AI"), Icon = CreateIcon("\uE99A", Visuals.ProjectBrush) };
        if (SelectionLength == 0) {
            return parent;
        }
        int start = SelectionStart;
        int length = SelectionLength;
        foreach ((Core.Ai.RewriteKind kind, string title) in new[] {
            (Core.Ai.RewriteKind.Tighten, "Tighten"),
            (Core.Ai.RewriteKind.Vivid, "Make More Vivid"),
            (Core.Ai.RewriteKind.Simplify, "Simplify"),
            (Core.Ai.RewriteKind.FixGrammar, "Fix Grammar and Spelling"),
            (Core.Ai.RewriteKind.Custom, "Rewrite with Instructions...")
        }) {
            parent.Items.Add(CreateMenuItem(title, "\uE99A", () => _ = _owner.Shell.Ai.RewriteAsync(_owner, start, length, kind)));
        }
        return parent;
    }

    private MenuItem CreateNewEntityMenu(string word) {
        MenuItem parent = new() { Header = CreateHeaderText($"Create story element '{word}'"), Icon = CreateIcon("\uE710", Visuals.ProjectBrush) };
        foreach (EntityKindInfo info in EntityKindInfo.All) {
            MenuItem item = new() { Header = info.DisplayName, Icon = CreateIcon(info.Glyph, Visuals.GetKindBrush(info.Kind)) };
            item.Click += (_, _) => _owner.Shell.CreateEntity(info.Kind, word, open: false);
            parent.Items.Add(item);
        }
        return parent;
    }

    private void ApplyFix(DiagnosticMarker marker, TextFix fix) {
        if (marker.EndOffset > Document.TextLength) {
            return;
        }
        Document.Replace(marker.StartOffset, marker.Length, fix.Replacement);
        TextArea.Focus();
    }

    private static MenuItem CreateMenuItem(string header, string glyph, Action action, string? gesture = null) {
        MenuItem item = new() { Header = CreateHeaderText(header), Icon = CreateIcon(glyph, Visuals.IconBrush), InputGestureText = gesture ?? "" };
        item.Click += (_, _) => action();
        return item;
    }

    /// <summary>A TextBlock header so underscores are not treated as access keys.</summary>
    private static TextBlock CreateHeaderText(string text) {
        return new() { Text = text, MaxWidth = 520, TextTrimming = TextTrimming.CharacterEllipsis };
    }

    private static TextBlock CreateIcon(string glyph, Brush brush) {
        return new() { Text = glyph, FontFamily = IconFont, Foreground = brush, FontSize = 13, HorizontalAlignment = HorizontalAlignment.Center };
    }
}