using System.Windows;

using WritingIDE.Core;
using WritingIDE.Core.Grammar;
using WritingIDE.Core.Model;
using WritingIDE.ViewModels;

namespace WritingIDE.Views;

/// <summary>Project properties, grammar rule configuration and the project dictionary.</summary>
public partial class OptionsDialog : HandyControl.Controls.Window {
    private readonly NovelProject _project;

    public OptionsDialog(MainViewModel shell, NovelProject project) {
        InitializeComponent();
        _project = project;
        ProjectPanel.DataContext = project;
        RuleList.ItemsSource = shell.Grammar.Rules.Select(rule => new RuleOption(rule, project)).ToList();
        WordList.ItemsSource = project.CustomDictionary;
    }

    private void OnAddWord(object sender, RoutedEventArgs e) {
        string word = NewWordBox.Text.Trim();
        if ((word.Length > 0) && !_project.CustomDictionary.Contains(word, StringComparer.OrdinalIgnoreCase)) {
            _project.CustomDictionary.Add(word);
        }
        NewWordBox.Clear();
        NewWordBox.Focus();
    }

    private void OnRemoveWord(object sender, RoutedEventArgs e) {
        foreach (string word in WordList.SelectedItems.Cast<string>().ToList()) {
            _project.CustomDictionary.Remove(word);
        }
    }

    public sealed class RuleOption(GrammarRule rule, NovelProject project) : ObservableObject {
        public GrammarRule Rule { get; } = rule;

        public string Summary => $"\u00B7 {Rule.Category} \u00B7 {Rule.Severity}";

        public bool IsEnabled {
            get => !project.DisabledRules.Contains(Rule.Id);
            set {
                if (value) {
                    project.DisabledRules.Remove(Rule.Id);
                }
                else if (!project.DisabledRules.Contains(Rule.Id)) {
                    project.DisabledRules.Add(Rule.Id);
                }
                OnPropertyChanged();
            }
        }
    }
}