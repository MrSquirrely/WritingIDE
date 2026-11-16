using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

using WritingIDE.Core.Export;
using WritingIDE.Core.Model;
using WritingIDE.Sdk;
using WritingIDE.Services;
using WritingIDE.ViewModels;

namespace WritingIDE.Views;

/// <summary>File > Export: DOCX, EPUB, PDF, Google Docs, Markdown, AI context, and formats added by extensions.</summary>
public partial class ExportDialog : HandyControl.Controls.Window {
    private readonly MainViewModel _shell;

    public sealed record ScopeChoice(string Label, IReadOnlyList<Chapter> Chapters);

    public sealed record FormatChoice(string Glyph, string Title, string Description, string Help, ExportFormat Format, IManuscriptExporter? Extension = null) {
        /// <summary>What screen readers announce for the list item.</summary>
        public override string ToString() {
            return $"{Title}: {Description}";
        }
    }

    public ExportDialog(MainViewModel shell, NovelProject project) {
        InitializeComponent();
        _shell = shell;
        List<FormatChoice> formats = [
            new("\uE8A5", "Word (.docx)", "For agents, editors and Word.",
                "A Word document with each chapter on a new page and chapters as Heading 1 (Word's navigation pane lists them).", ExportFormat.Docx),
            new("\uE8F1", "E-book (.epub)", "For Kindle, Apple Books, Kobo and e-readers.",
                "An EPUB 3 e-book with a table of contents. Send it to Kindle, open it in Apple Books, or check it with an e-book previewer.", ExportFormat.Epub),
            new("\uEA90", "PDF", "To print or share.",
                "A PDF laid out as a manuscript (US Letter) or a 6 x 9 in book.", ExportFormat.Pdf),
            new("\uE753", "Google Docs", "Opens Google Drive to upload.",
                "Exports a .docx that converts cleanly in Google Docs (chapters become its outline). WritingIDE then shows the file and opens Google Drive: choose New > File upload (or drag the file in), then open it with Google Docs.", ExportFormat.GoogleDocs),
            new("\uE943", "Markdown", "Plain text with # headings.",
                "Markdown with # book and ## chapter headings; the Import dialog reads it back.", ExportFormat.Markdown),
            new("\uE99A", "For an AI chat", "Everything an AI needs about your book.",
                "One Markdown file with your story bible, every story element (attributes and relationships), plot threads, chapter synopses, story dates and POV, and optionally the full text. It is copied to the clipboard too: paste it into ChatGPT, Claude or Gemini, or attach the file.", ExportFormat.AiContext)
        ];
        foreach (IManuscriptExporter extension in shell.Extensions.Exporters) {
            formats.Add(new FormatChoice("\uEA86", extension.Name, $"From an extension ({extension.FileExtension})", $"Exported by an extension as {extension.FileExtension}.", ExportFormat.Markdown, extension));
        }
        FormatList.ItemsSource = formats;

        List<ScopeChoice> scopes = [new("The whole " + (project.IsSeries ? "series" : "manuscript"), shell.OrderedChapters.ToList())];
        if (project.IsSeries) {
            foreach (Book book in project.Books) {
                scopes.Add(new($"Book: {book.Title}", project.GetChapters(book).ToList()));
            }
        }
        if (shell.ActiveChapter is not null) {
            scopes.Add(new($"Current chapter: {shell.ActiveChapter.Chapter.Title}", [shell.ActiveChapter.Chapter]));
        }
        ScopeBox.ItemsSource = scopes;
        ScopeBox.DisplayMemberPath = nameof(ScopeChoice.Label);
        ScopeBox.SelectedIndex = 0;
        ContactBox.Text = project.Author;
        List<FontOption> fonts = [new("Default for the layout (Times New Roman or Georgia)", null, new FontFamily("Segoe UI"))];
        fonts.AddRange(FontLibrary.Choices.Select(static choice => new FontOption(choice.IsAdded ? $"{choice.Name}  (added)" : choice.Name, choice.Name, choice.Family)));
        FontBox.ItemsSource = fonts;
        FontBox.SelectedIndex = 0;
        FormatList.SelectedIndex = 0;
    }

    /// <summary>A font in the export picker; <see cref="Family"/> null means the layout's own font.</summary>
    public sealed record FontOption(string Label, string? Family, FontFamily Display) {
        public override string ToString() {
            return Label;
        }
    }

    private FormatChoice? Selected => FormatList.SelectedItem as FormatChoice;

    private void OnFormatChanged(object sender, SelectionChangedEventArgs e) {
        if (Selected is not FormatChoice choice) {
            return;
        }
        FormatTitle.Text = choice.Title;
        FormatHelp.Text = choice.Help;
        bool book = choice.Extension is null && (choice.Format is ExportFormat.Docx or ExportFormat.Pdf);
        StylePanel.Visibility = book ? Visibility.Visible : Visibility.Collapsed;
        TitlePanel.Visibility = choice.Extension is null && (choice.Format is ExportFormat.Docx or ExportFormat.Pdf or ExportFormat.Epub or ExportFormat.GoogleDocs) ? Visibility.Visible : Visibility.Collapsed;
        AiPanel.Visibility = (choice.Extension is null) && (choice.Format == ExportFormat.AiContext) ? Visibility.Visible : Visibility.Collapsed;
        FontPanel.Visibility = (choice.Extension is null) && (choice.Format is ExportFormat.Docx or ExportFormat.Pdf or ExportFormat.Epub or ExportFormat.GoogleDocs) ? Visibility.Visible : Visibility.Collapsed;
        FontHelp.Text = choice.Format switch {
            ExportFormat.Pdf => "The PDF embeds the font, so it looks the same everywhere (unless the font's licence forbids embedding).",
            ExportFormat.Epub => "The e-book embeds the font when its licence allows; otherwise readers use their own font.",
            _ => "Word names the font; it shows on PCs where that font is installed."
        };
        ScopeBox.IsEnabled = choice.Extension is null;
    }

    private void OnExport(object sender, RoutedEventArgs e) {
        if ((Selected is not FormatChoice choice) || (ScopeBox.SelectedItem is not ScopeChoice scope)) {
            return;
        }
        bool manuscript = ManuscriptStyle.IsChecked == true;
        ExportOptions options = new() {
            Format = choice.Format,
            Style = manuscript ? ExportStyle.Manuscript : ExportStyle.Book,
            Chapters = scope.Chapters,
            IncludeTitlePage = TitlePageBox.IsChecked == true,
            SceneBreak = manuscript && (choice.Format is ExportFormat.Docx or ExportFormat.Pdf) ? "#" : "* * *",
            Contact = ContactBox.Text,
            IncludeFullText = FullTextBox.IsChecked == true,
            AiInstructions = InstructionsBox.Text,
            FontFamily = (FontBox.SelectedItem as FontOption)?.Family
        };
        if (_shell.Export(options, choice.Extension) is not null) {
            DialogResult = true;
        }
    }
}