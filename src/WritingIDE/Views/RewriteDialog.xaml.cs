using System.Windows;

namespace WritingIDE.Views;

/// <summary>Shows an AI rewrite next to the original; the writer edits it and decides.</summary>
public partial class RewriteDialog : HandyControl.Controls.Window {
    public RewriteDialog(string original, string suggestion) {
        InitializeComponent();
        OriginalBox.Text = original;
        SuggestionBox.Text = suggestion;
    }

    public string Result => SuggestionBox.Text;

    private void OnReplace(object sender, RoutedEventArgs e) {
        DialogResult = true;
    }

    private void OnCopy(object sender, RoutedEventArgs e) {
        Clipboard.SetText(SuggestionBox.Text);
        WritingIDE.ViewModels.MainViewModel.Notify("Copied the suggestion.");
    }
}