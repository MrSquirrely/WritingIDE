using WritingIDE.Core.Grammar;
using WritingIDE.Core.Model;
using WritingIDE.Core.Persistence;

namespace WritingIDE.Core.Tests;

public sealed class ProjectSerializerTests : IDisposable {
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "WritingIDE.Tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public void SampleProjectRoundTrips() {
        ProjectTemplate sample = ProjectTemplate.All.Single(template => template.Title == "Sample Novel");
        NovelProject created = ProjectSerializer.Create(_directory, "Lighthouse", "Test Author", sample);

        NovelProject loaded = ProjectSerializer.Load(created.FilePath);

        Assert.Equal("Lighthouse", loaded.Name);
        Assert.Equal("Test Author", loaded.Author);
        Assert.Equal(created.Chapters.Count, loaded.Chapters.Count);
        Assert.Equal(created.Chapters[0].Text, loaded.Chapters[0].Text);
        Assert.Equal(created.Entities.Select(e => (e.Id, e.Kind, e.Name, e.Aliases)), loaded.Entities.Select(e => (e.Id, e.Kind, e.Name, e.Aliases)));
        Assert.Equal("41", loaded.Entities.Single(e => e.Name == "Elena Marsh").Attributes.Single(a => a.Key == "Age").Value);
        Assert.False(loaded.IsDirty);
        Assert.True(File.Exists(Path.Combine(loaded.DirectoryPath, loaded.Chapters[0].FileName)));
    }

    [Fact]
    public void SampleChapterProducesDiagnosticsFromSeveralRules() {
        NovelProject project = ProjectTemplate.All.Single(template => template.Title == "Sample Novel").Create("Sample", "");
        GrammarOptions options = new(new Analysis.EntityIndex(project.Entities), new HashSet<string>(), new HashSet<string>());
        AnalysisResult result = new GrammarEngine().Analyze(project.Chapters[0].Text, options);

        string[] expected = ["NW1002", "NW1003", "NW1004", "NW1005", "NW1006", "NW3001", "NW3002", "NW3005", "NW4001"];
        Assert.All(expected, id => Assert.Contains(result.Diagnostics, d => d.RuleId == id));
        Assert.Contains(result.Mentions, m => m.Entity.Name == "Elena Marsh");
        Assert.True(result.Statistics.Words > 150);
    }

    [Fact]
    public void SampleDemonstratesEveryFeature() {
        NovelProject project = ProjectSerializer.Create(_directory, "Lighthouse", "Test Author", ProjectTemplate.All.Single(template => template.Title == "Sample Novel"));
        NovelProject loaded = ProjectSerializer.Load(project.FilePath);
        List<string> titles = loaded.Chapters.Select(static chapter => chapter.Title).ToList();
        GrammarEngine engine = new();

        IReadOnlyList<GrammarDiagnostic> Check(int index) {
            Chapter chapter = loaded.Chapters[index];
            GrammarOptions options = new GrammarOptions(new Analysis.EntityIndex(loaded.Entities), new HashSet<string>(), new HashSet<string>())
                with { Position = new Analysis.ManuscriptPosition(index, titles), StoryDate = chapter.StoryDate };
            return engine.Check(chapter.Text, options);
        }

        // Chapter 3 has one deliberate slip for each continuity rule; chapter 4 (1888) the birth-year one.
        IReadOnlyList<GrammarDiagnostic> three = Check(2);
        Assert.All(new[] { "NW4002", "NW4003", "NW4004", "NW4005", "NW4006", "NW4007", "NW4009" }, id => Assert.Contains(three, d => d.RuleId == id));
        Assert.Contains(Check(3), d => d.RuleId == "NW4008");
        Assert.DoesNotContain(Check(1), d => d.RuleId.StartsWith("NW4", StringComparison.Ordinal));

        // Plot threads: one never paid off, one paid off before it is set up, one clean.
        IReadOnlyList<Analysis.ThreadReport> threads = Analysis.PlotThreadAnalyzer.Analyze(loaded, loaded.Chapters.Select(static chapter => new Analysis.ChapterText(chapter, chapter.Text)).ToList());
        Assert.Contains("never paid off", Assert.Single(threads[0].Problems));
        Assert.Contains("comes before the first setup", Assert.Single(threads[1].Problems));
        Assert.Empty(threads[2].Problems);

        // Timeline: chapter 4 is set years before chapter 3; the scenes, tasks, comments and bookmark are there.
        Assert.True(Analysis.StoryTime.TryGetSortKey(loaded.Chapters[3].StoryDate) < Analysis.StoryTime.TryGetSortKey(loaded.Chapters[2].StoryDate));
        Assert.Equal(2, Analysis.SceneParser.Split(loaded.Chapters[2].Text).Count);
        Assert.Contains(Analysis.TaskScanner.Scan(loaded.Chapters[1], loaded.Chapters[1].Text), task => task.Kind == "TK");
        Assert.Equal(ChapterStatus.Outline, loaded.Chapters[4].Status);
        Assert.Single(loaded.Chapters[2].Comments, comment => comment.Kind == MarkerKind.Bookmark);
        Assert.Equal(2, loaded.Chapters[0].Comments.Count(comment => comment.Kind == MarkerKind.Comment));
        Assert.Equal(2, loaded.Entities.Single(e => e.Name == "Silas Crane").Relationships.Count);

        // Chapter 2: a snapshot to compare with, and tracked changes against its revision baseline.
        Chapter two = loaded.Chapters[1];
        Assert.Equal("First draft", Assert.Single(SnapshotStore.List(loaded.DirectoryPath, two.Id)).Label);
        Assert.True(two.TrackRevisions);
        string baseline = RevisionStore.Load(loaded.DirectoryPath, two)!;
        IReadOnlyList<Analysis.RevisionChange> changes = Analysis.RevisionTracker.Compare(baseline, two.Text);
        Assert.Contains(changes, change => (change.Deleted == "Bad") && (change.Inserted == "Rough"));
        Assert.Contains(changes, change => change.IsInsertion && change.Inserted.Contains("hands shake", StringComparison.Ordinal));
        Assert.False(loaded.IsDirty);
    }

    public void Dispose() {
        if (Directory.Exists(_directory)) {
            Directory.Delete(_directory, recursive: true);
        }
    }
}