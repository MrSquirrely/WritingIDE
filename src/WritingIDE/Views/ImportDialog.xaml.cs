using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

using Microsoft.Win32;

using WritingIDE.Core.Import;
using WritingIDE.Core.Model;
using WritingIDE.Infrastructure;

namespace WritingIDE.Views;

/// <summary>Import wizard: builds an AI prompt for the WritingIDE format, then parses and previews the pasted reply.</summary>
public partial class ImportDialog : HandyControl.Controls.Window {
    private static readonly Brush NewBrush = Visuals.CreateBrush("#388A34");
    private static readonly Brush UpdateBrush = Visuals.CreateBrush("#007ACC");
    private static readonly Brush SkipBrush = Visuals.CreateBrush("#9A9A9A");

    private readonly NovelProject _project;
    private readonly Dictionary<EntityKind, CheckBox> _kindChecks = new();
    private readonly DispatcherTimer _previewTimer;
    private readonly bool _ready;
    private string _fallbackTitle = "Imported Chapter";

    public ImportDialog(NovelProject project) {
        InitializeComponent();
        _project = project;
        foreach (EntityKindInfo info in EntityKindInfo.All) {
            CheckBox check = new() { Content = info.PluralName, IsChecked = true, Margin = new Thickness(0, 0, 14, 6) };
            check.Checked += OnOptionChanged;
            check.Unchecked += OnOptionChanged;
            _kindChecks[info.Kind] = check;
            IncludePanel.Children.Add(check);
        }
        ChapterCountBox.ValueChanged += (_, _) => UpdatePrompt();
        if (project.IsSeries) {
            TargetBookPanel.Visibility = Visibility.Visible;
            TargetBookBox.ItemsSource = project.Books;
            TargetBookBox.SelectedItem = project.Books.LastOrDefault();
        }
        _previewTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
        _previewTimer.Tick += (_, _) => {
            _previewTimer.Stop();
            UpdatePreview();
        };
        _ready = true;
        UpdatePrompt();
    }

    /// <summary>The parsed import, available once the dialog returns true.</summary>
    public ImportDocument? Document { get; private set; }

    public ImportConflictMode ConflictMode => ConflictBox.SelectedIndex switch {
        1 => ImportConflictMode.Skip,
        2 => ImportConflictMode.AddNew,
        _ => ImportConflictMode.Update
    };

    public ImportOptions Options => new(ConflictMode) {
        DefaultBook = TargetBookBox.SelectedItem as Book,
        ConvertToSeries = (ConvertCheck.Visibility == Visibility.Visible) && (ConvertCheck.IsChecked == true),
        OutlineChaptersToNotes = OutlineCheck.IsChecked == true
    };

    private ImportPromptMode Mode => BibleRadio.IsChecked == true ? ImportPromptMode.ConvertBible
        : ManuscriptRadio.IsChecked == true ? ImportPromptMode.ConvertManuscript
        : DevelopRadio.IsChecked == true ? ImportPromptMode.DevelopMyIdea
        : ImportPromptMode.ConvertMyText;

    // ------------------------------------------------------------------
    // Step 1: the prompt
    // ------------------------------------------------------------------

    private void OnOptionChanged(object sender, RoutedEventArgs e) {
        UpdatePrompt();
    }

    private void UpdatePrompt() {
        if (!_ready) {
            return;
        }
        ImportPromptMode mode = Mode;
        bool fromChat = mode is ImportPromptMode.ConvertBible or ImportPromptMode.ConvertManuscript;
        ChaptersCheck.Content = mode switch {
            ImportPromptMode.ConvertBible => "Chapter plan",
            ImportPromptMode.ConvertManuscript => "Chapters (always)",
            _ => "Chapters"
        };
        ChaptersCheck.IsEnabled = mode != ImportPromptMode.ConvertManuscript;
        NotesCheck.Visibility = mode == ImportPromptMode.ConvertBible ? Visibility.Visible : Visibility.Collapsed;
        bool chapters = (ChaptersCheck.IsChecked == true) || (mode == ImportPromptMode.ConvertManuscript);
        ChapterCountPanel.Visibility = (mode == ImportPromptMode.DevelopMyIdea) && chapters ? Visibility.Visible : Visibility.Collapsed;
        MaterialLabel.Text = fromChat ? "Paste it here instead (optional)" : mode == ImportPromptMode.DevelopMyIdea ? "Your story idea" : "Your draft or notes";
        HandyControl.Controls.InfoElement.SetPlaceholder(MaterialBox, fromChat
            ? "Leave empty when the bible or manuscript is already in your AI chat: send this prompt in that same chat."
            : "Paste it here to include it in the prompt, or leave empty and paste it into the AI chat after the prompt.");
        ModeHint.Text = mode switch {
            ImportPromptMode.ConvertBible => "Send the prompt in the chat where the bible was written. Long bibles arrive in parts: import each part, then reply \"continue\".",
            ImportPromptMode.ConvertManuscript => "Send the prompt in the chat where the manuscript was written. Import each part as it arrives, then reply \"continue\" for the next. A .md export of the manuscript can also be opened directly on the next tab, no AI needed.",
            _ => ""
        };
        HashSet<EntityKind> kinds = _kindChecks.Where(static pair => pair.Value.IsChecked == true).Select(static pair => pair.Key).ToHashSet();
        bool notes = (mode == ImportPromptMode.ConvertBible) && (NotesCheck.IsChecked == true);
        if (!chapters && (kinds.Count == 0) && !notes) {
            PromptBox.Text = "Select at least one thing to include: chapters, story notes or a kind of story element.";
            return;
        }
        ImportPromptOptions options = new(mode, chapters, kinds, (int)ChapterCountBox.Value, MaterialBox.Text, _project.Entities.ToList()) {
            IsSeries = _project.IsSeries,
            ExistingBooks = _project.Books.Select(static book => book.Title).ToList(),
            IncludeOutlineChapters = chapters,
            IncludeStoryNotes = notes
        };
        PromptBox.Text = ImportPrompt.Build(options);
        CopyStatus.Text = "";
    }

    private void OnCopyPrompt(object sender, RoutedEventArgs e) {
        try {
            Clipboard.SetText(PromptBox.Text);
            CopyStatus.Text = "Copied to the clipboard";
        }
        catch (System.Runtime.InteropServices.COMException) {
            CopyStatus.Text = "The clipboard is busy; try again";
        }
    }

    private void OnSavePrompt(object sender, RoutedEventArgs e) {
        SaveFileDialog dialog = new() {
            Title = "Save Prompt",
            FileName = "WritingIDE import prompt.txt",
            InitialDirectory = _project.DirectoryPath,
            Filter = "Text files (*.txt)|*.txt"
        };
        if (dialog.ShowDialog(this) == true) {
            File.WriteAllText(dialog.FileName, PromptBox.Text);
            CopyStatus.Text = "Saved";
        }
    }

    private void OnNextStep(object sender, RoutedEventArgs e) {
        Tabs.SelectedIndex = 1;
        InputBox.Focus();
    }

    // ------------------------------------------------------------------
    // Step 2: paste, preview and import
    // ------------------------------------------------------------------

    private void OnPasteClipboard(object sender, RoutedEventArgs e) {
        if (Clipboard.ContainsText()) {
            InputBox.Text = Clipboard.GetText();
            _fallbackTitle = "Imported Chapter";
        }
    }

    private void OnOpenFile(object sender, RoutedEventArgs e) {
        OpenFileDialog dialog = new() {
            Title = "Import File",
            Filter = "Text and Markdown (*.txt;*.md;*.markdown)|*.txt;*.md;*.markdown|All files (*.*)|*.*"
        };
        if (dialog.ShowDialog(this) != true) {
            return;
        }
        try {
            _fallbackTitle = Path.GetFileNameWithoutExtension(dialog.FileName);
            InputBox.Text = File.ReadAllText(dialog.FileName);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) {
            SummaryText.Text = $"Could not read the file: {exception.Message}";
        }
    }

    private void OnClear(object sender, RoutedEventArgs e) {
        InputBox.Clear();
    }

    private void OnInputChanged(object sender, TextChangedEventArgs e) {
        if (!_ready) {
            return;
        }
        _previewTimer.Stop();
        _previewTimer.Start();
    }

    private void OnConvertChanged(object sender, RoutedEventArgs e) {
        if (_ready && (Document is not null)) {
            UpdatePreview();
        }
    }

    private void OnConflictModeChanged(object sender, SelectionChangedEventArgs e) {
        if (_ready) {
            UpdatePreview();
        }
    }

    private void UpdatePreview() {
        string text = InputBox.Text;
        if (string.IsNullOrWhiteSpace(text)) {
            Document = null;
            PreviewList.ItemsSource = null;
            SummaryText.Text = "Nothing to import yet.";
            WarningsPanel.Visibility = Visibility.Collapsed;
            ImportButton.IsEnabled = false;
            return;
        }

        ImportDocument document = ImportParser.Parse(text, _fallbackTitle);
        if ((Document is null) || (Document.Books.Count != document.Books.Count)) {
            // Offer to turn a novel into a series when the text brings books; default on for several books.
            ConvertCheck.IsChecked = document.Books.Count > 1;
        }
        Document = document;
        ImportConflictMode mode = ConflictMode;
        bool novelWithBooks = !_project.IsSeries && (document.Books.Count > 0);
        ConvertCheck.Visibility = novelWithBooks ? Visibility.Visible : Visibility.Collapsed;
        ConvertCheck.Content = $"Convert this novel into a series ({document.Books.Count} book{(document.Books.Count == 1 ? "" : "s")} in the text)";
        bool useBooks = _project.IsSeries || (novelWithBooks && (ConvertCheck.IsChecked == true));
        int planCount = document.Chapters.Count(ImportApplier.IsPlan);
        OutlineCheck.Visibility = planCount > 0 ? Visibility.Visible : Visibility.Collapsed;
        OutlineCheck.Content = $"Put {planCount} outline chapter{(planCount == 1 ? "" : "s")} in the {(_project.IsSeries ? "Series" : "Story")} Bible";
        bool plansToNotes = OutlineCheck.IsChecked == true;
        List<PreviewItem> items = [];
        if (document.Novel is not null) {
            string notes = document.Novel.Notes.Length > 0 ? $"story notes ({Core.Analysis.TextStatistics.CountWords(document.Novel.Notes):N0} words)" : "";
            items.Add(new PreviewItem(Visuals.BibleGlyph, Visuals.ProjectBrush, document.Novel.IsSeries ? "Series details" : "Novel details",
                string.Join(" \u00B7 ", new[] { document.Novel.Author, document.Novel.Genre, notes }.Where(static part => !string.IsNullOrWhiteSpace(part))),
                mode == ImportConflictMode.Update ? "Update" : "Fill blanks", UpdateBrush));
        }
        foreach (ImportedBook book in document.Books) {
            bool exists = _project.Books.Any(existing => existing.Title.Equals(book.Title, StringComparison.OrdinalIgnoreCase));
            (string action, Brush brush) = useBooks ? (exists ? ("Existing", UpdateBrush) : ("New", NewBrush)) : ("Ignored", SkipBrush);
            int chapterCount = document.Chapters.Count(chapter => book.Title.Equals(chapter.BookTitle, StringComparison.OrdinalIgnoreCase));
            items.Add(new PreviewItem(Visuals.BookGlyph, Visuals.BookBrush, book.Title,
                useBooks ? $"Book \u00B7 {chapterCount} chapter(s)" : "Book \u00B7 a novel has no books; its chapters are added to the manuscript", action, brush));
        }
        foreach (ImportedEntity entity in document.Entities) {
            bool exists = ImportApplier.FindEntity(_project, entity) is not null;
            (string action, Brush brush) = GetAction(exists, mode);
            string detail = entity.Summary.Length > 0 ? entity.Summary : $"{entity.Attributes.Count} attribute(s)";
            items.Add(new PreviewItem(EntityKindInfo.Get(entity.Kind).Glyph, Visuals.GetKindBrush(entity.Kind), entity.Name,
                $"{EntityKindInfo.Get(entity.Kind).DisplayName} \u00B7 {detail}", action, brush));
        }
        foreach (ImportedChapter chapter in document.Chapters) {
            if (plansToNotes && ImportApplier.IsPlan(chapter)) {
                items.Add(new PreviewItem(Visuals.BibleGlyph, Visuals.ProjectBrush, chapter.Title,
                    $"Chapter plan \u00B7 goes into the {(_project.IsSeries ? "Series" : "Story")} Bible notes{(chapter.Synopsis.Length > 0 ? " \u00B7 " + chapter.Synopsis : "")}",
                    "To bible", UpdateBrush));
                continue;
            }
            // Chapter titles repeat across books, so look in the book the chapter will land in.
            Book? targetBook = !useBooks ? null
                : chapter.BookTitle is not null ? _project.Books.FirstOrDefault(book => book.Title.Equals(chapter.BookTitle, StringComparison.OrdinalIgnoreCase))
                : TargetBookBox.SelectedItem as Book;
            bool bookIsNew = useBooks && (chapter.BookTitle is not null) && (targetBook is null);
            bool exists = !bookIsNew && _project.GetChapters(targetBook).Any(existing => existing.Title.Equals(chapter.Title, StringComparison.OrdinalIgnoreCase));
            (string action, Brush brush) = GetAction(exists, mode);
            int words = Core.Analysis.TextStatistics.CountWords(chapter.Text);
            items.Add(new PreviewItem(Visuals.ChapterGlyph, Visuals.ChapterBrush, chapter.Title,
                $"Chapter \u00B7 {words:N0} words{(chapter.Synopsis.Length > 0 ? " \u00B7 " + chapter.Synopsis : "")}", action, brush));
        }
        PreviewList.ItemsSource = items;

        if (document.IsEmpty) {
            SummaryText.Text = "Nothing recognisable was found in the text.";
        }
        else {
            List<string> parts = [];
            if (document.Books.Count > 0) {
                parts.Add($"{document.Books.Count} book(s)");
            }
            if (document.Chapters.Count > 0) {
                parts.Add($"{document.Chapters.Count} chapter(s)");
            }
            foreach (EntityKindInfo info in EntityKindInfo.All) {
                int count = document.Entities.Count(entity => entity.Kind == info.Kind);
                if (count > 0) {
                    parts.Add($"{count} {(count == 1 ? info.DisplayName : info.PluralName).ToLowerInvariant()}");
                }
            }
            if (document.Novel?.Notes.Length > 0) {
                parts.Add("story notes");
            }
            SummaryText.Text = (document.IsPlainText ? "Plain manuscript: " : "Found ") + string.Join(", ", parts) + "."
                + (document.IsPartial ? " This is one part of a longer reply: import it, then reply \"continue\" in the AI chat and import the next part." : "");
        }
        WarningsList.ItemsSource = document.Warnings;
        WarningsPanel.Visibility = document.Warnings.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        ImportButton.IsEnabled = !document.IsEmpty;
    }

    private static (string Action, Brush Brush) GetAction(bool exists, ImportConflictMode mode) {
        if (!exists) {
            return ("New", NewBrush);
        }
        return mode switch {
            ImportConflictMode.Skip => ("Skip", SkipBrush),
            ImportConflictMode.AddNew => ("New copy", NewBrush),
            _ => ("Update", UpdateBrush)
        };
    }

    private void OnImport(object sender, RoutedEventArgs e) {
        _previewTimer.Stop();
        UpdatePreview();
        if ((Document is null) || Document.IsEmpty) {
            return;
        }
        DialogResult = true;
    }

    public sealed record PreviewItem(string Glyph, Brush GlyphBrush, string Name, string Detail, string Action, Brush ActionBrush);
}