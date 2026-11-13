using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

using Microsoft.Win32;

using WritingIDE.Core.Ai;
using WritingIDE.Core.Analysis;
using WritingIDE.Core.Import;
using WritingIDE.Core.Model;
using WritingIDE.Infrastructure;
using WritingIDE.Services;

namespace WritingIDE.Views;

/// <summary>Import wizard: builds an AI prompt for the WritingIDE format, then parses and previews the pasted reply.</summary>
public partial class ImportDialog : HandyControl.Controls.Window {
    private static readonly Brush NewBrush = Visuals.CreateBrush("#388A34");
    private static readonly Brush UpdateBrush = Visuals.CreateBrush("#007ACC");
    private static readonly Brush SkipBrush = Visuals.CreateBrush("#9A9A9A");

    /// <summary>A whole series can take dozens of replies; this only guards against an AI that never stops.</summary>
    private const int MaxParts = 60;

    private readonly NovelProject _project;
    private readonly AiService? _ai;
    private readonly ObservableCollection<SourceItem> _sources = [];
    private ImportPromptMode? _lastMode;
    private bool _sending;
    private bool _stopRequested;
    private readonly Dictionary<EntityKind, CheckBox> _kindChecks = new();
    private readonly DispatcherTimer _previewTimer;
    private readonly bool _ready;
    private string _fallbackTitle = "Imported Chapter";

    /// <param name="ai">The configured AI provider, for "Send to AI"; null hides the button.</param>
    public ImportDialog(NovelProject project, AiService? ai = null) {
        InitializeComponent();
        _project = project;
        _ai = ai;
        SourcesList.ItemsSource = _sources;
        SendButton.Visibility = ai is null ? Visibility.Collapsed : Visibility.Visible;
        SendLabel.Text = ai is null ? "Send to AI" : $"Send to {ai.ProviderName}";
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
        : CombineRadio.IsChecked == true ? ImportPromptMode.CombineSeries
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
        bool combine = mode == ImportPromptMode.CombineSeries;
        if (mode != _lastMode) {
            // A combined series keeps planned books as Outline chapters; a single bible's plan goes into its notes.
            OutlineCheck.IsChecked = !combine;
            _lastMode = mode;
        }
        bool fromChat = mode is ImportPromptMode.ConvertBible or ImportPromptMode.ConvertManuscript or ImportPromptMode.CombineSeries;
        ChaptersCheck.Content = mode switch {
            ImportPromptMode.ConvertBible => "Chapter plan",
            ImportPromptMode.ConvertManuscript or ImportPromptMode.CombineSeries => "Chapters (always)",
            _ => "Chapters"
        };
        ChaptersCheck.IsEnabled = mode is not (ImportPromptMode.ConvertManuscript or ImportPromptMode.CombineSeries);
        SourcesPanel.Visibility = combine ? Visibility.Visible : Visibility.Collapsed;
        NotesCheck.Visibility = mode == ImportPromptMode.ConvertBible ? Visibility.Visible : Visibility.Collapsed;
        bool chapters = (ChaptersCheck.IsChecked == true) || (mode is ImportPromptMode.ConvertManuscript or ImportPromptMode.CombineSeries);
        ChapterCountPanel.Visibility = (mode == ImportPromptMode.DevelopMyIdea) && chapters ? Visibility.Visible : Visibility.Collapsed;
        MaterialLabel.Text = combine ? "Or paste material here (optional)" : fromChat ? "Paste it here instead (optional)" : mode == ImportPromptMode.DevelopMyIdea ? "Your story idea" : "Your draft or notes";
        HandyControl.Controls.InfoElement.SetPlaceholder(MaterialBox, fromChat
            ? "Leave empty when the bible or manuscript is already in your AI chat: send this prompt in that same chat."
            : "Paste it here to include it in the prompt, or leave empty and paste it into the AI chat after the prompt.");
        ModeHint.Text = mode switch {
            ImportPromptMode.ConvertBible => "Send the prompt in the chat where the bible was written. Long bibles arrive in parts: import each part, then reply \"continue\".",
            ImportPromptMode.ConvertManuscript => "Send the prompt in the chat where the manuscript was written. Import each part as it arrives, then reply \"continue\" for the next. A .md export of the manuscript can also be opened directly on the next tab, no AI needed.",
            ImportPromptMode.CombineSeries => "A series is long: the AI answers in parts. \"Send to AI\" asks for every part and joins them for you. In a chat AI, import each part, then reply \"continue\". "
                + "If the prompt is too long to paste, use \"Save as .txt\" and attach the file in the chat instead. The import turns this project into a series.",
            _ => ""
        };
        HashSet<EntityKind> kinds = _kindChecks.Where(static pair => pair.Value.IsChecked == true).Select(static pair => pair.Key).ToHashSet();
        bool notes = ((mode == ImportPromptMode.ConvertBible) && (NotesCheck.IsChecked == true)) || combine;
        if (!chapters && (kinds.Count == 0) && !notes) {
            PromptBox.Text = "Select at least one thing to include: chapters, story notes or a kind of story element.";
            return;
        }
        ImportPromptOptions options = new(mode, chapters, kinds, (int)ChapterCountBox.Value, MaterialBox.Text, _project.Entities.ToList()) {
            IsSeries = _project.IsSeries,
            ExistingBooks = _project.Books.Select(static book => book.Title).ToList(),
            IncludeOutlineChapters = chapters,
            IncludeStoryNotes = notes,
            Sources = combine ? _sources.Select(static item => item.Source).ToList() : []
        };
        PromptBox.Text = ImportPrompt.Build(options);
        CopyStatus.Text = "";
        int sourceWords = _sources.Sum(static item => item.Words);
        SourcesTotal.Text = _sources.Count == 0 ? "" : $"{_sources.Count} file{(_sources.Count == 1 ? "" : "s")}  \u00B7  {sourceWords.ToString("N0", CultureInfo.CurrentCulture)} words";
        // Sending from here needs the material in the prompt: the provider has not seen any earlier chat.
        bool hasMaterial = !string.IsNullOrWhiteSpace(MaterialBox.Text) || (combine && (_sources.Count > 0));
        SendButton.IsEnabled = hasMaterial && !_sending;
        SendButton.ToolTip = hasMaterial
            ? "Sends the prompt to the AI provider set in AI Settings, asks for every part, and pastes the joined reply on the next tab"
            : (combine ? "Add your manuscripts and bibles, or paste the material, to send it from here" : "Paste your material to send it from here");
    }

    private void OnAddSources(object sender, RoutedEventArgs e) {
        OpenFileDialog dialog = new() {
            Title = "Add Manuscripts and Bibles",
            Filter = ImportSourceReader.FileFilter,
            Multiselect = true,
            InitialDirectory = _project.DirectoryPath
        };
        if (dialog.ShowDialog(this) != true) {
            return;
        }
        List<string> failed = [];
        foreach (string path in dialog.FileNames) {
            if (_sources.Any(item => string.Equals(item.Path, path, StringComparison.OrdinalIgnoreCase))) {
                continue;
            }
            try {
                ImportSource source = ImportSourceReader.Read(path);
                _sources.Add(new SourceItem(path, source, TextStatistics.CountWords(source.Text)));
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException or DocumentFormat.OpenXml.Packaging.OpenXmlPackageException) {
                failed.Add($"{Path.GetFileName(path)}: {exception.Message}");
            }
        }
        if (failed.Count > 0) {
            HandyControl.Controls.MessageBox.Show(this, "Could not read:\n" + string.Join("\n", failed), "Add Files", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        UpdatePrompt();
    }

    private void OnRemoveSource(object sender, RoutedEventArgs e) {
        if (SourcesList.SelectedItem is SourceItem item) {
            _sources.Remove(item);
            UpdatePrompt();
        }
    }

    /// <summary>
    /// Sends the prompt to the configured AI provider and keeps answering "continue" until the reply is complete,
    /// joining the parts on the next tab. Each "continue" sends only the prompt and the previous part, so a long series
    /// does not grow the request with every part.
    /// </summary>
    private async void OnSendToAi(object sender, RoutedEventArgs e) {
        if ((_ai is null) || _sending) {
            return;
        }
        string prompt = PromptBox.Text;
        _sending = true;
        _stopRequested = false;
        SendButton.IsEnabled = false;
        StopButton.Visibility = Visibility.Visible;
        AiStatus.Visibility = Visibility.Visible;
        List<string> parts = [];
        List<AiMessage> messages = [AiMessage.User(prompt)];
        try {
            for (int part = 1; part <= MaxParts; part++) {
                AiStatus.Text = $"Waiting for {_ai.ProviderName}: part {part}{(parts.Count > 0 ? $" ({CountWords(parts):N0} words so far)" : "")}...";
                string? reply = await _ai.RunAsync("Importing with AI", messages);
                if (reply is null) {
                    AiStatus.Text = parts.Count == 0
                        ? $"Nothing was received from {_ai.ProviderName}. See the Output window for the reason."
                        : $"{_ai.ProviderName} stopped answering after {parts.Count} part(s). Import what arrived, then send again or continue in a chat AI.";
                    break;
                }
                parts.Add(reply);
                InputBox.Text = ImportPrompt.JoinParts(parts);
                if (!ImportPrompt.AsksToContinue(reply)) {
                    AiStatus.Text = $"Done: {parts.Count} part(s), {CountWords(parts):N0} words. Check the preview on the next tab, then import.";
                    break;
                }
                if (_stopRequested || (part == MaxParts)) {
                    AiStatus.Text = $"Stopped after {parts.Count} part(s). Import them now; the reply was not finished.";
                    break;
                }
                messages = [AiMessage.User(prompt), AiMessage.Assistant(reply), AiMessage.User(ImportPrompt.ContinueReply)];
            }
        }
        finally {
            _sending = false;
            StopButton.Visibility = Visibility.Collapsed;
            UpdatePrompt();
        }
        if (parts.Count > 0) {
            Tabs.SelectedIndex = 1;
        }
    }

    private void OnStopAi(object sender, RoutedEventArgs e) {
        _stopRequested = true;
        AiStatus.Text = "Stopping after the part that is on its way...";
    }

    private static int CountWords(IEnumerable<string> parts) {
        return parts.Sum(TextStatistics.CountWords);
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
            // Offer to turn a novel into a series when the text brings books; default on for several books or a series import.
            ConvertCheck.IsChecked = (document.Books.Count > 1) || (document.Novel?.IsSeries == true) || (Mode == ImportPromptMode.CombineSeries);
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

    /// <summary>A manuscript or bible added for a series import.</summary>
    public sealed record SourceItem(string Path, ImportSource Source, int Words) {
        public string WordsText => $"{Words.ToString("N0", CultureInfo.CurrentCulture)} words";
    }
}