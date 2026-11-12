using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

using WritingIDE.Core.Analysis;
using WritingIDE.Core.Model;
using WritingIDE.Infrastructure;
using WritingIDE.ViewModels;

namespace WritingIDE.Views;

/// <summary>
/// The relationship map: story elements as nodes, the relationships you defined as labelled lines and elements that
/// share scenes as dashed lines. Drag nodes to arrange them (positions are saved with the project), double-click to open.
/// </summary>
public sealed class RelationshipMapView : UserControl {
    private static readonly FontFamily IconFont = new("Segoe MDL2 Assets");

    private readonly RelationshipMapDocumentViewModel _model;
    private readonly Canvas _canvas = new() { ClipToBounds = true, MinHeight = 240 };
    private readonly Dictionary<Guid, Border> _nodes = [];
    private readonly List<(GraphEdge Edge, Line Line, Border? Label)> _edges = [];
    private readonly Dictionary<Guid, (double X, double Y)> _positions = [];
    private readonly TextBlock _empty = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(24, 0, 24, 6) };
    private Border? _dragging;
    private Point _dragOffset;
    private bool _moved;

    public RelationshipMapView(RelationshipMapDocumentViewModel model) {
        _model = model;
        DataContext = model;
        SetResourceReference(BackgroundProperty, "SurfaceBrush");
        _canvas.SetResourceReference(Panel.BackgroundProperty, "SurfaceAltBrush");
        _empty.SetResourceReference(TextBlock.ForegroundProperty, "SubtleTextBrush");
        model.Refreshed += Build;

        WrapPanel toolbar = new() { Margin = new Thickness(24, 4, 24, 6) };
        toolbar.Children.Add(Check("Characters", nameof(RelationshipMapDocumentViewModel.IncludeCharacters)));
        toolbar.Children.Add(Check("Factions", nameof(RelationshipMapDocumentViewModel.IncludeFactions)));
        toolbar.Children.Add(Check("Places", nameof(RelationshipMapDocumentViewModel.IncludePlaces)));
        toolbar.Children.Add(Check("Items, events and lore", nameof(RelationshipMapDocumentViewModel.IncludeOthers)));
        TextBlock shared = new() { Text = "Link elements that share at least", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(16, 0, 6, 0) };
        toolbar.Children.Add(shared);
        ComboBox minimum = new() { Width = 64, ItemsSource = model.SharedParagraphChoices };
        minimum.SetBinding(System.Windows.Controls.Primitives.Selector.SelectedItemProperty, new Binding(nameof(RelationshipMapDocumentViewModel.MinimumSharedParagraphs)));
        System.Windows.Automation.AutomationProperties.SetName(minimum, "Minimum shared paragraphs");
        toolbar.Children.Add(minimum);
        toolbar.Children.Add(new TextBlock { Text = "paragraphs (0: only relationships you defined)", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 16, 0) });
        Button rearrange = new() { Content = "Re-arrange", Padding = new Thickness(10, 4, 10, 4), Margin = new Thickness(0), ToolTip = "Forget where you dragged the elements and lay them out again" };
        rearrange.SetResourceReference(StyleProperty, "TileButton");
        rearrange.Click += (_, _) => _model.Rearrange();
        toolbar.Children.Add(rearrange);
        Button refresh = new() { Content = "Refresh", Padding = new Thickness(10, 4, 10, 4), Margin = new Thickness(6, 0, 0, 0) };
        refresh.SetResourceReference(StyleProperty, "TileButton");
        refresh.Click += (_, _) => _model.Refresh();
        toolbar.Children.Add(refresh);

        TextBlock title = new() { Text = "Relationship Map", FontSize = 20, FontWeight = FontWeights.SemiBold, Margin = new Thickness(24, 16, 24, 0) };
        TextBlock hint = new() {
            Text = "Solid lines are relationships you defined on a story element's page; dashed lines join elements that appear in the same paragraphs (thicker: more often). "
                + "Drag to arrange, double-click to open, hover to highlight a node's links.",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(24, 2, 24, 4)
        };
        hint.SetResourceReference(TextBlock.ForegroundProperty, "SubtleTextBrush");

        StackPanel top = new();
        top.Children.Add(title);
        top.Children.Add(hint);
        top.Children.Add(toolbar);
        top.Children.Add(_empty);
        DockPanel.SetDock(top, Dock.Top);

        // The map fills the tab: positions are fractions of its size, so every node stays in view at full size.
        Border frame = new() { Child = _canvas, BorderThickness = new Thickness(1), Margin = new Thickness(16, 0, 16, 16) };
        frame.SetResourceReference(Border.BorderBrushProperty, "DividerBrush");
        DockPanel root = new();
        root.Children.Add(top);
        root.Children.Add(frame);
        Content = root;

        _canvas.MouseMove += OnMouseMove;
        _canvas.MouseLeftButtonUp += OnMouseUp;
        _canvas.SizeChanged += (_, _) => PlaceAll();
    }

    private static CheckBox Check(string text, string property) {
        CheckBox box = new() { Content = text, Margin = new Thickness(0, 0, 14, 0), VerticalAlignment = VerticalAlignment.Center };
        box.SetBinding(System.Windows.Controls.Primitives.ToggleButton.IsCheckedProperty, new Binding(property));
        return box;
    }

    private void Build() {
        _canvas.Children.Clear();
        _nodes.Clear();
        _edges.Clear();
        _positions.Clear();
        foreach ((Guid id, (double X, double Y) position) in _model.Positions) {
            _positions[id] = position;
        }
        _empty.Text = _model.Nodes.Count == 0
            ? "Nothing to show: tick a kind of story element above, or add characters to the project."
            : (_model.Edges.Count == 0 ? "No links yet. Add relationships on a character's page (Relationships), or lower the shared-paragraph threshold." : "");
        _empty.Visibility = _empty.Text.Length == 0 ? Visibility.Collapsed : Visibility.Visible;

        foreach (GraphEdge edge in _model.Edges) {
            Line line = new() { StrokeThickness = edge.IsExplicit ? 2 : Math.Min(5, 0.8 + Math.Log(edge.CoOccurrences, 2)), IsHitTestVisible = false };
            line.SetResourceReference(Shape.StrokeProperty, edge.IsExplicit ? "AccentBrush" : "SubtleTextBrush");
            if (!edge.IsExplicit) {
                line.StrokeDashArray = [4, 3];
                line.Opacity = 0.6;
            }
            Panel.SetZIndex(line, 0);
            _canvas.Children.Add(line);
            Border? label = null;
            if (edge.IsExplicit || (edge.CoOccurrences > 0)) {
                string text = edge.IsExplicit ? string.Join("\n", edge.Labels) : $"{edge.CoOccurrences} shared paragraphs";
                TextBlock block = new() { Text = text, FontSize = 10.5, TextAlignment = TextAlignment.Center };
                block.SetResourceReference(TextBlock.ForegroundProperty, edge.IsExplicit ? "TextBrush" : "SubtleTextBrush");
                label = new Border { Child = block, Padding = new Thickness(4, 1, 4, 1), CornerRadius = new CornerRadius(3), IsHitTestVisible = false, Visibility = edge.IsExplicit ? Visibility.Visible : Visibility.Collapsed };
                label.SetResourceReference(Border.BackgroundProperty, "SurfaceBrush");
                Panel.SetZIndex(label, 1);
                _canvas.Children.Add(label);
            }
            _edges.Add((edge, line, label));
        }

        foreach (StoryEntity entity in _model.Nodes) {
            Border node = CreateNode(entity);
            _nodes[entity.Id] = node;
            _canvas.Children.Add(node);
        }
        PlaceAll();
    }

    private void PlaceAll() {
        foreach (Guid id in _nodes.Keys) {
            PlaceNode(id);
        }
        foreach ((GraphEdge edge, Line _, Border? _) in _edges) {
            UpdateEdge(edge);
        }
    }

    /// <summary>The canvas size (or the layout area before the map is on screen).</summary>
    private Size CanvasSize => (_canvas.ActualWidth > 0) && (_canvas.ActualHeight > 0)
        ? new Size(_canvas.ActualWidth, _canvas.ActualHeight)
        : new Size(RelationshipMapDocumentViewModel.CanvasWidth, RelationshipMapDocumentViewModel.CanvasHeight);

    /// <summary>A stored position (fractions of the map) in canvas pixels.</summary>
    private Point ToPixels(Guid id) {
        (double x, double y) = _positions[id];
        Size size = CanvasSize;
        return new Point(x * size.Width, y * size.Height);
    }

    private Border CreateNode(StoryEntity entity) {
        StackPanel content = new() { Orientation = Orientation.Horizontal };
        content.Children.Add(new TextBlock { Text = entity.KindInfo.Glyph, FontFamily = IconFont, Foreground = Brushes.White, FontSize = 12, Margin = new Thickness(0, 1, 6, 0), VerticalAlignment = VerticalAlignment.Center });
        content.Children.Add(new TextBlock { Text = entity.Name, Foreground = Brushes.White, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center });
        Border node = new() {
            Child = content,
            Padding = new Thickness(12, 5, 12, 6),
            CornerRadius = new CornerRadius(14),
            Background = Visuals.GetKindBrush(entity.Kind),
            Cursor = Cursors.SizeAll,
            Tag = entity,
            ToolTip = BuildToolTip(entity),
            BorderThickness = new Thickness(2),
            BorderBrush = Brushes.Transparent
        };
        System.Windows.Automation.AutomationProperties.SetName(node, entity.Name);
        Panel.SetZIndex(node, 2);
        node.MouseLeftButtonDown += OnNodeMouseDown;
        node.MouseEnter += (_, _) => Highlight(entity.Id);
        node.MouseLeave += (_, _) => Highlight(null);
        return node;
    }

    private string BuildToolTip(StoryEntity entity) {
        List<string> lines = [$"{entity.KindInfo.DisplayName}: {entity.Name}"];
        if (entity.Summary.Length > 0) {
            lines.Add(entity.Summary);
        }
        foreach (GraphEdge edge in _model.Edges.Where(edge => (edge.From == entity) || (edge.To == entity))) {
            StoryEntity other = edge.From == entity ? edge.To : edge.From;
            lines.Add(edge.IsExplicit ? $"\u2022 {string.Join("; ", edge.Labels)}" : $"\u2022 shares {edge.CoOccurrences} paragraphs with {other.Name}");
        }
        lines.Add("Double-click to open, drag to move.");
        return string.Join("\n", lines);
    }

    /// <summary>Dims everything not linked to the hovered node.</summary>
    private void Highlight(Guid? id) {
        foreach ((GraphEdge edge, Line line, Border? label) in _edges) {
            bool connected = (id is null) || (edge.From.Id == id) || (edge.To.Id == id);
            line.Opacity = connected ? (edge.IsExplicit ? 1 : 0.6) : 0.12;
            if (label is not null) {
                label.Visibility = (edge.IsExplicit && connected) || ((id is not null) && connected) ? Visibility.Visible : Visibility.Collapsed;
            }
        }
        foreach ((Guid nodeId, Border node) in _nodes) {
            bool linked = (id is null) || (nodeId == id) || _edges.Any(entry => ((entry.Edge.From.Id == id) && (entry.Edge.To.Id == nodeId)) || ((entry.Edge.To.Id == id) && (entry.Edge.From.Id == nodeId)));
            node.Opacity = linked ? 1 : 0.35;
        }
    }

    private void PlaceNode(Guid id) {
        Border node = _nodes[id];
        Point center = ToPixels(id);
        // Measured directly: the map may be built before it is on screen, when ActualWidth is still 0.
        node.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        Canvas.SetLeft(node, center.X - (node.DesiredSize.Width / 2));
        Canvas.SetTop(node, center.Y - (node.DesiredSize.Height / 2));
    }

    private void UpdateEdge(GraphEdge edge) {
        (GraphEdge _, Line line, Border? label) = _edges.First(entry => entry.Edge == edge);
        (double x1, double y1) = (ToPixels(edge.From.Id).X, ToPixels(edge.From.Id).Y);
        (double x2, double y2) = (ToPixels(edge.To.Id).X, ToPixels(edge.To.Id).Y);
        line.X1 = x1;
        line.Y1 = y1;
        line.X2 = x2;
        line.Y2 = y2;
        if (label is not null) {
            label.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            Canvas.SetLeft(label, ((x1 + x2) / 2) - (label.DesiredSize.Width / 2));
            Canvas.SetTop(label, ((y1 + y2) / 2) - (label.DesiredSize.Height / 2));
        }
    }

    private void OnNodeMouseDown(object sender, MouseButtonEventArgs e) {
        if ((sender is not Border { Tag: StoryEntity entity } node)) {
            return;
        }
        if (e.ClickCount == 2) {
            _dragging = null;
            _model.Shell.OpenEntity(entity);
            e.Handled = true;
            return;
        }
        _dragging = node;
        _moved = false;
        Point point = e.GetPosition(_canvas);
        Point center = ToPixels(entity.Id);
        _dragOffset = new Point(point.X - center.X, point.Y - center.Y);
        _canvas.CaptureMouse();
        e.Handled = true;
    }

    private void OnMouseMove(object sender, MouseEventArgs e) {
        if ((_dragging?.Tag is not StoryEntity entity) || (e.LeftButton != MouseButtonState.Pressed)) {
            return;
        }
        Point point = e.GetPosition(_canvas);
        Size size = CanvasSize;
        double x = Math.Clamp(point.X - _dragOffset.X, 40, Math.Max(41, size.Width - 40));
        double y = Math.Clamp(point.Y - _dragOffset.Y, 16, Math.Max(17, size.Height - 16));
        _positions[entity.Id] = (x / size.Width, y / size.Height);
        _moved = true;
        PlaceNode(entity.Id);
        foreach ((GraphEdge edge, Line _, Border? _) in _edges.Where(entry => (entry.Edge.From == entity) || (entry.Edge.To == entity)).ToList()) {
            UpdateEdge(edge);
        }
    }

    private void OnMouseUp(object sender, MouseButtonEventArgs e) {
        if (_dragging?.Tag is StoryEntity entity && _moved) {
            // Saved with the project, so the map keeps your arrangement.
            (double x, double y) = _positions[entity.Id];
            entity.MapX = Math.Round(x, 4);
            entity.MapY = Math.Round(y, 4);
        }
        _dragging = null;
        _canvas.ReleaseMouseCapture();
    }
}