using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

using WritingIDE.Core.Ai;
using WritingIDE.Core.Analysis;
using WritingIDE.Core.Model;
using WritingIDE.Infrastructure;
using WritingIDE.Views;

namespace WritingIDE.ViewModels;

/// <summary>A plot thread's row in the tracker.</summary>
public sealed class ThreadRow(ThreadReport report) {
    public ThreadReport Report { get; } = report;

    public PlotThread Thread => Report.Thread;

    public string Counts => $"{Report.Setups} setup(s) \u00B7 {Report.Markers.Count(static marker => marker.Marker.ThreadRole == ThreadRole.Development)} development(s) \u00B7 {Report.Payoffs} payoff(s)";

    public string Problems => string.Join(" ", Report.Problems);

    public bool HasProblems => Report.Problems.Count > 0;

    public IReadOnlyList<ThreadMarkerLocation> Markers => Report.Markers;
}

/// <summary>Analyze > Plot Threads: every thread with its setups and payoffs, and what is unresolved.</summary>
public sealed class PlotThreadsDocumentViewModel : DocumentViewModel {
    public PlotThreadsDocumentViewModel(MainViewModel shell) : base(shell) {
        AddCommand = new RelayCommand(() => {
            PlotThread? thread = Shell.CreatePlotThread();
            if (thread is not null) {
                Refresh();
                Selected = Rows.FirstOrDefault(row => row.Thread == thread);
            }
        });
        DeleteCommand = new RelayCommand(Delete, () => Selected is not null);
        OpenMarkerCommand = new RelayCommand(parameter => {
            if (parameter is ThreadMarkerLocation location) {
                Shell.NavigateTo(location.Chapter, location.Marker.Offset, location.Marker.Length);
            }
        });
        RefreshCommand = new RelayCommand(Refresh);
    }

    public override string Title => "Plot Threads";

    public override string Glyph => "\uE8F1";

    public override Brush GlyphBrush => Visuals.GetKindBrush(EntityKind.Event);

    public ObservableCollection<ThreadRow> Rows { get; } = [];

    public ThreadRow? Selected { get; set => SetProperty(ref field, value); }

    public IReadOnlyList<PlotThreadStatus> Statuses { get; } = Enum.GetValues<PlotThreadStatus>();

    public string Summary { get; private set => SetProperty(ref field, value); } = "";

    public ICommand AddCommand { get; }

    public ICommand DeleteCommand { get; }

    public ICommand OpenMarkerCommand { get; }

    public ICommand RefreshCommand { get; }

    protected override FrameworkElement CreateView() {
        return new PlotThreadsView { DataContext = this };
    }

    public override void OnActivated() {
        Refresh();
    }

    public void Refresh() {
        NovelProject? project = Shell.Project;
        if (project is null) {
            return;
        }
        foreach (ChapterDocumentViewModel document in Shell.Documents.OfType<ChapterDocumentViewModel>()) {
            document.SyncComments();
        }
        Guid? selected = Selected?.Thread.Id;
        Rows.Clear();
        foreach (ThreadReport report in PlotThreadAnalyzer.Analyze(project, Shell.GetManuscript())) {
            Rows.Add(new ThreadRow(report));
        }
        Selected = Rows.FirstOrDefault(row => row.Thread.Id == selected) ?? Rows.FirstOrDefault();
        int open = Rows.Count(static row => row.Thread.Status == PlotThreadStatus.Open);
        int problems = Rows.Count(static row => row.HasProblems);
        Summary = Rows.Count == 0
            ? "No plot threads yet. Add one here, or select a passage in a chapter and right-click > Mark for Plot Thread."
            : $"{Rows.Count} thread(s), {open} open, {problems} needing attention.";
    }

    private void Delete() {
        if ((Selected is null) || (Shell.Project is null)) {
            return;
        }
        PlotThread thread = Selected.Thread;
        if (HandyControl.Controls.MessageBox.Show(Application.Current.MainWindow!, $"Delete the plot thread '{thread.Name}' and its {Selected.Markers.Count} marked passage(s)? The text is not changed.",
                "Plot Threads", MessageBoxButton.OKCancel, MessageBoxImage.Warning) != MessageBoxResult.OK) {
            return;
        }
        foreach (Chapter chapter in Shell.Project.Chapters) {
            foreach (ChapterComment marker in chapter.Comments.Where(marker => marker.ThreadId == thread.Id).ToList()) {
                chapter.Comments.Remove(marker);
            }
        }
        Shell.Project.PlotThreads.Remove(thread);
        Refresh();
    }
}

/// <summary>A message in the AI chat.</summary>
public sealed record ChatMessage(string Role, string Text) {
    public bool IsUser => Role == "user";

    public HorizontalAlignment Alignment => IsUser ? HorizontalAlignment.Right : HorizontalAlignment.Left;
}

/// <summary>AI > Ask About Your Book: a chat that has read the story bible, the story elements and the manuscript.</summary>
public sealed class AiChatDocumentViewModel : DocumentViewModel {
    public AiChatDocumentViewModel(MainViewModel shell) : base(shell) {
        SendCommand = new RelayCommand(() => _ = SendAsync(), () => !Shell.Ai.IsBusy && (Input.Trim().Length > 0));
        ClearCommand = new RelayCommand(() => Messages.Clear(), () => Messages.Count > 0);
    }

    public override string Title => "Ask About Your Book";

    public override string Glyph => "\uE99A";

    public override Brush GlyphBrush => Visuals.ProjectBrush;

    public ObservableCollection<ChatMessage> Messages { get; } = [];

    public string Input { get; set => SetProperty(ref field, value); } = "";

    /// <summary>Send the whole manuscript (best with Gemini's large context), or synopses plus the current chapter.</summary>
    public bool IncludeFullText { get; set => SetProperty(ref field, value); } = true;

    public string ProviderNote => $"Answers come from {Shell.Ai.ProviderName}. Change the provider in AI > AI Settings.";

    public ICommand SendCommand { get; }

    public ICommand ClearCommand { get; }

    protected override FrameworkElement CreateView() {
        return new AiChatView { DataContext = this };
    }

    public override void OnActivated() {
        OnPropertyChanged(nameof(ProviderNote));
    }

    private async Task SendAsync() {
        string question = Input.Trim();
        if (question.Length == 0) {
            return;
        }
        List<AiMessage> history = Messages.Select(static message => new AiMessage(message.Role, message.Text)).ToList();
        Messages.Add(new ChatMessage("user", question));
        Input = "";
        string context = Shell.BuildAiContext(IncludeFullText);
        if (!IncludeFullText && (Shell.ActiveChapter is ChapterDocumentViewModel current)) {
            context += $"\n\n## The chapter I am working on: {current.Chapter.Title}\n\n{current.Document.Text}";
        }
        string? reply = await Shell.Ai.RunAsync("Ask about your book", AiTools.Ask(context, history, question));
        Messages.Add(new ChatMessage("assistant", reply?.Trim() ?? "(No answer: see the notification for what went wrong.)"));
    }
}