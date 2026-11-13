using System.Diagnostics;
using System.Text;

using WritingIDE.Core.Persistence;

namespace WritingIDE.Core.Tests;

public sealed class GitCommitDiffTests : IDisposable {
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "WritingIDE.Tests", Guid.NewGuid().ToString("N"));

    public GitCommitDiffTests() {
        Directory.CreateDirectory(Path.Combine(_directory, "Manuscript"));
        Git("init", "-b", "main");
        Git("config", "user.name", "Test Writer");
        Git("config", "user.email", "writer@example.invalid");
        Git("config", "core.autocrlf", "false");
    }

    [Fact]
    public void ReadsTheFirstCommitAsAdditions() {
        Write("Manuscript/Chapter One.txt", "The keeper climbed the stairs.\n\nThe lamp was cold.\n");
        Commit("Draft chapter one\n\nThe opening scene.");

        CommitDetails commit = Show("HEAD");

        Assert.Equal("Draft chapter one", commit.Subject);
        Assert.Equal("The opening scene.", commit.Body);
        Assert.Equal("Test Writer", commit.Author);
        Assert.NotNull(commit.Date);
        Assert.Equal(40, commit.Hash.Length);
        Assert.StartsWith(commit.ShortHash, commit.Hash, StringComparison.Ordinal);
        CommitFileDiff file = Assert.Single(commit.Files);
        Assert.Equal("Manuscript/Chapter One.txt", file.Path);
        Assert.Equal(CommitFileStatus.Added, file.Status);
        Assert.Equal(9, file.WordsAdded);
        Assert.Equal(0, file.WordsRemoved);
        Assert.StartsWith("@@", file.Diff, StringComparison.Ordinal);
    }

    [Fact]
    public void ReadsWordChangesAddedFilesRenamesAndDeletions() {
        Write("Manuscript/Chapter One.txt", "The keeper climbed the stairs.\n\nThe lamp was cold.\n");
        Write("Manuscript/Chapter Two.txt", "Elena found the logbook in the drawer, under a stack of charts and an old pipe.\n");
        Write("Notes.txt", "Remember the compass.\n");
        Commit("First");
        Write("Manuscript/Chapter One.txt", "The keeper walked up the stairs.\n\nThe lamp was cold.\n");
        Write("Manuscript/Chapter Three.txt", "Nobody slept.\n");
        File.Move(Path.Combine(_directory, "Manuscript", "Chapter Two.txt"), Path.Combine(_directory, "Manuscript", "Chapter 2.txt"));
        File.Delete(Path.Combine(_directory, "Notes.txt"));
        Commit("Second");

        CommitDetails commit = Show("HEAD");

        CommitFileDiff one = commit.Files.Single(static file => file.Path == "Manuscript/Chapter One.txt");
        Assert.Equal(CommitFileStatus.Modified, one.Status);
        Assert.Contains("[-climbed-]{+walked up+}", one.Diff, StringComparison.Ordinal);
        Assert.Equal((2, 1), (one.WordsAdded, one.WordsRemoved));
        Assert.Equal(CommitFileStatus.Added, commit.Files.Single(static file => file.Path == "Manuscript/Chapter Three.txt").Status);
        CommitFileDiff renamed = commit.Files.Single(static file => file.Path == "Manuscript/Chapter 2.txt");
        Assert.Equal(CommitFileStatus.Renamed, renamed.Status);
        Assert.Equal("Manuscript/Chapter Two.txt", renamed.OldPath);
        Assert.Equal(0, renamed.WordsAdded);
        CommitFileDiff deleted = commit.Files.Single(static file => file.Path == "Notes.txt");
        Assert.Equal(CommitFileStatus.Deleted, deleted.Status);
        Assert.Equal(3, deleted.WordsRemoved);
        Assert.Equal(4, commit.Files.Count);
        Assert.Equal(commit.Files.Sum(static file => file.WordsAdded), commit.WordsAdded);
    }

    [Fact]
    public void RejectsOutputThatIsNotACommit() {
        Assert.Throws<FormatException>(() => GitCommitDiff.Parse("fatal: bad revision"));
    }

    private void Write(string relativePath, string text) {
        File.WriteAllText(Path.Combine(_directory, relativePath), text, new UTF8Encoding(false));
    }

    private void Commit(string message) {
        Git("add", "-A");
        Git("commit", "-q", "-m", message);
    }

    private CommitDetails Show(string revision) {
        return GitCommitDiff.Parse(Git(GitCommitDiff.Arguments(revision)));
    }

    private string Git(params string[] arguments) {
        ProcessStartInfo startInfo = new("git") {
            WorkingDirectory = _directory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8
        };
        foreach (string argument in arguments) {
            startInfo.ArgumentList.Add(argument);
        }
        using Process process = Process.Start(startInfo)!;
        Task<string> error = process.StandardError.ReadToEndAsync();
        string output = process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        Assert.True(process.ExitCode == 0, $"git {string.Join(' ', arguments)} failed: {error.Result}");
        return output;
    }

    public void Dispose() {
        if (!Directory.Exists(_directory)) {
            return;
        }
        // Git marks its object files read-only.
        foreach (string file in Directory.EnumerateFiles(_directory, "*", SearchOption.AllDirectories)) {
            File.SetAttributes(file, FileAttributes.Normal);
        }
        Directory.Delete(_directory, recursive: true);
    }
}