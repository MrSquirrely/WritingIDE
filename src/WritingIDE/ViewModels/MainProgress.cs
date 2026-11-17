using System.Windows.Input;
using System.Windows.Threading;

using WritingIDE.Core.Analysis;
using WritingIDE.Core.Model;
using WritingIDE.Infrastructure;

namespace WritingIDE.ViewModels;

/// <summary>Writing habit and planning: the daily goal, the writing log, sprints, and the planning documents.</summary>
public sealed partial class MainViewModel {
    private readonly Dictionary<Guid, (string Text, int Words)> _progressCounts = [];
    private readonly DispatcherTimer _sprintTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    private int? _progressBaseline;
    private int _sprintStartTotal;
    private DateTime _sprintEnd;

    public ICommand OpenWritingProgressCommand { get; private set; } = null!;

    public ICommand ToggleSprintCommand { get; private set; } = null!;

    public ICommand OpenCorkboardCommand { get; private set; } = null!;

    public ICommand OpenBeatSheetCommand { get; private set; } = null!;

    public ICommand OpenThreadChartCommand { get; private set; } = null!;

    /// <summary>Words written today in the open project, net of deletions.</summary>
    public int WordsToday { get; private set => SetProperty(ref field, value); }

    /// <summary>The status bar's "Today 312 / 500".</summary>
    public string TodayText { get; private set => SetProperty(ref field, value); } = "";

    /// <summary>Today's words against the daily goal, 0 to 100.</summary>
    public double TodayPercent { get; private set => SetProperty(ref field, value); }

    public bool IsSprinting { get; private set => SetProperty(ref field, value); }

    /// <summary>The status bar's "Sprint 12:34 · 210 words"; empty when no sprint is running.</summary>
    public string SprintText { get; private set => SetProperty(ref field, value); } = "";

    public TimeSpan SprintRemaining { get; private set => SetProperty(ref field, value); }

    public int SprintWords { get; private set => SetProperty(ref field, value); }

    /// <summary>What the last sprint achieved, for the Writing Progress page.</summary>
    public string LastSprintText { get; private set => SetProperty(ref field, value); } = "";

    /// <summary>The length of a sprint in minutes (a per-user setting).</summary>
    public int SprintMinutes {
        get => Settings.SprintMinutes;
        set {
            int minutes = Math.Clamp(value, 1, 180);
            if (Settings.SprintMinutes != minutes) {
                Settings.SprintMinutes = minutes;
                Settings.Save();
                OnPropertyChanged();
            }
        }
    }

    /// <summary>Raised when the writing log or a goal changed, so the Writing Progress page redraws.</summary>
    public event Action? ProgressChanged;

    private void InitializeProgress() {
        OpenWritingProgressCommand = new RelayCommand(() => OpenToolDocument(() => new WritingProgressDocumentViewModel(this)), () => HasProject);
        ToggleSprintCommand = new RelayCommand(() => {
            if (IsSprinting) {
                EndSprint(finished: false);
            }
            else {
                StartSprint();
            }
        }, () => HasProject);
        OpenCorkboardCommand = new RelayCommand(() => OpenToolDocument(() => new CorkboardDocumentViewModel(this)), () => HasProject);
        OpenBeatSheetCommand = new RelayCommand(() => OpenToolDocument(() => new BeatSheetDocumentViewModel(this)), () => HasProject);
        OpenThreadChartCommand = new RelayCommand(() => OpenToolDocument(() => new ThreadChartDocumentViewModel(this)), () => HasProject);
        _sprintTimer.Tick += (_, _) => OnSprintTick();
    }

    /// <summary>
    /// Forgets the manuscript's last known length, so that the next change in it is not logged as writing. Called
    /// around changes that are not writing: opening a project, importing, deleting a chapter.
    /// </summary>
    private void ResetProgressBaseline() {
        _progressBaseline = null;
    }

    /// <summary>True while an import is adding chapters: nothing is logged until it is done.</summary>
    private bool _progressPaused;

    /// <summary>Adds what was written since the last call to today's entry in the writing log.</summary>
    private void TrackProgress() {
        NovelProject? project = Project;
        if (project is null) {
            _progressBaseline = null;
            _progressCounts.Clear();
            WordsToday = 0;
            TodayText = "";
            TodayPercent = 0;
            if (IsSprinting) {
                EndSprint(finished: false);
            }
            return;
        }
        if (_progressPaused) {
            _progressBaseline = null;
            return;
        }
        int total = CountManuscriptWords(project);
        DateOnly today = DateOnly.FromDateTime(DateTime.Now);
        if (_progressBaseline is int baseline) {
            if (total != baseline) {
                WritingProgress.Record(project.WritingLog, today, total - baseline, total);
            }
        }
        _progressBaseline = total;
        WordsToday = WritingProgress.WrittenOn(project.WritingLog, today);
        int goal = project.DailyWordGoal;
        bool reachedBefore = TodayPercent >= 100;
        TodayText = goal > 0 ? $"Today {WordsToday:N0} / {goal:N0}" : $"Today {WordsToday:N0}";
        TodayPercent = goal > 0 ? Math.Clamp(100.0 * WordsToday / goal, 0, 100) : 0;
        if ((TodayPercent >= 100) && !reachedBefore && (_announcedGoalDay != today)) {
            _announcedGoalDay = today;
            int streak = WritingProgress.Streak(project.WritingLog, today, goal);
            Notify($"Daily goal reached: {WordsToday:N0} words.{(streak > 1 ? $" That is {streak} days in a row." : "")}", NotificationKind.Success);
            Extensions.RaiseDailyGoalReached();
        }
        if (IsSprinting) {
            SprintWords = total - _sprintStartTotal;
            UpdateSprintText();
        }
        ProgressChanged?.Invoke();
    }

    private DateOnly? _announcedGoalDay;

    /// <summary>
    /// The manuscript's length, counted the same way every time (the analysis counts tokens, which can differ by a word
    /// or two). A closed chapter's text is the same string until it changes, so only edited chapters are recounted.
    /// </summary>
    private int CountManuscriptWords(NovelProject project) {
        int total = 0;
        foreach (Chapter chapter in project.Chapters) {
            string text = GetChapterText(chapter);
            if (!_progressCounts.TryGetValue(chapter.Id, out (string Text, int Words) cached) || !(ReferenceEquals(cached.Text, text) || (cached.Text == text))) {
                cached = (text, TextStatistics.CountWords(text));
                _progressCounts[chapter.Id] = cached;
            }
            total += cached.Words;
        }
        if (_progressCounts.Count > project.Chapters.Count) {
            HashSet<Guid> ids = project.Chapters.Select(static chapter => chapter.Id).ToHashSet();
            foreach (Guid id in _progressCounts.Keys.Where(id => !ids.Contains(id)).ToList()) {
                _progressCounts.Remove(id);
            }
        }
        return total;
    }

    /// <summary>Corkboard: drops a chapter before or after another one, in that chapter's book.</summary>
    public void MoveChapterNextTo(Chapter chapter, Chapter target, bool after) {
        Project?.MoveChapter(chapter, target, after);
        RefreshErrorList();
    }

    /// <summary>Corkboard: drops a chapter at the end of a book (or of a novel's manuscript).</summary>
    public void MoveChapterToEnd(Chapter chapter, Book? book) {
        Project?.MoveChapterToEnd(chapter, book);
        RefreshErrorList();
    }

    /// <summary>The daily goal, deadline or target changed on the Writing Progress page.</summary>
    public void OnGoalsChanged() {
        TrackProgress();
    }

    // ------------------------------------------------------------------
    // Sprints
    // ------------------------------------------------------------------

    public void StartSprint() {
        if ((Project is null) || IsSprinting) {
            return;
        }
        TrackProgress();
        _sprintStartTotal = _progressBaseline ?? 0;
        _sprintEnd = DateTime.Now.AddMinutes(SprintMinutes);
        SprintWords = 0;
        SprintRemaining = TimeSpan.FromMinutes(SprintMinutes);
        IsSprinting = true;
        UpdateSprintText();
        _sprintTimer.Start();
        StatusText = $"Sprint started: {SprintMinutes} minutes. Write!";
    }

    private void OnSprintTick() {
        SprintRemaining = _sprintEnd - DateTime.Now;
        if (SprintRemaining <= TimeSpan.Zero) {
            EndSprint(finished: true);
        }
        else {
            UpdateSprintText();
        }
    }

    private void EndSprint(bool finished) {
        _sprintTimer.Stop();
        if (!IsSprinting) {
            return;
        }
        IsSprinting = false;
        if (Project is not null) {
            SprintWords = CountManuscriptWords(Project) - _sprintStartTotal;
        }
        double minutes = Math.Max(1.0 / 60, SprintMinutes - Math.Max(0, (_sprintEnd - DateTime.Now).TotalMinutes));
        string words = $"{SprintWords:N0} word{(SprintWords == 1 ? "" : "s")}";
        LastSprintText = finished
            ? $"Last sprint: {words} in {SprintMinutes} minutes ({SprintWords / minutes:0} a minute)."
            : $"Last sprint (stopped early): {words} in {minutes:0.#} minutes.";
        SprintText = "";
        SprintRemaining = TimeSpan.Zero;
        Log(LastSprintText);
        StatusText = LastSprintText;
        if (finished) {
            System.Media.SystemSounds.Asterisk.Play();
            Notify($"Sprint finished: {words} in {SprintMinutes} minutes.", NotificationKind.Success);
        }
        ProgressChanged?.Invoke();
        Extensions.RaiseSprintFinished(SprintWords, minutes, finished);
    }

    private void UpdateSprintText() {
        TimeSpan remaining = SprintRemaining < TimeSpan.Zero ? TimeSpan.Zero : SprintRemaining;
        SprintText = $"Sprint {(int)remaining.TotalMinutes}:{remaining.Seconds:00}  ·  {SprintWords:N0} words";
    }
}