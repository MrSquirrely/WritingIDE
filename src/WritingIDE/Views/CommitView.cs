using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;

using ICSharpCode.AvalonEdit;

using WritingIDE.Editor;
using WritingIDE.ViewModels;

namespace WritingIDE.Views;

/// <summary>
/// A commit: its message at the top, the files it changed on the left and every file's word diff on the right.
/// Selecting a file scrolls to its section.
/// </summary>
public sealed class CommitView : UserControl {
    private readonly TextEditor _editor;

    public CommitView(CommitDocumentViewModel model) {
        SetResourceReference(BackgroundProperty, "SurfaceBrush");

        TextBlock subject = new() { Text = model.Commit.Subject, FontSize = 20, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap };
        TextBlock summary = new() { Text = model.Summary, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0) };
        summary.SetResourceReference(TextBlock.ForegroundProperty, "SubtleTextBrush");
        StackPanel heading = new() { Margin = new Thickness(24, 14, 24, 10) };
        heading.Children.Add(subject);
        if (model.Commit.Body.Length > 0) {
            TextBlock body = new() { Text = model.Commit.Body, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0), MaxHeight = 160 };
            body.SetResourceReference(TextBlock.ForegroundProperty, "LabelBrush");
            heading.Children.Add(body);
        }
        heading.Children.Add(summary);
        DockPanel.SetDock(heading, Dock.Top);

        ListBox files = new() { BorderThickness = new Thickness(0), HorizontalContentAlignment = HorizontalAlignment.Stretch };
        AutomationProperties.SetName(files, "Changed files");
        foreach (CommitFileItem item in model.Files) {
            files.Items.Add(CreateRow(item));
        }
        files.SetResourceReference(BackgroundProperty, "SurfaceBrush");
        TextBlock filesHeader = new() { Text = "Changed files", FontWeight = FontWeights.SemiBold, Margin = new Thickness(4, 0, 0, 4) };
        filesHeader.SetResourceReference(TextBlock.ForegroundProperty, "LabelBrush");
        DockPanel.SetDock(filesHeader, Dock.Top);
        DockPanel fileList = new() { Width = 290, Margin = new Thickness(20, 0, 0, 12) };
        fileList.Children.Add(filesHeader);
        fileList.Children.Add(files);
        DockPanel.SetDock(fileList, Dock.Left);

        _editor = new TextEditor {
            IsReadOnly = true,
            WordWrap = true,
            ShowLineNumbers = false,
            FontFamily = new FontFamily("Cambria, Georgia"),
            FontSize = 15,
            Padding = new Thickness(16, 8, 16, 8),
            Margin = new Thickness(12, 0, 0, 0),
            Text = model.Text
        };
        _editor.SetResourceReference(BackgroundProperty, "EditorBackgroundBrush");
        _editor.SetResourceReference(ForegroundProperty, "EditorForegroundBrush");
        _editor.TextArea.TextView.LineTransformers.Add(new DiffColorizer());
        AutomationProperties.SetName(_editor, "Commit changes");

        files.SelectionChanged += (_, _) => {
            if (files.SelectedItem is ListBoxItem { Tag: CommitFileItem item }) {
                ScrollToLine(item.Line);
            }
        };

        DockPanel root = new();
        root.Children.Add(heading);
        root.Children.Add(fileList);
        root.Children.Add(_editor);
        Content = root;
    }

    private static ListBoxItem CreateRow(CommitFileItem item) {
        TextBlock status = new() { Text = item.StatusLetter, Foreground = item.StatusBrush, FontWeight = FontWeights.Bold, Width = 16, ToolTip = item.StatusDescription };
        DockPanel.SetDock(status, Dock.Left);
        TextBlock words = new() { Text = item.WordSummary, Margin = new Thickness(8, 0, 4, 0), ToolTip = "Words added and removed" };
        words.SetResourceReference(TextBlock.ForegroundProperty, "SubtleTextBrush");
        DockPanel.SetDock(words, Dock.Right);
        TextBlock name = new() { Text = item.DisplayName, TextTrimming = TextTrimming.CharacterEllipsis };
        DockPanel row = new() { ToolTip = item.File.Path };
        row.Children.Add(status);
        row.Children.Add(words);
        row.Children.Add(name);
        ListBoxItem container = new() { Content = row, Tag = item };
        AutomationProperties.SetName(container, $"{item.DisplayName}, {item.StatusDescription}");
        return container;
    }

    /// <summary>Puts a file's header at the top of the diff.</summary>
    private void ScrollToLine(int line) {
        if ((line < 1) || (line > _editor.Document.LineCount)) {
            return;
        }
        _editor.TextArea.Caret.Line = line;
        _editor.TextArea.Caret.Column = 1;
        _editor.TextArea.TextView.EnsureVisualLines();
        _editor.ScrollToVerticalOffset(_editor.TextArea.TextView.GetVisualTopByDocumentLine(line));
    }
}