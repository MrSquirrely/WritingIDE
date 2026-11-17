using WritingIDE.Core.Analysis;
using WritingIDE.Core.Model;
using WritingIDE.Core.Persistence;

namespace WritingIDE.Core.Tests;

public sealed class ProgressAndPlanningTests : IDisposable {
    private static readonly DateOnly Today = new(2026, 11, 17);

    private readonly string _directory = Path.Combine(Path.GetTempPath(), "WritingIDE.Tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public void RecordAddsUpTheDayAndKeepsDaysInOrder() {
        List<WritingDay> log = [];
        WritingProgress.Record(log, Today, 120, 1120);
        WritingProgress.Record(log, Today, 80, 1200);
        WritingProgress.Record(log, Today, -50, 1150);
        WritingProgress.Record(log, Today.AddDays(1), 300, 1450);
        WritingProgress.Record(log, Today.AddDays(-2), 10, 1000);

        Assert.Equal([Today.AddDays(-2), Today, Today.AddDays(1)], log.Select(static day => day.Date));
        Assert.Equal(150, WritingProgress.WrittenOn(log, Today));
        Assert.Equal(1150, log[1].Total);
        Assert.Equal(0, WritingProgress.WrittenOn(log, Today.AddDays(5)));
    }

    [Fact]
    public void StreakCountsDaysThatMetTheGoalAndForgivesAnUnfinishedToday() {
        List<WritingDay> log = [
            new(Today.AddDays(-6), 600, 0),
            new(Today.AddDays(-5), 700, 0),
            new(Today.AddDays(-4), 900, 0),
            // A day under the goal breaks the run.
            new(Today.AddDays(-3), 100, 0),
            new(Today.AddDays(-2), 500, 0),
            new(Today.AddDays(-1), 650, 0),
            new(Today, 200, 0)
        ];

        Assert.Equal(2, WritingProgress.Streak(log, Today, 500));
        Assert.Equal(3, WritingProgress.BestStreak(log, 500));
        // Without a goal, any words count.
        Assert.Equal(7, WritingProgress.Streak(log, Today, 0));
        log[^1] = log[^1] with { Written = 500 };
        Assert.Equal(3, WritingProgress.Streak(log, Today, 500));
        Assert.Equal(0, WritingProgress.Streak([], Today, 500));
    }

    [Fact]
    public void ForecastGivesWordsPerDayAndAProjectedFinish() {
        List<WritingDay> log = Enumerable.Range(0, 14).Select(i => new WritingDay(Today.AddDays(-i), 1000, 0)).ToList();

        ProgressForecast forecast = WritingProgress.Forecast(log, Today, total: 50000, target: 80000, deadline: Today.AddDays(29));

        Assert.Equal(30000, forecast.Remaining);
        Assert.Equal(30, forecast.DaysLeft);
        Assert.Equal(1000, forecast.WordsPerDayNeeded);
        Assert.Equal(1000, forecast.AveragePerDay);
        // 14,000 words in the last four weeks is 500 a calendar day: 60 days to go.
        Assert.Equal(Today.AddDays(60), forecast.ProjectedFinish);

        ProgressForecast overdue = WritingProgress.Forecast([], Today, 10, 100, Today.AddDays(-1));
        Assert.Null(overdue.WordsPerDayNeeded);
        Assert.Null(overdue.ProjectedFinish);
        Assert.Equal(Today, WritingProgress.Forecast([], Today, 100, 100, null).ProjectedFinish);
    }

    [Fact]
    public void BeatSheetReportsBeatsThatAreMisplacedOrOutOfOrder() {
        // Four chapters of 100 words: each is a quarter of the story.
        List<ChapterText> manuscript = Enumerable.Range(1, 4).Select(static i => new ChapterText(new Chapter($"Chapter {i}"), string.Join(' ', Enumerable.Repeat("word", 100)))).ToList();
        BeatSheet sheet = new("Test");
        sheet.Beats.Add(new Beat { Name = "Hook", ExpectedPercent = 0, ChapterId = manuscript[0].Chapter.Id });
        sheet.Beats.Add(new Beat { Name = "Turn", ExpectedPercent = 25, ChapterId = manuscript[3].Chapter.Id });
        sheet.Beats.Add(new Beat { Name = "Midpoint", ExpectedPercent = 50, ChapterId = manuscript[1].Chapter.Id });
        sheet.Beats.Add(new Beat { Name = "Climax", ExpectedPercent = 90 });

        IReadOnlyList<BeatPlacement> placements = BeatSheetAnalyzer.Analyze(sheet, manuscript);

        Assert.Null(placements[0].Problem);
        Assert.Equal(0, placements[0].StartPercent);
        Assert.Equal(25, placements[0].EndPercent);
        Assert.StartsWith("Late", placements[1].Problem);
        Assert.Equal(50, placements[1].Deviation);
        Assert.Contains("'Turn'", placements[2].Problem);
        Assert.Equal("Not placed in a chapter yet.", placements[3].Problem);
    }

    [Fact]
    public void BraidShowsWhereAThreadGoesQuiet() {
        NovelProject project = new();
        PlotThread thread = new("The compass");
        project.PlotThreads.Add(thread);
        List<ChapterText> manuscript = [];
        for (int i = 0; i < 6; i++) {
            Chapter chapter = new($"Chapter {i + 1}") { Text = "Some words here." };
            project.Chapters.Add(chapter);
            manuscript.Add(new ChapterText(chapter, chapter.Text));
        }
        void Mark(int index, ThreadRole role) {
            project.Chapters[index].Comments.Add(new ChapterComment { Kind = MarkerKind.PlotThread, ThreadId = thread.Id, ThreadRole = role, Offset = 0, Length = 4 });
        }
        Mark(0, ThreadRole.Setup);
        Mark(1, ThreadRole.Development);
        Mark(5, ThreadRole.Payoff);

        BraidRow row = Assert.Single(ThreadBraid.Build(project, manuscript));

        Assert.True(row.Cells[0].Setup);
        Assert.True(row.Cells[1].Development);
        Assert.True(row.Cells[5].Payoff);
        Assert.True(row.Cells[3].IsEmpty);
        Assert.Equal(3, row.LongestGap);
        Assert.Equal(2, row.GapStart);
    }

    [Fact]
    public void MovingAChapterNextToAnotherTakesItsBook() {
        NovelProject series = new() { Name = "Saga" };
        Book one = series.ConvertToSeries("One");
        Book two = new("Two");
        series.Books.Add(two);
        Chapter a = new("A") { BookId = one.Id };
        Chapter b = new("B") { BookId = one.Id };
        Chapter c = new("C") { BookId = two.Id };
        Chapter d = new("D") { BookId = two.Id };
        foreach (Chapter chapter in new[] { a, b, c, d }) {
            series.Chapters.Add(chapter);
        }

        series.MoveChapter(a, d, after: false);
        Assert.Equal(["B", "C", "A", "D"], series.Chapters.Select(static chapter => chapter.Title));
        Assert.Equal(two.Id, a.BookId);

        series.MoveChapter(d, b, after: true);
        Assert.Equal(["B", "D", "C", "A"], series.Chapters.Select(static chapter => chapter.Title));
        Assert.Equal(one.Id, d.BookId);

        series.MoveChapterToEnd(b, two);
        Assert.Equal(["D", "C", "A", "B"], series.Chapters.Select(static chapter => chapter.Title));
        Assert.Equal(two.Id, b.BookId);
    }

    [Fact]
    public void GoalsLogAndBeatSheetRoundTrip() {
        NovelProject project = ProjectSerializer.Create(_directory, "Plan", "", ProjectTemplate.All[0]);
        Assert.Equal(500, ProjectSerializer.Load(project.FilePath).DailyWordGoal);

        project.DailyWordGoal = 750;
        project.Deadline = new DateOnly(2027, 3, 1);
        project.WritingLog.Add(new WritingDay(Today, 320, 1320));
        project.BeatSheet = BeatTemplate.All.Single(static template => template.Name == "Save the Cat").Create();
        project.BeatSheet.Beats[3].ChapterId = project.Chapters[0].Id;
        project.BeatSheet.Beats[3].Notes = "The letter arrives.";
        ProjectSerializer.Save(project);
        NovelProject loaded = ProjectSerializer.Load(project.FilePath);

        Assert.Equal(750, loaded.DailyWordGoal);
        Assert.Equal(new DateOnly(2027, 3, 1), loaded.Deadline);
        Assert.Equal(new WritingDay(Today, 320, 1320), Assert.Single(loaded.WritingLog));
        Assert.Equal("Save the Cat", loaded.BeatSheet!.Template);
        Assert.Equal(15, loaded.BeatSheet.Beats.Count);
        Assert.Equal("Catalyst", loaded.BeatSheet.Beats[3].Name);
        Assert.Equal(project.Chapters[0].Id, loaded.BeatSheet.Beats[3].ChapterId);
        Assert.Equal("The letter arrives.", loaded.BeatSheet.Beats[3].Notes);
        Assert.False(loaded.IsDirty);
    }

    public void Dispose() {
        if (Directory.Exists(_directory)) {
            Directory.Delete(_directory, recursive: true);
        }
    }
}