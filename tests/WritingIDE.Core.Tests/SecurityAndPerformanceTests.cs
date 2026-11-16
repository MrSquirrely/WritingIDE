using System.Diagnostics;
using System.Text;

using WritingIDE.Core.Analysis;
using WritingIDE.Core.Export;
using WritingIDE.Core.Model;
using WritingIDE.Core.Persistence;
using WritingIDE.Core.Search;

namespace WritingIDE.Core.Tests;

public sealed class SecurityAndPerformanceTests : IDisposable {
    private readonly string _root = Path.Combine(Path.GetTempPath(), "WritingIDE.Tests", Guid.NewGuid().ToString("N"));

    public SecurityAndPerformanceTests() {
        Directory.CreateDirectory(_root);
    }

    [Theory]
    [InlineData("Manuscript/Chapter 1.txt", true)]
    [InlineData("Manuscript\\Book One\\Chapter 1.txt", true)]
    [InlineData("Notes.md", true)]
    [InlineData("../outside.txt", false)]
    [InlineData("Manuscript/../../outside.txt", false)]
    [InlineData("C:\\Windows\\win.ini", false)]
    [InlineData("\\\\server\\share\\a.txt", false)]
    [InlineData("Manuscript/Chapter.txt:hidden", false)]
    [InlineData(".git/hooks/pre-commit.txt", false)]
    [InlineData("Manuscript/run.bat", false)]
    [InlineData("Manuscript/CON.txt", false)]
    [InlineData("Manuscript/aux", false)]
    [InlineData("", false)]
    public void ChapterFilesMustStayInsideTheProject(string fileName, bool safe) {
        Assert.Equal(safe, ProjectSerializer.IsSafeChapterFile(_root, fileName));
    }

    [Fact]
    public void AProjectCannotReadOrWriteOutsideItsFolder() {
        string projectFolder = Path.Combine(_root, "Project");
        Directory.CreateDirectory(projectFolder);
        string secret = Path.Combine(_root, "secret.txt");
        File.WriteAllText(secret, "private");
        string projectFile = Path.Combine(projectFolder, "Shared.novproj");
        File.WriteAllText(projectFile, """
            { "Name": "Shared", "Chapters": [
              { "Id": "11111111-1111-1111-1111-111111111111", "Title": "One", "File": "../secret.txt" },
              { "Id": "22222222-2222-2222-2222-222222222222", "Title": "Two", "File": "../written.txt" } ] }
            """);

        NovelProject project = ProjectSerializer.Load(projectFile);

        Assert.Equal("", project.Chapters[0].Text);
        Assert.Equal(2, project.LoadWarnings.Count);
        project.Chapters[1].Text = "new text";
        project.RemovedChapterFiles.Add("../secret.txt");
        ProjectSerializer.Save(project);
        Assert.False(File.Exists(Path.Combine(_root, "written.txt")));
        Assert.True(File.Exists(secret));
        Assert.All(project.Chapters, chapter => Assert.True(ProjectSerializer.IsSafeChapterFile(projectFolder, chapter.FileName)));
        Assert.Equal("new text", File.ReadAllText(Path.Combine(projectFolder, project.Chapters[1].FileName)));
        Assert.Empty(ProjectSerializer.Load(projectFile).LoadWarnings);
    }

    [Fact]
    public void AwkwardChapterTitlesGetSafeFileNames() {
        NovelProject project = ProjectSerializer.Create(_root, "Titles", "", ProjectTemplate.All.Single(static template => template.Title == "Empty Novel"));
        project.Chapters.Add(new Chapter("...And Then") { Text = "a" });
        project.Chapters.Add(new Chapter("Con") { Text = "b" });
        project.Chapters.Add(new Chapter("Aux.") { Text = "c" });

        ProjectSerializer.Save(project);
        NovelProject loaded = ProjectSerializer.Load(project.FilePath);

        Assert.Empty(loaded.LoadWarnings);
        Assert.Equal(["a", "b", "c"], loaded.Chapters.Skip(1).Select(static chapter => chapter.Text));
    }

    [Fact]
    public void ARecoverySnapshotTakenBeforeASaveIsNotWrittenAfterIt() {
        string previous = RecoveryStore.RootDirectory;
        RecoveryStore.RootDirectory = Path.Combine(_root, "Recovery");
        try {
            NovelProject project = ProjectSerializer.Create(_root, "Race", "", ProjectTemplate.All.Single(static template => template.Title == "Empty Novel"));
            project.MarkDirty();
            Action write = RecoveryStore.Capture(project, static chapter => chapter.Text);

            RecoveryStore.Delete(project.FilePath);
            write();

            Assert.Null(RecoveryStore.Find(project.FilePath));
            RecoveryStore.Capture(project, static chapter => chapter.Text)();
            Assert.NotNull(RecoveryStore.Find(project.FilePath));
        }
        finally {
            RecoveryStore.RootDirectory = previous;
        }
    }

    [Fact]
    public void ADamagedFontCollectionIsReportedNotCrashed() {
        string fonts = Environment.GetFolderPath(Environment.SpecialFolder.Fonts);
        string cambria = Path.Combine(fonts, "cambria.ttc");
        if (!File.Exists(cambria)) {
            return;
        }
        byte[] data = File.ReadAllBytes(cambria);
        // Point the first face's first table far past the end of the file.
        int face = (int)System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(12));
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(face + 12 + 8), 0x7FFFFFF0);
        string damaged = Path.Combine(_root, "damaged.ttc");
        File.WriteAllBytes(damaged, data);
        FontFace first = FontCatalog.Read(damaged).First(static candidate => candidate.FaceIndex == 0);

        Assert.Throws<InvalidDataException>(() => FontCatalog.ExtractFace(first));
    }

    [Fact]
    public void TheWordDiffStaysFastOnALongChapter() {
        StringBuilder builder = new();
        Random random = new(7);
        string[] words = ["the", "keeper", "climbed", "stairs", "wind", "lamp", "Elena", "harbor", "rope", "salt"];
        for (int paragraph = 0; paragraph < 1200; paragraph++) {
            for (int word = 0; word < 25; word++) {
                builder.Append(words[random.Next(words.Length)]).Append(word == 24 ? "." : " ");
            }
            builder.Append("\r\n\r\n");
        }
        string baseline = builder.ToString();
        int middle = baseline.IndexOf(' ', baseline.Length / 2) + 1;
        string current = baseline.Insert(middle, "suddenly ");
        Stopwatch watch = Stopwatch.StartNew();

        IReadOnlyList<RevisionChange> changes = RevisionTracker.Compare(baseline, current);

        watch.Stop();
        RevisionChange change = Assert.Single(changes);
        Assert.Equal("suddenly ", change.Inserted);
        Assert.Equal(middle, change.Offset);
        // 30,000 words: quadratic string building took several hundred milliseconds here.
        Assert.True(watch.ElapsedMilliseconds < 250, $"Compare took {watch.ElapsedMilliseconds} ms.");
        Assert.Equal(current, string.Concat(WordDiff.DiffWords(baseline, current).Where(static piece => piece.Kind != DiffKind.Removed).Select(static piece => piece.Text)));
    }

    public void Dispose() {
        if (Directory.Exists(_root)) {
            Directory.Delete(_root, recursive: true);
        }
    }
}