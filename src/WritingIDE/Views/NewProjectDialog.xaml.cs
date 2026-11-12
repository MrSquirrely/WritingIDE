using System.Windows;
using System.Windows.Controls;

using Microsoft.Win32;

using WritingIDE.Core.Persistence;

namespace WritingIDE.Views;

public partial class NewProjectDialog : HandyControl.Controls.Window {
    public NewProjectDialog() {
        InitializeComponent();
        TemplateList.ItemsSource = ProjectTemplate.All;
        TemplateList.SelectedIndex = 0;
        LocationBox.Text = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "WritingIDE Projects");
        NameBox.Text = CreateUniqueName(LocationBox.Text);
        AuthorBox.Text = Environment.UserName;
        Loaded += (_, _) => {
            NameBox.Focus();
            NameBox.SelectAll();
        };
    }

    public string ProjectName => NameBox.Text.Trim();

    public string ProjectLocation => LocationBox.Text.Trim();

    public string Author => AuthorBox.Text.Trim();

    public ProjectTemplate SelectedTemplate => (ProjectTemplate?)TemplateList.SelectedItem ?? ProjectTemplate.All[0];

    private void OnTemplateChanged(object sender, SelectionChangedEventArgs e) {
        TemplateTitle.Text = SelectedTemplate.Title;
        TemplateDescription.Text = SelectedTemplate.Description;
        if ((SelectedTemplate.Title == "Sample Novel") && NameBox.Text.StartsWith("MyNovel", StringComparison.Ordinal)) {
            NameBox.Text = CreateUniqueName(LocationBox.Text, "The Lighthouse Keeper");
        }
    }

    private void OnBrowse(object sender, RoutedEventArgs e) {
        OpenFolderDialog dialog = new() { Title = "Project Location", InitialDirectory = Directory.Exists(LocationBox.Text) ? LocationBox.Text : "" };
        if (dialog.ShowDialog(this) == true) {
            LocationBox.Text = dialog.FolderName;
        }
    }

    private void OnCreate(object sender, RoutedEventArgs e) {
        string? error = Validate();
        if (error is not null) {
            ErrorText.Text = error;
            return;
        }
        DialogResult = true;
    }

    private string? Validate() {
        if (ProjectName.Length == 0) {
            return "Enter a project name.";
        }
        if (ProjectName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) {
            return "The project name contains characters that are not allowed in a folder name.";
        }
        if (ProjectLocation.Length == 0) {
            return "Choose a location.";
        }
        if (Directory.Exists(Path.Combine(ProjectLocation, ProjectName))) {
            return $"A folder named '{ProjectName}' already exists in that location.";
        }
        return null;
    }

    private static string CreateUniqueName(string location, string baseName = "MyNovel") {
        string name = baseName;
        int counter = 2;
        while (Directory.Exists(Path.Combine(location, name))) {
            name = $"{baseName}{counter++}";
        }
        return name;
    }
}