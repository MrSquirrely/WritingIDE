using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;

namespace WritingIDE.Services;

public sealed record GitResult(int ExitCode, string Output, string Error) {
    public bool Success => ExitCode == 0;

    /// <summary>The most useful line to show: git's "fatal:"/"error:" line when it failed, else the last line of output.</summary>
    public string Message {
        get {
            string[] lines = (Error.Trim().Length > 0 ? Error : Output).Split('\n').Select(static line => line.Trim()).Where(static line => line.Length > 0).ToArray();
            string? problem = lines.FirstOrDefault(static line => line.StartsWith("fatal:", StringComparison.OrdinalIgnoreCase) || line.StartsWith("error:", StringComparison.OrdinalIgnoreCase));
            return problem ?? lines.LastOrDefault() ?? $"git exited with code {ExitCode}.";
        }
    }
}

/// <summary>A changed file from "git status". <see cref="Status"/> is the two-letter porcelain code ("M ", "??", "R ").</summary>
public sealed record GitFileChange(string Status, string Path, string? OriginalPath);

public sealed record GitStatusInfo(string Branch, string? Upstream, int Ahead, int Behind, IReadOnlyList<GitFileChange> Changes);

public sealed record GitCommitInfo(string Hash, string Author, string When, string Subject);

/// <summary>Runs the git command line in a working directory. Requires Git for Windows on PATH.</summary>
public sealed partial class GitService(string workingDirectory) {
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan NetworkTimeout = TimeSpan.FromMinutes(3);

    public string WorkingDirectory { get; } = workingDirectory;

    /// <summary>True when "git --version" runs.</summary>
    public static async Task<bool> IsGitInstalledAsync() {
        try {
            GitResult result = await new GitService(Environment.CurrentDirectory).RunAsync(DefaultTimeout, "--version");
            return result.Success;
        }
        catch (System.ComponentModel.Win32Exception) {
            return false;
        }
    }

    /// <summary>The repository root containing the working directory, or null when it is not in a repository.</summary>
    public async Task<string?> GetRepositoryRootAsync() {
        GitResult result = await RunAsync(DefaultTimeout, "rev-parse", "--show-toplevel");
        return result.Success ? Path.GetFullPath(result.Output.Trim()) : null;
    }

    public Task<GitResult> InitAsync() {
        return RunAsync(DefaultTimeout, "init", "-b", "main");
    }

    public async Task<GitStatusInfo> GetStatusAsync() {
        GitResult result = await RunAsync(DefaultTimeout, "-c", "core.quotepath=off", "status", "--porcelain=v1", "-b", "--untracked-files=all");
        if (!result.Success) {
            throw new InvalidOperationException(result.Message);
        }
        string branch = "";
        string? upstream = null;
        int ahead = 0;
        int behind = 0;
        List<GitFileChange> changes = [];
        foreach (string rawLine in result.Output.Split('\n')) {
            string line = rawLine.TrimEnd('\r');
            if (line.StartsWith("## ", StringComparison.Ordinal)) {
                Match header = BranchHeader().Match(line);
                branch = header.Groups["branch"].Value;
                upstream = header.Groups["upstream"].Success ? header.Groups["upstream"].Value : null;
                ahead = header.Groups["ahead"].Success ? int.Parse(header.Groups["ahead"].Value) : 0;
                behind = header.Groups["behind"].Success ? int.Parse(header.Groups["behind"].Value) : 0;
                continue;
            }
            if (line.Length < 4) {
                continue;
            }
            string status = line[..2];
            string path = line[3..];
            string? original = null;
            int arrow = path.IndexOf(" -> ", StringComparison.Ordinal);
            if (arrow >= 0) {
                original = path[..arrow];
                path = path[(arrow + 4)..];
            }
            changes.Add(new GitFileChange(status, path.Trim('"'), original?.Trim('"')));
        }
        return new GitStatusInfo(branch, upstream, ahead, behind, changes);
    }

    /// <summary>Stages everything and commits.</summary>
    public async Task<GitResult> CommitAllAsync(string message) {
        GitResult add = await RunAsync(DefaultTimeout, "add", "-A");
        if (!add.Success) {
            return add;
        }
        return await RunAsync(DefaultTimeout, "commit", "-m", message);
    }

    /// <summary>Pushes; the first push of a branch sets its upstream on "origin".</summary>
    public async Task<GitResult> PushAsync(string branch, bool hasUpstream) {
        return hasUpstream
            ? await RunAsync(NetworkTimeout, "push")
            : await RunAsync(NetworkTimeout, "push", "-u", "origin", branch);
    }

    public Task<GitResult> PullAsync() {
        return RunAsync(NetworkTimeout, "pull", "--ff-only");
    }

    public async Task<bool> HasRemoteAsync() {
        GitResult result = await RunAsync(DefaultTimeout, "remote");
        return result.Success && (result.Output.Trim().Length > 0);
    }

    public Task<GitResult> AddRemoteAsync(string url) {
        return RunAsync(DefaultTimeout, "remote", "add", "origin", url);
    }

    public async Task<IReadOnlyList<GitCommitInfo>> GetHistoryAsync(int count = 50) {
        GitResult result = await RunAsync(DefaultTimeout, "log", $"-n{count}", "--pretty=format:%h%x1f%an%x1f%ar%x1f%s");
        if (!result.Success) {
            return [];
        }
        return result.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(static line => line.TrimEnd('\r').Split('\u001f'))
            .Where(static parts => parts.Length == 4)
            .Select(static parts => new GitCommitInfo(parts[0], parts[1], parts[2], parts[3]))
            .ToList();
    }

    /// <summary>
    /// A word-level diff of a file against the last commit ("[-old-]{+new+}" markers), which reads far better than line
    /// diffs for prose, where a paragraph is one long line.
    /// </summary>
    public async Task<string> GetWordDiffAsync(string repositoryPath) {
        GitResult result = await RunAsync(DefaultTimeout, "-c", "core.quotepath=off", "diff", "HEAD", "--word-diff=plain", "--no-color", "--", repositoryPath);
        if (!result.Success) {
            // No commits yet: compare against nothing.
            result = await RunAsync(DefaultTimeout, "diff", "--cached", "--word-diff=plain", "--no-color", "--", repositoryPath);
        }
        return result.Output;
    }

    public async Task<GitResult> RunAsync(TimeSpan timeout, params string[] arguments) {
        ProcessStartInfo startInfo = new("git") {
            WorkingDirectory = WorkingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };
        foreach (string argument in arguments) {
            startInfo.ArgumentList.Add(argument);
        }
        // Never block on a terminal prompt; Git Credential Manager can still show its own sign-in window.
        startInfo.Environment["GIT_TERMINAL_PROMPT"] = "0";
        using Process process = Process.Start(startInfo) ?? throw new InvalidOperationException("Could not start git.");
        Task<string> output = process.StandardOutput.ReadToEndAsync();
        Task<string> error = process.StandardError.ReadToEndAsync();
        using CancellationTokenSource cancellation = new(timeout);
        try {
            await process.WaitForExitAsync(cancellation.Token);
        }
        catch (OperationCanceledException) {
            process.Kill(entireProcessTree: true);
            return new GitResult(-1, "", $"git {arguments.FirstOrDefault()} timed out after {timeout.TotalSeconds:0} seconds.");
        }
        return new GitResult(process.ExitCode, await output, await error);
    }

    [GeneratedRegex(@"^## (?:No commits yet on )?(?<branch>[^.\s]+(?:\.[^.\s]+)*?)(?:\.\.\.(?<upstream>\S+))?(?: \[(?:ahead (?<ahead>\d+))?(?:, )?(?:behind (?<behind>\d+))?\])?\s*$")]
    private static partial Regex BranchHeader();
}