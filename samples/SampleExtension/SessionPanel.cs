using System.Globalization;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

using WritingIDE.Sdk;

namespace SampleExtension;

/// <summary>
/// Counts the words written since WritingIDE started. It lives as long as the extension, not the tool window: a tool
/// window is a tab, so it is off screen (and unloaded) while the writer is typing in a chapter.
/// </summary>
public sealed partial class SessionTracker {
    private readonly IPluginHost _host;
    private readonly Dictionary<Guid, int> _startingWords = [];
    private readonly Dictionary<Guid, int> _currentWords = [];
    private readonly HashSet<Guid> _changed = [];
    private DispatcherTimer? _timer;

    public SessionTracker(IPluginHost host) {
        _host = host;
        host.ActiveChapterChanged += (_, e) => SetStartingPoint(e.ChapterId);
        host.ChapterTextChanged += OnChapterTextChanged;
        host.ProjectClosed += (_, _) => {
            _startingWords.Clear();
            _currentWords.Clear();
            _changed.Clear();
            Changed?.Invoke(this, EventArgs.Empty);
        };
    }

    /// <summary>Raised on the UI thread when <see cref="Written"/> changes.</summary>
    public event EventHandler? Changed;

    public int Written => _currentWords.Sum(pair => pair.Value - _startingWords[pair.Key]);

    public int Chapters => _currentWords.Count;

    /// <summary>Notes how long a chapter is before the writer types in it (once per chapter).</summary>
    private void SetStartingPoint(Guid? chapterId) {
        if ((chapterId is Guid id) && !_startingWords.ContainsKey(id)) {
            _startingWords[id] = Words().Count(_host.Project.GetChapterText(id));
        }
    }

    /// <summary>Runs on every keystroke, so it only notes the chapter and (re)starts the timer.</summary>
    private void OnChapterTextChanged(object? sender, ChapterEventArgs e) {
        if (e.ChapterId is not Guid chapterId) {
            return;
        }
        _changed.Add(chapterId);
        if (_timer is null) {
            // Editor events arrive on the UI thread, so the timer is created there.
            _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
            _timer.Tick += (_, _) => Recount();
        }
        _timer.Stop();
        _timer.Start();
    }

    private void Recount() {
        _timer?.Stop();
        foreach (Guid chapterId in _changed) {
            int words = Words().Count(_host.Project.GetChapterText(chapterId));
            // A chapter edited without ever being the active tab (in the second pane, say) starts counting from here.
            _startingWords.TryAdd(chapterId, words);
            _currentWords[chapterId] = words;
        }
        _changed.Clear();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    [GeneratedRegex(@"[\p{L}\p{N}]+(?:['’-][\p{L}\p{N}]+)*")]
    internal static partial Regex Words();
}

/// <summary>
/// The "Writing Session" tool window: the session's words against a goal. It shows per-project storage and theme
/// brushes. Put it in the second pane (Window > Split) to watch it while writing.
/// </summary>
public sealed class SessionPanel : StackPanel {
    private const string GoalKey = "sessionGoal";

    private readonly IPluginHost _host;
    private readonly SessionTracker _tracker;
    private readonly TextBlock _count = new() { FontSize = 40, FontWeight = FontWeights.Light };
    private readonly TextBlock _detail = new() { Margin = new Thickness(0, 4, 0, 12) };
    private readonly ProgressBar _progress = new() { Height = 8, Maximum = 1 };
    private readonly TextBox _goal = new() { Width = 80, HorizontalAlignment = HorizontalAlignment.Left };
    private bool _reached;

    public SessionPanel(IPluginHost host, SessionTracker tracker) {
        _host = host;
        _tracker = tracker;
        Margin = new Thickness(24);
        MaxWidth = 420;
        HorizontalAlignment = HorizontalAlignment.Left;

        // WritingIDE's palette: these keys follow the Light and Dark themes.
        _count.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");
        _detail.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");
        TextBlock goalLabel = new() { Text = "Session goal (words)", Margin = new Thickness(0, 16, 0, 4) };
        goalLabel.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");

        // The goal travels with the project; a new project starts from the writer's last goal.
        _goal.Text = host.Storage.GetProjectValue(GoalKey) ?? host.Storage.GetSetting(GoalKey) ?? "500";
        _goal.LostFocus += (_, _) => SaveGoal();

        Children.Add(_count);
        Children.Add(_detail);
        Children.Add(_progress);
        Children.Add(goalLabel);
        Children.Add(_goal);

        // Listen only while the panel is on screen, so a closed panel is not kept alive by the tracker.
        Loaded += (_, _) => {
            _tracker.Changed += OnTrackerChanged;
            Show();
        };
        Unloaded += (_, _) => _tracker.Changed -= OnTrackerChanged;
    }

    private int Goal => int.TryParse(_goal.Text, NumberStyles.Integer, CultureInfo.CurrentCulture, out int goal) && (goal > 0) ? goal : 500;

    private void OnTrackerChanged(object? sender, EventArgs e) {
        Show();
    }

    private void Show() {
        int written = _tracker.Written;
        _count.Text = written.ToString("+#,0;-#,0;0", CultureInfo.CurrentCulture);
        _detail.Text = $"words this session, in {_tracker.Chapters} chapter(s). Goal: {Goal:N0}.";
        _progress.Value = Math.Clamp((double)written / Goal, 0, 1);
        if ((written >= Goal) && !_reached) {
            _host.SetStatusText($"Session goal reached: {written:N0} words.");
        }
        _reached = written >= Goal;
    }

    private void SaveGoal() {
        string goal = Goal.ToString(CultureInfo.InvariantCulture);
        _host.Storage.SetSetting(GoalKey, goal);
        if (_host.Project.IsOpen) {
            _host.Storage.SetProjectValue(GoalKey, goal);
        }
        Show();
    }
}