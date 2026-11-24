using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;

using WritingIDE.Core.Model;
using WritingIDE.ViewModels;

namespace WritingIDE.Views;

/// <summary>
/// Solution Explorer: drag a chapter onto another chapter to put it above or below that one, or onto a book (or the
/// Manuscript folder) to move it to the end of it. A line shows where the chapter lands.
/// </summary>
public partial class MainWindow {
    private const string ChapterDragFormat = "WritingIDE.SolutionExplorerChapter";
    private const double ScrollEdge = 22;

    private Point? _treeDragStart;
    private Chapter? _treeDragChapter;
    private DropLineAdorner? _dropLine;
    private ScrollViewer? _treeScroller;

    private void OnTreePreviewLeftButtonDown(object sender, MouseButtonEventArgs e) {
        TreeViewItem? item = FindAncestor<TreeViewItem>(e.OriginalSource as DependencyObject);
        _treeDragChapter = (item?.DataContext as ChapterNode)?.Chapter;
        _treeDragStart = _treeDragChapter is null ? null : e.GetPosition(SolutionTree);
    }

    private void OnTreePreviewMouseMove(object sender, MouseEventArgs e) {
        if ((_treeDragStart is not Point start) || (_treeDragChapter is not Chapter chapter)) {
            return;
        }
        if (e.LeftButton != MouseButtonState.Pressed) {
            _treeDragStart = null;
            _treeDragChapter = null;
            return;
        }
        Vector moved = e.GetPosition(SolutionTree) - start;
        if ((Math.Abs(moved.X) <= SystemParameters.MinimumHorizontalDragDistance) && (Math.Abs(moved.Y) <= SystemParameters.MinimumVerticalDragDistance)) {
            return;
        }
        _treeDragStart = null;
        _treeDragChapter = null;
        DragDrop.DoDragDrop(SolutionTree, new DataObject(ChapterDragFormat, chapter), DragDropEffects.Move);
        ClearDropLine();
    }

    private void OnTreeDragOver(object sender, DragEventArgs e) {
        e.Handled = true;
        ScrollTreeNearEdge(e.GetPosition(SolutionTree).Y);
        if (FindChapterDrop(e) is not (TreeViewItem item, bool below, _)) {
            e.Effects = DragDropEffects.None;
            ClearDropLine();
            return;
        }
        e.Effects = DragDropEffects.Move;
        if ((_dropLine is null) || !ReferenceEquals(_dropLine.AdornedElement, item) || (_dropLine.Below != below)) {
            ClearDropLine();
            if (AdornerLayer.GetAdornerLayer(item) is AdornerLayer layer) {
                _dropLine = new DropLineAdorner(item, below, (TryFindResource("AccentBrush") as Brush) ?? SystemColors.HighlightBrush);
                layer.Add(_dropLine);
            }
        }
    }

    private void OnTreeDragLeave(object sender, DragEventArgs e) {
        ClearDropLine();
    }

    private void OnTreeDrop(object sender, DragEventArgs e) {
        e.Handled = true;
        ClearDropLine();
        if (FindChapterDrop(e) is (_, _, Action move)) {
            move();
        }
    }

    /// <summary>Where a dragged chapter would land: the row under the mouse, whether the line goes below it, and the move itself.</summary>
    private (TreeViewItem Item, bool Below, Action Move)? FindChapterDrop(DragEventArgs e) {
        if (e.Data.GetData(ChapterDragFormat) is not Chapter dragged) {
            return null;
        }
        TreeViewItem? item = FindAncestor<TreeViewItem>(e.OriginalSource as DependencyObject);
        switch (item?.DataContext) {
            case ChapterNode { Chapter: Chapter target } when !ReferenceEquals(target, dragged):
                bool after = e.GetPosition(item).Y > (item.ActualHeight / 2);
                return (item, after, () => _viewModel.MoveChapterNextTo(dragged, target, after));
            case BookNode { Book: Book book }:
                return (item, true, () => _viewModel.MoveChapterToEnd(dragged, book));
            case FolderNode { Kind: null }:
                return (item, true, () => _viewModel.MoveChapterToEnd(dragged, null));
            default:
                return null;
        }
    }

    /// <summary>Dragging near the top or bottom of a long chapter list scrolls it.</summary>
    private void ScrollTreeNearEdge(double y) {
        _treeScroller ??= FindDescendant<ScrollViewer>(SolutionTree);
        if (_treeScroller is null) {
            return;
        }
        if (y < ScrollEdge) {
            _treeScroller.LineUp();
        }
        else if (y > (SolutionTree.ActualHeight - ScrollEdge)) {
            _treeScroller.LineDown();
        }
    }

    private void ClearDropLine() {
        if (_dropLine is not null) {
            AdornerLayer.GetAdornerLayer(_dropLine.AdornedElement)?.Remove(_dropLine);
            _dropLine = null;
        }
    }

    private static T? FindDescendant<T>(DependencyObject element) where T : DependencyObject {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(element); i++) {
            DependencyObject child = VisualTreeHelper.GetChild(element, i);
            if ((child as T ?? FindDescendant<T>(child)) is T found) {
                return found;
            }
        }
        return null;
    }

    /// <summary>The line that shows where a dragged chapter lands: along the top or the bottom of a row.</summary>
    private sealed class DropLineAdorner : Adorner {
        private readonly Pen _pen;

        public DropLineAdorner(UIElement adorned, bool below, Brush brush) : base(adorned) {
            Below = below;
            IsHitTestVisible = false;
            _pen = new Pen(brush, 2);
            _pen.Freeze();
        }

        public bool Below { get; }

        protected override void OnRender(DrawingContext drawingContext) {
            double y = Below ? AdornedElement.RenderSize.Height : 0;
            drawingContext.DrawLine(_pen, new Point(0, y), new Point(AdornedElement.RenderSize.Width, y));
        }
    }
}