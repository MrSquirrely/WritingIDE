using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;

using Microsoft.Win32;

using WritingIDE.Services;

namespace WritingIDE.Views;

/// <summary>Tools > Fonts: pick the editor font and size, and add or remove font files used only by WritingIDE.</summary>
public partial class FontsDialog : HandyControl.Controls.Window {
    private readonly bool _ready;

    public FontsDialog() {
        InitializeComponent();
        SizeBox.Value = FontLibrary.EditorSize;
        LoadFonts(FontLibrary.EditorFamilyName);
        _ready = true;
        UpdatePreview();
    }

    private FontChoice? Selected => FontList.SelectedItem as FontChoice;

    private void LoadFonts(string? select) {
        string filter = SearchBox.Text.Trim();
        List<FontChoice> choices = FontLibrary.Choices
            .Where(choice => (filter.Length == 0) || choice.Name.Contains(filter, StringComparison.OrdinalIgnoreCase))
            .ToList();
        FontList.ItemsSource = choices;
        FontChoice? target = choices.FirstOrDefault(choice => choice.Name.Equals(select, StringComparison.OrdinalIgnoreCase));
        if (target is not null) {
            FontList.SelectedItem = target;
            FontList.ScrollIntoView(target);
        }
    }

    private void OnSearchChanged(object sender, TextChangedEventArgs e) {
        if (_ready) {
            LoadFonts(Selected?.Name);
        }
    }

    private void OnFontSelected(object sender, SelectionChangedEventArgs e) {
        if (_ready) {
            UpdatePreview();
        }
    }

    private void OnSizeChanged(object sender, HandyControl.Data.FunctionEventArgs<double> e) {
        if (_ready) {
            UpdatePreview();
        }
    }

    private void UpdatePreview() {
        FontChoice? choice = Selected;
        RemoveButton.IsEnabled = choice?.IsAdded == true;
        UseButton.IsEnabled = choice is not null;
        FontName.Text = choice?.Name ?? "Choose a font";
        FontDetail.Text = choice?.Detail ?? "";
        if (choice is not null) {
            Preview.FontFamily = choice.Family;
            FontName.FontFamily = choice.Family;
        }
        Preview.FontSize = SizeBox.Value;
    }

    private void OnAddFonts(object sender, RoutedEventArgs e) {
        OpenFileDialog dialog = new() { Title = "Add Fonts to WritingIDE", Filter = FontLibrary.FileFilter, Multiselect = true };
        if (dialog.ShowDialog(this) != true) {
            return;
        }
        try {
            (IReadOnlyList<string> families, IReadOnlyList<string> rejected) = FontLibrary.AddFiles(dialog.FileNames);
            SearchBox.Text = "";
            LoadFonts(families.FirstOrDefault() ?? Selected?.Name);
            if (rejected.Count > 0) {
                HandyControl.Controls.MessageBox.Show(this, "These files are not fonts WritingIDE can read:\n" + string.Join("\n", rejected), "Add Fonts", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) {
            HandyControl.Controls.MessageBox.Show(this, $"Could not add the fonts.\n\n{exception.Message}", "Add Fonts", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void OnRemoveFont(object sender, RoutedEventArgs e) {
        if (Selected is not { IsAdded: true } choice) {
            return;
        }
        string message = $"Remove '{choice.Name}' from WritingIDE?"
            + (choice.Name.Equals(FontLibrary.EditorFamilyName, StringComparison.OrdinalIgnoreCase) ? " The editor goes back to Cambria." : "");
        if (HandyControl.Controls.MessageBox.Show(this, message, "Remove Font", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) {
            return;
        }
        bool wasEditorFont = choice.Name.Equals(FontLibrary.EditorFamilyName, StringComparison.OrdinalIgnoreCase);
        FontLibrary.Remove(choice.Name);
        if (wasEditorFont) {
            FontLibrary.SetEditorFont(FontLibrary.DefaultFamily, FontLibrary.EditorSize);
        }
        LoadFonts(FontLibrary.EditorFamilyName);
        UpdatePreview();
    }

    private void OnOpenFolder(object sender, RoutedEventArgs e) {
        Directory.CreateDirectory(FontLibrary.Folder);
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{FontLibrary.Folder}\"") { UseShellExecute = true });
    }

    private void OnUse(object sender, RoutedEventArgs e) {
        if (Selected is FontChoice choice) {
            FontLibrary.SetEditorFont(choice.Name, SizeBox.Value);
            DialogResult = true;
        }
    }
}