using System.Windows;

namespace WritingIDE.Views;

/// <summary>A small prompt with an optional check box, used for Rename.</summary>
public partial class InputDialog : HandyControl.Controls.Window {
    public InputDialog(string title, string prompt, string value, string? optionText) {
        InitializeComponent();
        Title = title;
        PromptText.Text = prompt;
        ValueBox.Text = value;
        if (optionText is null) {
            OptionBox.Visibility = Visibility.Collapsed;
            OptionBox.IsChecked = false;
        }
        else {
            OptionBox.Content = optionText;
        }
        Loaded += (_, _) => {
            ValueBox.Focus();
            ValueBox.SelectAll();
        };
    }

    public string Value => ValueBox.Text;

    public bool IsChecked => OptionBox.IsChecked == true;

    private void OnOk(object sender, RoutedEventArgs e) {
        DialogResult = true;
    }
}