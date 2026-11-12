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

    public void Dispose() {
        if (Directory.Exists(_directory)) {
            Directory.Delete(_directory, recursive: true);
        }
    }
}