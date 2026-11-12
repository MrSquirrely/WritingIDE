using System.Text.RegularExpressions;
using System.Windows;

using WritingIDE.Core.Model;
using WritingIDE.Core.Search;
using WritingIDE.ViewModels;

namespace WritingIDE.Views;

/// <summary>Find and Replace in Files (Ctrl+Shift+F / Ctrl+Shift+H). Modeless; results go to the Find Results window.</summary>
public partial class FindReplaceDialog : HandyControl.Controls.Window {
    private readonly MainViewModel _shell;

    public FindReplaceDialog(MainViewModel shell) {
        InitializeComponent();
        _shell = shell;
        BookScopeItem.Visibility = shell.Project?.IsSeries ?? false ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>Brings the window forward with the editor's selection as the search text.</summary>
    public void Prepare(string? selection, bool replace) {
        if (!string.IsNullOrEmpty(selection)) {
            FindBox.Text = RegexCheck.IsChecked == true ? Regex.Escape(selection) : selection;
        }
        Activate();
        if (replace && (FindBox.Text.Length > 0)) {
            ReplaceBox.Focus();
            ReplaceBox.SelectAll();
        }
        else {
            FindBox.Focus();
            FindBox.SelectAll();
        }
    }

    private SearchQuery Query => new(FindBox.Text, MatchCaseCheck.IsChecked == true, WholeWordCheck.IsChecked == true, RegexCheck.IsChecked == true);

    private MainViewModel.SearchScope Scope => ScopeBox.SelectedIndex switch {
        1 => MainViewModel.SearchScope.CurrentBook,
        2 => MainViewModel.SearchScope.CurrentChapter,
        _ => MainViewModel.SearchScope.EntireProject
    };

    private void OnInputChanged(object sender, RoutedEventArgs e) {
        if (!IsLoaded) {
            return;
        }
        StatusText.Foreground = (System.Windows.Media.Brush)FindResource("SubtleTextBrush");
        StatusText.Text = (FindBox.Text.Length > 0) && !Query.TryCreateRegex(out _, out string? error) ? error : "";
    }

    private bool TryGetRegex(out Regex regex) {
        if (Query.TryCreateRegex(out Regex? created, out string? error)) {
            regex = created!;
            return true;
        }
        regex = null!;
        StatusText.Foreground = (System.Windows.Media.Brush)FindResource("ErrorBrush");
        StatusText.Text = error;
        return false;
    }

    private void OnFindAll(object sender, RoutedEventArgs e) {
        if (!TryGetRegex(out Regex regex)) {
            return;
        }
        if (_shell.GetChaptersInScope(Scope).Count == 0) {
            StatusText.Text = "Open a chapter to search the current chapter or book.";
            return;
        }
        int count = _shell.FindInFiles(regex, FindBox.Text, Scope);
        StatusText.Text = count == 0 ? "No matches." : $"{count} match(es). See Find Results.";
    }

    private void OnReplaceAll(object sender, RoutedEventArgs e) {
        if (!TryGetRegex(out Regex regex)) {
            return;
        }
        bool useRegex = RegexCheck.IsChecked == true;
        IReadOnlyList<(Chapter Chapter, IReadOnlyList<SearchMatch> Plan)> plans = _shell.PlanReplaceInFiles(regex, ReplaceBox.Text, useRegex, Scope);
        int total = plans.Sum(static entry => entry.Plan.Count);
        if (total == 0) {
            StatusText.Text = "No matches to replace.";
            return;
        }
        string message = $"Replace {total} occurrence(s) of '{FindBox.Text}' with '{ReplaceBox.Text}' in {plans.Count} chapter(s)?\n\n" +
            "Open chapters can be undone with Ctrl+Z. Closed chapters change when you save.";
        if (HandyControl.Controls.MessageBox.Show(this, message, "Replace All", MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK) {
            return;
        }
        int replaced = _shell.ApplyReplaceInFiles(plans, FindBox.Text, ReplaceBox.Text);
        StatusText.Text = $"Replaced {replaced} occurrence(s) in {plans.Count} chapter(s).";
    }

    private void OnClose(object sender, RoutedEventArgs e) {
        Close();
    }
}