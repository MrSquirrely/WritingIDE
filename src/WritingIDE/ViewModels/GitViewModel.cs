using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

using WritingIDE.Core;
using WritingIDE.Core.Model;
using WritingIDE.Infrastructure;
using WritingIDE.Services;
using WritingIDE.Views;

namespace WritingIDE.ViewModels;

/// <summary>A changed file in Git Changes, shown by chapter name where possible.</summary>
public sealed class GitChangeItem(GitFileChange change, string displayName, string fullPath, Chapter? chapter) {
    public GitFileChange Change { get; } = change;

    public string DisplayName { get; } = displayName;

    public string FullPath { get; } = fullPath;

    public Chapter? Chapter { get; } = chapter;

    public string Path => Change.Path;

    public bool IsUntracked => Change.Status == "??";

    public string StatusLetter => Change.Status.Trim() switch {
        "??" => "A",
        string status when status.Length > 0 => status[..1],
        _ => "M"
    };

    public string StatusDescription => StatusLetter switch {
        "A" => IsUntracked ? "New (untracked)" : "Added",
        "D" => "Deleted",
        "R" => $"Renamed from {Change.OriginalPath}",
        _ => "Modified"
    };

    public Brush StatusBrush => StatusLetter switch {
        "A" => Visuals.CreateBrush("#388A34"),
        "D" => Visuals.ErrorBrush,
        "R" => Visuals.MessageBrush,
        _ => Visuals.WarningBrush
    };
}

/// <summary>The Git Changes tool window: status, commit, push/pull and history for the project's repository.</summary>
public sealed class GitViewModel : ObservableObject {
    private readonly MainViewModel _shell;
    private readonly DispatcherTimer _refreshTimer;
    private GitService? _git;
    private string? _root;
    private string? _upstream;
    private bool? _gitInstalled;

    public GitViewModel(MainViewModel shell) {
        _shell = shell;
        _refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
        _refreshTimer.Tick += async (_, _) => {
            _refreshTimer.Stop();
            await RefreshAsync();
        };
        RefreshCommand = new RelayCommand(() => _ = RefreshAsync(), () => !IsBusy);
        InitCommand = new RelayCommand(() => _ = RunAsync(InitAsync), () => !IsBusy && (_shell.Project is not null));
        CommitCommand = new RelayCommand(() => _ = RunAsync(() => CommitAsync(push: false)), () => CanCommit);
        CommitAndPushCommand = new RelayCommand(() => _ = RunAsync(() => CommitAsync(push: true)), () => CanCommit);
        PushCommand = new RelayCommand(() => _ = RunAsync(PushAsync), () => !IsBusy && IsRepository);
        PullCommand = new RelayCommand(() => _ = RunAsync(PullAsync), () => !IsBusy && IsRepository && (_upstream is not null));
        AddRemoteCommand = new RelayCommand(() => _ = RunAsync(AddRemoteAsync), () => !IsBusy && IsRepository);
        OpenDiffCommand = new RelayCommand(parameter => {
            if (parameter is GitChangeItem item) {
                _ = OpenDiffAsync(item);
            }
        });
        OpenCommitCommand = new RelayCommand(parameter => {
            if (parameter is GitCommitInfo commit) {
                _ = OpenCommitAsync(commit);
            }
        });
        CopyCommitIdCommand = new RelayCommand(parameter => {
            if (parameter is GitCommitInfo commit) {
                Clipboard.SetText(commit.Hash);
            }
        });
    }

    public bool IsGitInstalled { get; private set => SetProperty(ref field, value); } = true;

    public bool IsRepository {
        get;
        private set {
            if (SetProperty(ref field, value)) {
                OnPropertyChanged(nameof(IsNotRepository));
            }
        }
    }

    public bool IsNotRepository => IsGitInstalled && !IsRepository && (_shell.Project is not null);

    public bool IsBusy {
        get;
        private set {
            SetProperty(ref field, value);
            CommandManager.InvalidateRequerySuggested();
        }
    }

    public string Branch { get; private set => SetProperty(ref field, value); } = "";

    public int Ahead { get; private set => SetProperty(ref field, value); }

    public int Behind { get; private set => SetProperty(ref field, value); }

    public string CommitMessage {
        get;
        set {
            SetProperty(ref field, value);
            CommandManager.InvalidateRequerySuggested();
        }
    } = "";

    /// <summary>Outcome of the last operation, shown under the buttons.</summary>
    public string Message { get; private set => SetProperty(ref field, value); } = "";

    public ObservableCollection<GitChangeItem> Changes { get; } = [];

    public ObservableCollection<GitCommitInfo> History { get; } = [];

    /// <summary>Status bar summary: "main \u21911 \u21930 \u00B7 3 changes".</summary>
    public string StatusBarText => !IsRepository ? "" : $"{Branch}{(_upstream is null ? "" : $"  \u2191{Ahead} \u2193{Behind}")}  \u00B7  {Changes.Count} change{(Changes.Count == 1 ? "" : "s")}";

    public string ChangesHeader => Changes.Count == 0 ? "No changes since the last commit." : $"Changes ({Changes.Count})";

    private bool CanCommit => !IsBusy && IsRepository && (Changes.Count > 0) && (CommitMessage.Trim().Length > 0);

    public ICommand RefreshCommand { get; }

    public ICommand InitCommand { get; }

    public ICommand CommitCommand { get; }

    public ICommand CommitAndPushCommand { get; }

    public ICommand PushCommand { get; }

    public ICommand PullCommand { get; }

    public ICommand AddRemoteCommand { get; }

    public ICommand OpenDiffCommand { get; }

    /// <summary>Opens a commit from History with the word changes it made to each file.</summary>
    public ICommand OpenCommitCommand { get; }

    public ICommand CopyCommitIdCommand { get; }

    /// <summary>Asks for a refresh soon (debounced): after saves, project changes and window activation.</summary>
    public void RequestRefresh() {
        _refreshTimer.Stop();
        _refreshTimer.Start();
    }

    public async Task RefreshAsync() {
        NovelProject? project = _shell.Project;
        if (project is null) {
            _git = null;
            _root = null;
            IsRepository = false;
            Changes.Clear();
            History.Clear();
            RaiseSummary();
            return;
        }
        _gitInstalled ??= await GitService.IsGitInstalledAsync();
        IsGitInstalled = _gitInstalled.Value;
        if (!IsGitInstalled) {
            IsRepository = false;
            Message = "Git is not installed. Get it from git-scm.com, then restart WritingIDE.";
            RaiseSummary();
            return;
        }
        _git = new GitService(project.DirectoryPath);
        try {
            _root = await _git.GetRepositoryRootAsync();
            IsRepository = _root is not null;
            if (!IsRepository) {
                Changes.Clear();
                History.Clear();
                RaiseSummary();
                return;
            }
            GitStatusInfo status = await _git.GetStatusAsync();
            Branch = status.Branch;
            _upstream = status.Upstream;
            Ahead = status.Ahead;
            Behind = status.Behind;
            Changes.Clear();
            foreach (GitFileChange change in status.Changes) {
                Changes.Add(Describe(project, change));
            }
            History.Clear();
            foreach (GitCommitInfo commit in await _git.GetHistoryAsync()) {
                History.Add(commit);
            }
        }
        catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception or IOException) {
            Message = exception.Message;
        }
        RaiseSummary();
    }

    private GitChangeItem Describe(NovelProject project, GitFileChange change) {
        string fullPath = System.IO.Path.GetFullPath(System.IO.Path.Combine(_root!, change.Path));
        Chapter? chapter = project.Chapters.FirstOrDefault(chapter => (chapter.FileName.Length > 0)
            && string.Equals(System.IO.Path.GetFullPath(System.IO.Path.Combine(project.DirectoryPath, chapter.FileName)), fullPath, StringComparison.OrdinalIgnoreCase));
        string display = chapter is not null ? project.GetDisplayName(chapter)
            : string.Equals(fullPath, System.IO.Path.GetFullPath(project.FilePath), StringComparison.OrdinalIgnoreCase) ? "Project file (story elements, bible, chapter list)"
            : change.Path;
        return new GitChangeItem(change, display, fullPath, chapter);
    }

    private void RaiseSummary() {
        OnPropertyChanged(nameof(StatusBarText));
        OnPropertyChanged(nameof(ChangesHeader));
        OnPropertyChanged(nameof(IsNotRepository));
        CommandManager.InvalidateRequerySuggested();
    }

    private async Task RunAsync(Func<Task> operation) {
        if (IsBusy) {
            return;
        }
        IsBusy = true;
        try {
            await operation();
        }
        catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception or IOException) {
            Message = exception.Message;
            _shell.Log($"Git: {exception.Message}");
        }
        finally {
            IsBusy = false;
            await RefreshAsync();
        }
    }

    private async Task InitAsync() {
        NovelProject project = _shell.Project!;
        _git = new GitService(project.DirectoryPath);
        Report(await _git.InitAsync(), $"Created a Git repository in {project.DirectoryPath}.");
    }

    private async Task CommitAsync(bool push) {
        // Commit what is on disk: save first so the commit matches what the writer sees.
        if (_shell.IsProjectDirty) {
            _shell.SaveAll(automatic: true);
        }
        GitResult result = await _git!.CommitAllAsync(CommitMessage.Trim());
        if (!result.Success && result.Message.Contains("tell me who you are", StringComparison.OrdinalIgnoreCase)) {
            Message = "Git needs your name and email first. In a terminal run: git config --global user.name \"Your Name\" and git config --global user.email \"you@example.com\".";
            _shell.Log($"Git: {Message}");
            return;
        }
        if (!Report(result, "Committed.")) {
            return;
        }
        string subject = CommitMessage.Trim().Split('\n')[0];
        CommitMessage = "";
        MainViewModel.Notify($"Committed: {subject}", MainViewModel.NotificationKind.Success);
        if (push) {
            await PushAsync();
        }
    }

    private async Task PushAsync() {
        if (!await _git!.HasRemoteAsync()) {
            Message = "This repository has no remote yet. Use \"Add remote...\" to connect it to GitHub (or another host) first.";
            return;
        }
        Message = "Pushing...";
        if (Report(await _git.PushAsync(Branch, _upstream is not null), $"Pushed {Branch}.")) {
            MainViewModel.Notify($"Pushed {Branch}.", MainViewModel.NotificationKind.Success);
        }
    }

    private async Task PullAsync() {
        if (_shell.IsProjectDirty) {
            Message = "Save your changes before pulling, so nothing is overwritten.";
            return;
        }
        Message = "Pulling...";
        if (Report(await _git!.PullAsync(), "Pulled the latest changes. Reopen the project to load them.")) {
            if (_shell.Project is { FilePath: string path } && (HandyControl.Controls.MessageBox.Show(Application.Current.MainWindow!,
                "Pulled the latest changes from the remote. Reload the project now to see them?", "Git Pull", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)) {
                _shell.OpenProject(path);
            }
        }
    }

    private async Task AddRemoteAsync() {
        InputDialog dialog = new("Add Remote", "URL of the remote repository (for example https://github.com/you/my-novel.git):", "", null) {
            Owner = Application.Current.MainWindow
        };
        if ((dialog.ShowDialog() != true) || string.IsNullOrWhiteSpace(dialog.Value)) {
            return;
        }
        Report(await _git!.AddRemoteAsync(dialog.Value.Trim()), $"Added remote 'origin': {dialog.Value.Trim()}. You can push now.");
    }

    /// <summary>Shows the result in the panel and the Output window. Returns success.</summary>
    private bool Report(GitResult result, string successMessage) {
        Message = result.Success ? successMessage : result.Message;
        _shell.Log($"Git: {(result.Success ? successMessage : result.Message)}");
        return result.Success;
    }

    private async Task OpenDiffAsync(GitChangeItem item) {
        if (_git is null) {
            return;
        }
        string diff;
        if (item.IsUntracked && File.Exists(item.FullPath)) {
            // New file: everything is an addition.
            diff = string.Join("\n", (await File.ReadAllLinesAsync(item.FullPath)).Select(static line => line.Length == 0 ? "" : "{+" + line + "+}"));
        }
        else {
            diff = await _git.GetWordDiffAsync(item.Path);
        }
        _shell.OpenDiff(item.DisplayName, item.StatusDescription, diff);
    }

    private async Task OpenCommitAsync(GitCommitInfo commit) {
        if ((_git is null) || (_root is null)) {
            return;
        }
        try {
            _shell.OpenCommit(await _git.GetCommitAsync(commit.Hash), _root);
        }
        catch (Exception exception) when (exception is InvalidOperationException or FormatException or System.ComponentModel.Win32Exception or IOException) {
            Message = $"Could not read commit {commit.Hash}: {exception.Message}";
            _shell.Log($"Git: {Message}");
        }
    }
}