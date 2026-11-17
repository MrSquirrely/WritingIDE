using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

using WritingIDE.Core.Model;
using WritingIDE.Services;
using WritingIDE.ViewModels;

namespace WritingIDE.Views;

public partial class MainWindow : HandyControl.Controls.Window {
    private readonly MainViewModel _viewModel;

    public MainWindow(MainViewModel viewModel) {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;
        viewModel.PropertyChanged += (_, e) => {
            if (e.PropertyName == nameof(MainViewModel.IsDistractionFree)) {
                ApplyDistractionFree(viewModel.IsDistractionFree);
            }
            else if ((e.PropertyName == nameof(MainViewModel.ActiveDocument)) && viewModel.IsDistractionFree && (viewModel.ActiveChapter is null)) {
                viewModel.IsDistractionFree = false;
            }
            else if (e.PropertyName == nameof(MainViewModel.SecondaryView)) {
                UpdateSplitPane();
            }
        };
    }

    /// <summary>Shows or hides the second editor pane (half the width of the editor area by default).</summary>
    private void UpdateSplitPane() {
        bool open = (_viewModel.SecondaryView is not null) && !_viewModel.IsDistractionFree;
        SplitPane.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
        SplitSplitterColumn.Width = open ? new GridLength(5) : new GridLength(0);
        SplitPaneColumn.Width = open ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
        SplitPaneColumn.MinWidth = open ? 200 : 0;
    }

    private void OnShowBookmarks(object sender, RoutedEventArgs e) {
        _viewModel.RefreshBookmarks();
        _viewModel.SelectedBottomTab = MainViewModel.BookmarksTab;
    }

    private void OnShowTaskList(object sender, RoutedEventArgs e) {
        _viewModel.SelectedBottomTab = MainViewModel.TaskListTab;
    }

    private void OnShowOutline(object sender, RoutedEventArgs e) {
        OutlineTab.IsSelected = true;
        _viewModel.ActiveChapter?.RefreshOutline();
    }

    /// <summary>Clicking an outline entry goes there.</summary>
    private void OnOutlineSelected(object sender, SelectionChangedEventArgs e) {
        if ((((ListBox)sender).SelectedItem is OutlineItem item) && (_viewModel.ActiveChapter is ChapterDocumentViewModel chapter)) {
            chapter.Select(item.Offset, item.Length);
            ((ListBox)sender).SelectedItem = null;
        }
    }

    /// <summary>The Extensions menu lists the tool windows and commands extensions registered.</summary>
    private void OnExtensionsMenuOpened(object sender, RoutedEventArgs e) {
        if (!ReferenceEquals(e.OriginalSource, ExtensionsMenu)) {
            return;
        }
        ExtensionsMenu.Items.Clear();
        foreach ((ExtensionInfo extension, WritingIDE.Sdk.PluginToolWindow window) in _viewModel.Extensions.ToolWindows) {
            MenuItem item = new() {
                Header = new TextBlock { Text = window.Title },
                Icon = new TextBlock { Text = Infrastructure.Visuals.PluginGlyph, FontFamily = new FontFamily("Segoe MDL2 Assets"), Foreground = Infrastructure.Visuals.IconBrush, FontSize = 13, HorizontalAlignment = HorizontalAlignment.Center },
                ToolTip = window.Description.Length > 0 ? window.Description : null,
                IsEnabled = _viewModel.HasProject
            };
            item.Click += (_, _) => _viewModel.OpenPluginToolWindow(extension, window);
            ExtensionsMenu.Items.Add(item);
        }
        if (ExtensionsMenu.Items.Count > 0) {
            ExtensionsMenu.Items.Add(new Separator());
        }
        int toolWindowItems = ExtensionsMenu.Items.Count;
        foreach (WritingIDE.Sdk.PluginCommand command in _viewModel.Extensions.Commands) {
            WritingIDE.Sdk.PluginCommand captured = command;
            MenuItem item = new() { Header = new TextBlock { Text = command.Title }, ToolTip = command.Description.Length > 0 ? command.Description : null, IsEnabled = _viewModel.HasProject };
            item.Click += (_, _) => _viewModel.Extensions.Run(captured.Title, () => captured.Execute(_viewModel.Extensions.ProjectApi));
            ExtensionsMenu.Items.Add(item);
        }
        if ((toolWindowItems > 0) && (ExtensionsMenu.Items.Count == toolWindowItems)) {
            // Tool windows but no commands: no separator is needed below them.
            ExtensionsMenu.Items.RemoveAt(toolWindowItems - 1);
        }
        if (ExtensionsMenu.Items.Count == 0) {
            ExtensionsMenu.Items.Add(new MenuItem { Header = "(no extension commands)", IsEnabled = false });
        }
        ExtensionsMenu.Items.Add(new Separator());
        ExtensionsMenu.Items.Add(new MenuItem { Header = "_Manage Extensions and API...", Command = _viewModel.ExtensionsCommand });
    }

    private readonly List<(DefinitionBase Definition, GridLength Size)> _savedSizes = [];
    private WindowState _savedState;

    /// <summary>Distraction-free writing: full screen, everything but the chapter hidden. Undone exactly on exit.</summary>
    private void ApplyDistractionFree(bool enabled) {
        UIElement[] chrome = [ToolBarHost, StatusBarHost, SolutionPanel, LeftSplitter, BottomSplitter, BottomPanel, RightSplitter, PropertiesPanel];
        foreach (UIElement element in chrome) {
            element.Visibility = enabled ? Visibility.Collapsed : Visibility.Visible;
        }
        if (enabled) {
            _savedSizes.Clear();
            foreach (ColumnDefinition column in MainGrid.ColumnDefinitions) {
                _savedSizes.Add((column, column.Width));
            }
            foreach (RowDefinition row in MainGrid.RowDefinitions) {
                _savedSizes.Add((row, row.Height));
            }
            foreach (ColumnDefinition column in MainGrid.ColumnDefinitions.Where(column => MainGrid.ColumnDefinitions.IndexOf(column) != 2)) {
                column.MinWidth = 0;
                column.Width = new GridLength(0);
            }
            foreach (RowDefinition row in MainGrid.RowDefinitions.Skip(1)) {
                row.MinHeight = 0;
                row.Height = new GridLength(0);
            }
            DocumentWell.Tag = "Focus";
            UpdateSplitPane();
            _savedState = WindowState;
            // HandyControl's IsFullScreen fails here (negative CaptionHeight), so: no title bar, maximized.
            SetChromeSafely(() => {
                ShowNonClientArea = false;
                WindowState = WindowState.Maximized;
            });
        }
        else {
            foreach ((DefinitionBase definition, GridLength size) in _savedSizes) {
                if (definition is ColumnDefinition column) {
                    column.Width = size;
                    column.MinWidth = size.IsStar ? 300 : (size.Value > 10 ? 150 : 0);
                }
                else if (definition is RowDefinition row) {
                    row.Height = size;
                    row.MinHeight = size.IsStar ? 200 : (size.Value > 10 ? 80 : 0);
                }
            }
            DocumentWell.Tag = null;
            UpdateSplitPane();
            SetChromeSafely(() => {
                ShowNonClientArea = true;
                WindowState = _savedState;
            });
        }
    }

    /// <summary>Window chrome changes are cosmetic: a failure must not stop the editor switching modes.</summary>
    private void SetChromeSafely(Action change) {
        try {
            change();
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException) {
            _viewModel.Log($"warning: could not change the window frame: {exception.Message}");
        }
    }

    /// <summary>Esc leaves distraction-free mode, unless something else (IntelliSense, a search box) used it first.</summary>
    protected override void OnKeyDown(KeyEventArgs e) {
        if ((e.Key == Key.Escape) && !e.Handled && _viewModel.IsDistractionFree) {
            _viewModel.IsDistractionFree = false;
            e.Handled = true;
        }
        base.OnKeyDown(e);
    }

    /// <summary>The editor would take Ctrl+Shift+I as text input, so Import is handled before it gets there (Ctrl+I is italic).</summary>
    protected override void OnPreviewKeyDown(KeyEventArgs e) {
        if ((e.Key == Key.I) && (Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift)) && _viewModel.ImportCommand.CanExecute(null)) {
            _viewModel.ImportCommand.Execute(null);
            e.Handled = true;
            return;
        }
        base.OnPreviewKeyDown(e);
    }

    protected override void OnClosing(CancelEventArgs e) {
        if (!_viewModel.ConfirmCloseProject()) {
            e.Cancel = true;
        }
        base.OnClosing(e);
    }

    protected override void OnClosed(EventArgs e) {
        _viewModel.Shutdown();
        base.OnClosed(e);
    }

    // ------------------------------------------------------------------
    // Solution Explorer
    // ------------------------------------------------------------------

    private TreeNode? SelectedNode => SolutionTree.SelectedItem as TreeNode;

    private void OnTreeDoubleClick(object sender, MouseButtonEventArgs e) {
        if (FindAncestor<TreeViewItem>(e.OriginalSource as DependencyObject) is null) {
            return;
        }
        if (OpenNode(SelectedNode)) {
            e.Handled = true;
        }
    }

    private void OnTreeKeyDown(object sender, KeyEventArgs e) {
        TreeNode? node = SelectedNode;
        switch (e.Key) {
            case Key.Enter:
                e.Handled = OpenNode(node);
                break;
            case Key.Delete when node is ChapterNode chapterNode:
                _viewModel.DeleteChapter(chapterNode.Chapter);
                e.Handled = true;
                break;
            case Key.Delete when node is EntityNode entityNode:
                _viewModel.DeleteEntity(entityNode.Entity);
                e.Handled = true;
                break;
            case Key.F2 when node is ChapterNode chapterNode:
                _viewModel.RenameChapter(chapterNode.Chapter);
                e.Handled = true;
                break;
            case Key.F2 when node is EntityNode entityNode:
                _viewModel.RenameEntity(entityNode.Entity);
                e.Handled = true;
                break;
            case Key.F2 when node is BookNode bookNode:
                _viewModel.RenameBook(bookNode.Book);
                e.Handled = true;
                break;
            case Key.Delete when node is BookNode bookNode:
                _viewModel.DeleteBook(bookNode.Book);
                e.Handled = true;
                break;
        }
    }

    private bool OpenNode(TreeNode? node) {
        switch (node) {
            case ChapterNode chapterNode:
                _viewModel.OpenChapter(chapterNode.Chapter);
                return true;
            case EntityNode entityNode:
                _viewModel.OpenEntity(entityNode.Entity);
                return true;
            case StoryBibleNode:
                _viewModel.OpenStoryBible();
                return true;
            default:
                return false;
        }
    }

    /// <summary>Right-click selects the node under the mouse, like Visual Studio.</summary>
    private void OnTreePreviewRightButtonDown(object sender, MouseButtonEventArgs e) {
        TreeViewItem? item = FindAncestor<TreeViewItem>(e.OriginalSource as DependencyObject);
        if (item is not null) {
            item.IsSelected = true;
            item.Focus();
        }
    }

    private void OnTreeContextMenuOpening(object sender, ContextMenuEventArgs e) {
        ContextMenu menu = SolutionTree.ContextMenu;
        menu.Items.Clear();
        NovelProject? project = _viewModel.Project;
        if (project is null) {
            e.Handled = true;
            return;
        }
        switch (SelectedNode) {
            case ProjectNode:
                MenuItem add = new() { Header = "Add" };
                add.Items.Add(CreateItem("New Item...", () => _viewModel.AddNewItem(null, chapter: false), "\uE710", "Ctrl+Shift+A"));
                add.Items.Add(new Separator());
                if (project.IsSeries) {
                    add.Items.Add(CreateItem("Book...", () => _viewModel.AddBook(), Infrastructure.Visuals.BookGlyph, brush: Infrastructure.Visuals.BookBrush));
                }
                add.Items.Add(CreateItem("Chapter...", () => _viewModel.AddNewItem(null, chapter: true), "\uE8A5"));
                foreach (EntityKindInfo info in EntityKindInfo.All) {
                    add.Items.Add(CreateItem($"{info.DisplayName}...", () => _viewModel.AddNewItem(info.Kind, chapter: false), info.Glyph, brush: Infrastructure.Visuals.GetKindBrush(info.Kind)));
                }
                menu.Items.Add(add);
                menu.Items.Add(CreateItem(project.IsSeries ? "Series Bible" : "Story Bible", _viewModel.OpenStoryBible, Infrastructure.Visuals.BibleGlyph));
                menu.Items.Add(CreateItem("Import...", () => _viewModel.ImportCommand.Execute(null), "\uE8B5", "Ctrl+Shift+I"));
                menu.Items.Add(CreateItem("Analyze Manuscript", () => _viewModel.AnalyzeAllCommand.Execute(null), "\uE768", "F6"));
                if (!project.IsSeries) {
                    menu.Items.Add(CreateItem("Convert to Series...", () => _viewModel.ConvertToSeriesCommand.Execute(null), Infrastructure.Visuals.SeriesGlyph));
                }
                menu.Items.Add(new Separator());
                menu.Items.Add(CreateItem("Open Folder in File Explorer", () => OpenFolder(project.DirectoryPath), "\uE838"));
                menu.Items.Add(CreateItem("Properties...", () => _viewModel.OptionsCommand.Execute(null), "\uE713"));
                break;
            case FolderNode { Kind: null }:
                menu.Items.Add(CreateItem("Add Chapter...", () => _viewModel.AddNewItem(null, chapter: true), "\uE710"));
                break;
            case StoryBibleNode:
                menu.Items.Add(CreateItem("Open", _viewModel.OpenStoryBible, Infrastructure.Visuals.BibleGlyph, "Enter", bold: true));
                break;
            case BookNode { Book: Book book }:
                menu.Items.Add(CreateItem("Add Chapter...", () => _viewModel.AddNewItem(null, chapter: true), "\uE710"));
                menu.Items.Add(new Separator());
                menu.Items.Add(CreateItem("Move Up", () => _viewModel.MoveBook(book, -1), "\uE74A"));
                menu.Items.Add(CreateItem("Move Down", () => _viewModel.MoveBook(book, 1), "\uE74B"));
                menu.Items.Add(new Separator());
                menu.Items.Add(CreateItem("Rename...", () => _viewModel.RenameBook(book), "\uE8AC", "F2"));
                menu.Items.Add(CreateItem("Delete", () => _viewModel.DeleteBook(book), "\uE74D", "Del"));
                break;
            case FolderNode { Kind: EntityKind kind }:
                EntityKindInfo kindInfo = EntityKindInfo.Get(kind);
                menu.Items.Add(CreateItem($"Add {kindInfo.DisplayName}...", () => _viewModel.AddNewItem(kind, chapter: false), "\uE710"));
                break;
            case ChapterNode { Chapter: Chapter chapter }:
                menu.Items.Add(CreateItem("Open", () => _viewModel.OpenChapter(chapter), "\uE8A5", "Enter", bold: true));
                menu.Items.Add(CreateItem("Open in Second Pane", () => _viewModel.OpenChapterInSplitPane(chapter), "\uE89F"));
                menu.Items.Add(new Separator());
                if (_viewModel.NextChapterInBook(chapter) is Chapter next) {
                    menu.Items.Add(CreateItem($"Merge with '{next.Title}'", () => _viewModel.MergeWithNext(chapter), "\uE8C8"));
                }
                menu.Items.Add(CreateItem("Move Up", () => _viewModel.MoveChapter(chapter, -1), "\uE74A"));
                menu.Items.Add(CreateItem("Move Down", () => _viewModel.MoveChapter(chapter, 1), "\uE74B"));
                if (project.IsSeries && (project.Books.Count > 1)) {
                    MenuItem moveTo = new() { Header = "Move to Book" };
                    foreach (Book target in project.Books.Where(target => target.Id != chapter.BookId)) {
                        moveTo.Items.Add(CreateItem(target.Title, () => _viewModel.MoveChapterToBook(chapter, target), Infrastructure.Visuals.BookGlyph, brush: Infrastructure.Visuals.BookBrush));
                    }
                    menu.Items.Add(moveTo);
                }
                menu.Items.Add(CreateItem($"Move to {(project.IsSeries ? "Series" : "Story")} Bible", () => _viewModel.MoveChapterToBible(chapter), Infrastructure.Visuals.BibleGlyph));
                menu.Items.Add(new Separator());
                menu.Items.Add(CreateItem("Rename", () => _viewModel.RenameChapter(chapter), "\uE8AC", "F2"));
                menu.Items.Add(CreateItem("Delete", () => _viewModel.DeleteChapter(chapter), "\uE74D", "Del"));
                break;
            case EntityNode { Entity: StoryEntity entity }:
                menu.Items.Add(CreateItem("Open", () => _viewModel.OpenEntity(entity), entity.KindInfo.Glyph, "Enter", bold: true));
                menu.Items.Add(CreateItem("Open in Second Pane", () => _viewModel.OpenEntityInSplitPane(entity), "\uE89F"));
                menu.Items.Add(CreateItem("Find All References", () => _viewModel.FindReferences(entity), "\uE721", "Shift+F12"));
                menu.Items.Add(new Separator());
                menu.Items.Add(CreateItem("Rename...", () => _viewModel.RenameEntity(entity), "\uE8AC", "F2"));
                menu.Items.Add(CreateItem("Delete", () => _viewModel.DeleteEntity(entity), "\uE74D", "Del"));
                break;
            default:
                e.Handled = true;
                break;
        }
    }

    private static MenuItem CreateItem(string header, Action action, string glyph, string? gesture = null, bool bold = false, Brush? brush = null) {
        MenuItem item = new() {
            Header = new TextBlock { Text = header },
            InputGestureText = gesture ?? "",
            FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal,
            Icon = new TextBlock { Text = glyph, FontFamily = new FontFamily("Segoe MDL2 Assets"), Foreground = brush ?? Infrastructure.Visuals.IconBrush, HorizontalAlignment = HorizontalAlignment.Center }
        };
        item.Click += (_, _) => action();
        return item;
    }

    private static void OpenFolder(string path) {
        if (Directory.Exists(path)) {
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = true });
        }
    }

    // ------------------------------------------------------------------
    // Tool windows
    // ------------------------------------------------------------------

    private void OnErrorDoubleClick(object sender, MouseButtonEventArgs e) {
        if (((DataGrid)sender).SelectedItem is ErrorItem item) {
            _viewModel.NavigateToErrorCommand.Execute(item);
        }
    }

    private void OnErrorKeyDown(object sender, KeyEventArgs e) {
        if ((e.Key == Key.Enter) && (((DataGrid)sender).SelectedItem is ErrorItem item)) {
            _viewModel.NavigateToErrorCommand.Execute(item);
            e.Handled = true;
        }
    }

    private void OnGitChangeDoubleClick(object sender, MouseButtonEventArgs e) {
        if (((ListBox)sender).SelectedItem is GitChangeItem item) {
            _viewModel.Git.OpenDiffCommand.Execute(item);
        }
    }

    private void OnGitCommitDoubleClick(object sender, MouseButtonEventArgs e) {
        if (((ListBox)sender).SelectedItem is GitCommitInfo commit) {
            _viewModel.Git.OpenCommitCommand.Execute(commit);
        }
    }

    private void OnGitCommitKeyDown(object sender, KeyEventArgs e) {
        if ((e.Key == Key.Enter) && (((ListBox)sender).SelectedItem is GitCommitInfo commit)) {
            _viewModel.Git.OpenCommitCommand.Execute(commit);
            e.Handled = true;
        }
    }

    private void OnGitStatusClicked(object sender, MouseButtonEventArgs e) {
        _viewModel.SelectedBottomTab = MainViewModel.GitTab;
    }

    /// <summary>Coming back from another app (a terminal, GitHub Desktop) may have changed the repository.</summary>
    protected override void OnActivated(EventArgs e) {
        base.OnActivated(e);
        _viewModel.Git.RequestRefresh();
    }

    private void OnReferenceDoubleClick(object sender, MouseButtonEventArgs e) {
        if (((DataGrid)sender).SelectedItem is ReferenceItem item) {
            _viewModel.NavigateToReferenceCommand.Execute(item);
        }
    }

    private void OnOutputChanged(object sender, TextChangedEventArgs e) {
        OutputBox.ScrollToEnd();
    }

    private void OnShowErrorList(object sender, RoutedEventArgs e) {
        _viewModel.SelectedBottomTab = MainViewModel.ErrorListTab;
    }

    private void OnShowOutput(object sender, RoutedEventArgs e) {
        _viewModel.SelectedBottomTab = MainViewModel.OutputTab;
    }

    private void OnShowFindResults(object sender, RoutedEventArgs e) {
        _viewModel.SelectedBottomTab = MainViewModel.FindResultsTab;
    }

    private void OnMentionClicked(object sender, RoutedEventArgs e) {
        if ((sender as FrameworkElement)?.Tag is StoryEntity entity) {
            _viewModel.FindReferences(entity);
        }
    }

    /// <summary>Middle-click closes a tab.</summary>
    private void OnTabHeaderMouseDown(object sender, MouseButtonEventArgs e) {
        if ((e.ChangedButton == MouseButton.Middle) && ((sender as FrameworkElement)?.DataContext is DocumentViewModel document)) {
            _viewModel.CloseDocument(document);
            e.Handled = true;
        }
    }

    private static T? FindAncestor<T>(DependencyObject? element) where T : DependencyObject {
        while (element is not null and not T) {
            element = element is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(element) : LogicalTreeHelper.GetParent(element);
        }
        return element as T;
    }
}