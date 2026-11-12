using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

using WritingIDE.Core.Model;
using WritingIDE.ViewModels;

namespace WritingIDE.Views;

public partial class MainWindow : HandyControl.Controls.Window {
    private readonly MainViewModel _viewModel;

    public MainWindow(MainViewModel viewModel) {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;
    }

    /// <summary>The editor consumes Ctrl+I (it is the Tab character), so the Import shortcut is handled before it gets there.</summary>
    protected override void OnPreviewKeyDown(KeyEventArgs e) {
        if ((e.Key == Key.I) && (Keyboard.Modifiers == ModifierKeys.Control) && _viewModel.ImportCommand.CanExecute(null)) {
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
                menu.Items.Add(CreateItem("Import...", () => _viewModel.ImportCommand.Execute(null), "\uE8B5", "Ctrl+I"));
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
                menu.Items.Add(new Separator());
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