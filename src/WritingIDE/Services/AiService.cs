using System.Net.Http;
using System.Windows;
using System.Windows.Input;

using WritingIDE.Core;
using WritingIDE.Core.Ai;
using WritingIDE.Core.Model;
using WritingIDE.Infrastructure;
using WritingIDE.Plugins;
using WritingIDE.ViewModels;
using WritingIDE.Views;

using HcMessageBox = HandyControl.Controls.MessageBox;

namespace WritingIDE.Services;

/// <summary>
/// WritingIDE's AI tools: summarize a chapter, beta-read it, check continuity, rewrite a selection and chat about the
/// book. Works with free providers (Gemini, Groq, OpenRouter's free models) and with Ollama on this PC. Before
/// manuscript text goes to an online provider for the first time, the writer is asked.
/// </summary>
public sealed class AiService : ObservableObject {
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(3) };
    private readonly MainViewModel _shell;

    public AiService(MainViewModel shell) {
        _shell = shell;
        SettingsCommand = new RelayCommand(ShowSettings);
        SummarizeCommand = new RelayCommand(() => _ = SummarizeAsync(), () => CanRunOnChapter);
        BetaReadCommand = new RelayCommand(() => _ = AnnotateAsync(critique: true), () => CanRunOnChapter);
        ContinuityCommand = new RelayCommand(() => _ = AnnotateAsync(critique: false), () => CanRunOnChapter);
        ChatCommand = new RelayCommand(OpenChat, () => _shell.HasProject);
    }

    private AiSettings Settings => _shell.Settings.Ai;

    public bool IsBusy {
        get;
        private set {
            if (SetProperty(ref field, value)) {
                CommandManager.InvalidateRequerySuggested();
            }
        }
    }

    public string ProviderName => AiProviderInfo.Get(Settings.Provider).DisplayName;

    public ICommand SettingsCommand { get; }

    public ICommand SummarizeCommand { get; }

    public ICommand BetaReadCommand { get; }

    public ICommand ContinuityCommand { get; }

    public ICommand ChatCommand { get; }

    private bool CanRunOnChapter => !IsBusy && (_shell.ActiveChapter is not null);

    public AiConnection Connection {
        get {
            AiProviderSettings provider = Settings.For(Settings.Provider);
            return new AiConnection(Settings.Provider, provider.Endpoint, provider.Model, provider.GetKey());
        }
    }

    public IAiClient CreateClient(AiConnection? connection = null) {
        return AiClientFactory.Create(connection ?? Connection, Http);
    }

    public void ShowSettings() {
        AiSettingsDialog dialog = new(_shell) { Owner = Application.Current.MainWindow };
        dialog.ShowDialog();
        OnPropertyChanged(nameof(ProviderName));
    }

    /// <summary>Runs a request with a busy indicator; asks for consent before text leaves the PC; shows errors kindly.</summary>
    public async Task<string?> RunAsync(string what, IReadOnlyList<AiMessage> messages) {
        if (IsBusy) {
            MainViewModel.Notify("The AI is still working on the previous request.", MainViewModel.NotificationKind.Warning);
            return null;
        }
        AiProviderInfo info = AiProviderInfo.Get(Settings.Provider);
        if (info.NeedsKey && string.IsNullOrWhiteSpace(Settings.For(Settings.Provider).GetKey())) {
            MainViewModel.Notify($"Set up an AI provider first (AI > AI Settings). {info.DisplayName} needs a free API key.", MainViewModel.NotificationKind.Warning);
            ShowSettings();
            return null;
        }
        if ((Settings.Provider != AiProviderKind.Ollama) && !Settings.Consented.Contains(Settings.Provider)) {
            MessageBoxResult answer = HcMessageBox.Show(Application.Current.MainWindow!,
                $"{what} sends text from your manuscript (and story elements) to {info.DisplayName} over the internet. Free tiers may use what you send to improve their models; check the provider's terms.\n\nContinue, and don't ask again for {info.DisplayName}?\n(Ollama keeps everything on this PC.)",
                "Send text to an AI provider", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (answer != MessageBoxResult.Yes) {
                return null;
            }
            Settings.Consented.Add(Settings.Provider);
            _shell.Settings.Save();
        }
        IsBusy = true;
        _shell.StatusText = $"{what}: waiting for {info.DisplayName}...";
        try {
            string reply = await CreateClient().CompleteAsync(messages);
            _shell.StatusText = $"{what}: done";
            return reply;
        }
        catch (AiException exception) {
            _shell.StatusText = $"{what}: failed";
            _shell.Log($"AI ({info.DisplayName}): {exception.Message}");
            MainViewModel.Notify(exception.Message, MainViewModel.NotificationKind.Error);
            return null;
        }
        finally {
            IsBusy = false;
        }
    }

    /// <summary>AI > Summarize Chapter: writes the chapter's synopsis.</summary>
    public async Task SummarizeAsync() {
        ChapterDocumentViewModel? document = _shell.ActiveChapter;
        if (document is null) {
            return;
        }
        string? reply = await RunAsync("Summarize chapter", AiTools.Summarize(document.Chapter.Title, document.Document.Text));
        if (reply is null) {
            return;
        }
        string synopsis = reply.Trim();
        if ((document.Chapter.Synopsis.Trim().Length > 0) && (HcMessageBox.Show(Application.Current.MainWindow!,
                $"Replace the current synopsis of '{document.Chapter.Title}'?\n\nCurrent:\n{document.Chapter.Synopsis}\n\nAI suggestion:\n{synopsis}",
                "Summarize Chapter", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)) {
            return;
        }
        document.Chapter.Synopsis = synopsis;
        MainViewModel.Notify("Synopsis updated (see the chapter's Properties).", MainViewModel.NotificationKind.Success);
    }

    /// <summary>AI > Beta Read Chapter / Check Continuity with AI: the AI's notes become margin comments.</summary>
    public async Task AnnotateAsync(bool critique) {
        ChapterDocumentViewModel? document = _shell.ActiveChapter;
        if (document is null) {
            return;
        }
        string text = document.Document.Text;
        string context = _shell.BuildAiContext(fullText: false);
        IReadOnlyList<AiMessage> messages = critique
            ? AiTools.Critique(document.Chapter.Title, text, context)
            : AiTools.ContinuityCheck(document.Chapter.Title, text, context);
        string? reply = await RunAsync(critique ? "Beta read" : "Continuity check", messages);
        if (reply is null) {
            return;
        }
        IReadOnlyList<AiNote> notes = AiTools.ParseNotes(reply);
        if (notes.Count == 0) {
            MainViewModel.Notify(critique ? "The AI returned no notes." : "The AI found no continuity problems.", MainViewModel.NotificationKind.Success);
            _shell.Log($"AI reply: {reply}");
            return;
        }
        // The text may have changed while the AI was thinking: anchor against the current text.
        string current = document.Document.Text;
        string author = $"AI ({AiProviderInfo.Get(Settings.Provider).DisplayName})";
        foreach (AnchoredNote note in AiTools.Anchor(current, notes)) {
            document.Chapter.Comments.Add(new ChapterComment {
                Offset = note.Offset,
                Length = note.Length,
                AnchorText = current.Substring(note.Offset, note.Length),
                Text = (critique ? "" : "Continuity: ") + note.Note.Comment + (note.Length == 0 && note.Note.Quote.Length > 0 ? $" (on \u201C{note.Note.Quote}\u201D)" : ""),
                Author = author,
                CreatedUtc = DateTime.UtcNow
            });
        }
        if (!_shell.ShowCommentMargin) {
            _shell.ShowCommentMargin = true;
        }
        MainViewModel.Notify($"Added {notes.Count} AI note(s) as comments in the margin. Resolve or delete them as you go.", MainViewModel.NotificationKind.Success);
    }

    /// <summary>Rewrites a selection and shows the suggestion; the writer chooses whether to replace it.</summary>
    public async Task RewriteAsync(ChapterDocumentViewModel document, int start, int length, RewriteKind kind) {
        string instruction = "";
        if (kind == RewriteKind.Custom) {
            InputDialog prompt = new("Rewrite with AI", "How should the passage change? (e.g. \"from Wren's point of view\", \"more menace\", \"in present tense\")", "", null) { Owner = Application.Current.MainWindow };
            if ((prompt.ShowDialog() != true) || string.IsNullOrWhiteSpace(prompt.Value)) {
                return;
            }
            instruction = prompt.Value;
        }
        string text = document.Document.Text;
        if (start + length > text.Length) {
            return;
        }
        string passage = text.Substring(start, length);
        int contextStart = Math.Max(0, start - 600);
        int contextEnd = Math.Min(text.Length, start + length + 600);
        string surrounding = text[contextStart..start] + "[PASSAGE]" + text[(start + length)..contextEnd];
        string? reply = await RunAsync("Rewrite", AiTools.Rewrite(passage, kind, instruction, surrounding));
        if (reply is null) {
            return;
        }
        string suggestion = AiTools.CleanRewrite(reply);
        RewriteDialog dialog = new(passage, suggestion) { Owner = Application.Current.MainWindow };
        if (dialog.ShowDialog() != true) {
            return;
        }
        // Replace only if the passage is still where it was.
        if ((start + length <= document.Document.TextLength) && (document.Document.GetText(start, length) == passage)) {
            document.Document.Replace(start, length, dialog.Result);
            document.Select(start, dialog.Result.Length);
        }
        else {
            Clipboard.SetText(dialog.Result);
            MainViewModel.Notify("The passage changed in the meantime, so the rewrite was copied to the clipboard instead.", MainViewModel.NotificationKind.Warning);
        }
    }

    private void OpenChat() {
        AiChatDocumentViewModel? chat = _shell.Documents.OfType<AiChatDocumentViewModel>().FirstOrDefault();
        if (chat is null) {
            chat = new AiChatDocumentViewModel(_shell);
            _shell.Documents.Add(chat);
        }
        _shell.ActiveDocument = chat;
    }
}