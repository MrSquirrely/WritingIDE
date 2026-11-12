using System.Text.Json.Nodes;

using WritingIDE.Core;
using WritingIDE.Core.Grammar;

namespace WritingIDE.Plugins;

/// <summary>A Harper finding, already mapped to character offsets in the linted text.</summary>
public sealed record HarperLint(int Offset, int Length, string Message, string? Code, IReadOnlyList<TextFix> Fixes);

/// <summary>
/// The Harper plugin: runs Automattic's open-source grammar checker <c>harper-ls</c> locally as a language server
/// (https://github.com/Automattic/harper) and feeds its findings into WritingIDE's grammar engine.
/// </summary>
public sealed class HarperPlugin : ObservableObject, IDisposable {
    public const string RuleId = "HARPER";
    public const string ProjectUrl = "https://github.com/Automattic/harper";
    public static readonly string[] Dialects = ["American", "British", "Australian", "Canadian", "Indian"];

    private static readonly TimeSpan LintTimeout = TimeSpan.FromSeconds(30);

    /// <summary>Quick fixes are fetched for at most this many findings per chunk.</summary>
    private const int MaxFixRequests = 200;

    /// <summary>Paragraph results kept between analyses (cleared when it grows past this).</summary>
    private const int MaxCachedParagraphs = 20000;

    private readonly Dictionary<string, IReadOnlyList<HarperLint>> _cache = new(StringComparer.Ordinal);

    private readonly Lock _lintLock = new();
    private LanguageServerClient? _client;
    private int _documentCounter;

    public HarperPlugin(HarperSettings settings) {
        Settings = settings;
    }

    /// <summary>Raised (on any thread) with log lines for the Output window.</summary>
    public event Action<string>? Log;

    /// <summary>Raised (on any thread) when the server starts or stops.</summary>
    public event Action? StateChanged;

    public HarperSettings Settings { get; }

    public static string InstallDirectory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WritingIDE", "Plugins", "Harper");

    public bool IsRunning => _client?.IsRunning ?? false;

    public string? ServerVersion { get; private set => SetProperty(ref field, value); }

    public string Status { get; private set => SetProperty(ref field, value); } = "Disabled";

    /// <summary>The executable that will be used: the configured path, or one found automatically.</summary>
    public string? ResolveExecutable() {
        if (!string.IsNullOrWhiteSpace(Settings.ExecutablePath)) {
            return File.Exists(Settings.ExecutablePath) ? Settings.ExecutablePath : null;
        }
        return FindExecutable();
    }

    /// <summary>Looks in WritingIDE's plugin folder, on PATH and in Cargo's bin folder.</summary>
    public static string? FindExecutable() {
        List<string> candidates = [];
        if (Directory.Exists(InstallDirectory)) {
            candidates.AddRange(Directory.EnumerateFiles(InstallDirectory, "harper-ls.exe", SearchOption.AllDirectories));
        }
        foreach (string folder in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)) {
            candidates.Add(Path.Combine(folder.Trim('"'), "harper-ls.exe"));
        }
        candidates.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".cargo", "bin", "harper-ls.exe"));
        return candidates.FirstOrDefault(File.Exists);
    }

    public async Task<bool> StartAsync() {
        await StopAsync().ConfigureAwait(false);
        lock (_lintLock) {
            // Results depend on the dialect and Harper version, so a (re)start begins with an empty cache.
            _cache.Clear();
        }
        string? executable = ResolveExecutable();
        if (executable is null) {
            Status = "harper-ls was not found. Install it or choose its location.";
            return false;
        }
        Status = "Starting...";
        try {
            LanguageServerClient client = LanguageServerClient.Start(executable, "--stdio", HandleServerRequest);
            client.Log += line => Log?.Invoke($"[Harper] {line}");
            client.Exited += () => {
                Status = "harper-ls stopped unexpectedly.";
                StateChanged?.Invoke();
            };
            JsonNode? result = await client.RequestAsync("initialize", new JsonObject {
                ["processId"] = Environment.ProcessId,
                ["clientInfo"] = new JsonObject { ["name"] = "WritingIDE" },
                ["rootUri"] = null,
                ["capabilities"] = new JsonObject {
                    ["textDocument"] = new JsonObject {
                        ["publishDiagnostics"] = new JsonObject(),
                        ["codeAction"] = new JsonObject {
                            ["codeActionLiteralSupport"] = new JsonObject {
                                ["codeActionKind"] = new JsonObject { ["valueSet"] = new JsonArray("quickfix", "") }
                            }
                        }
                    },
                    ["workspace"] = new JsonObject { ["configuration"] = true }
                },
                ["initializationOptions"] = CreateConfiguration()
            }, TimeSpan.FromSeconds(15)).ConfigureAwait(false);
            client.Notify("initialized", new JsonObject());
            client.Notify("workspace/didChangeConfiguration", new JsonObject { ["settings"] = CreateConfiguration() });
            _client = client;
            ServerVersion = result?["serverInfo"]?["version"]?.ToString();
            Status = $"Running harper-ls{(ServerVersion is null ? "" : " " + ServerVersion)} ({Settings.Dialect} English)";
            Log?.Invoke($"Harper started: {executable}");
            StateChanged?.Invoke();
            return true;
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException or TimeoutException or System.ComponentModel.Win32Exception) {
            Status = $"Could not start harper-ls: {exception.Message}";
            Log?.Invoke(Status);
            _client?.Dispose();
            _client = null;
            return false;
        }
    }

    public async Task StopAsync() {
        LanguageServerClient? client = _client;
        _client = null;
        ServerVersion = null;
        if (client is not null) {
            await client.ShutdownAsync().ConfigureAwait(false);
            StateChanged?.Invoke();
        }
        Status = Settings.Enabled ? "Stopped" : "Disabled";
    }

    /// <summary>Lints text synchronously (call from a background thread). Returns an empty list when Harper is not running.</summary>
    public IReadOnlyList<HarperLint> Lint(string text, CancellationToken cancellationToken) {
        LanguageServerClient? client = _client;
        if ((client is null) || !client.IsRunning || (text.Length == 0)) {
            return [];
        }
        // harper-ls is single-document-at-a-time from our side; serialise to keep memory and CPU predictable.
        lock (_lintLock) {
            List<HarperLint> lints = [];
            foreach ((int start, string paragraph) in SplitIntoParagraphs(text)) {
                cancellationToken.ThrowIfCancellationRequested();
                if (!_cache.TryGetValue(paragraph, out IReadOnlyList<HarperLint>? paragraphLints)) {
                    paragraphLints = LintAsync(client, paragraph, cancellationToken).GetAwaiter().GetResult();
                    if (_cache.Count > MaxCachedParagraphs) {
                        _cache.Clear();
                    }
                    _cache[paragraph] = paragraphLints;
                }
                foreach (HarperLint lint in paragraphLints) {
                    lints.Add(lint with { Offset = lint.Offset + start });
                }
            }
            return lints;
        }
    }

    /// <summary>
    /// Splits text into its non-blank lines (paragraphs, in prose). harper-ls re-lints the whole document for every
    /// code-action request, so small documents keep quick fixes cheap; and since an edit usually changes one paragraph,
    /// every other paragraph comes from the cache.
    /// </summary>
    internal static IEnumerable<(int Start, string Text)> SplitIntoParagraphs(string text) {
        int start = 0;
        while (start < text.Length) {
            int lineBreak = text.IndexOfAny(['\r', '\n'], start);
            int end = lineBreak < 0 ? text.Length : lineBreak;
            if (end > start) {
                string line = text[start..end];
                if (!string.IsNullOrWhiteSpace(line)) {
                    yield return (start, line);
                }
            }
            start = end + 1;
        }
    }

    private async Task<IReadOnlyList<HarperLint>> LintAsync(LanguageServerClient client, string text, CancellationToken cancellationToken) {
        string uri = new Uri(Path.Combine(Path.GetTempPath(), $"writingide-harper-{Environment.ProcessId}-{Interlocked.Increment(ref _documentCounter)}.txt")).AbsoluteUri;
        Task<JsonArray> diagnosticsTask = client.ExpectDiagnostics(uri);
        try {
            client.Notify("textDocument/didOpen", new JsonObject {
                ["textDocument"] = new JsonObject { ["uri"] = uri, ["languageId"] = "plaintext", ["version"] = 1, ["text"] = text }
            });
            JsonArray diagnostics = await diagnosticsTask.WaitAsync(LintTimeout, cancellationToken).ConfigureAwait(false);
            // harper-ls answers code actions for the finding at a range, so ask once per finding, all in flight at once.
            IEnumerable<Task<JsonNode?>> requests = diagnostics.Take(MaxFixRequests).Select(diagnostic => client.RequestAsync("textDocument/codeAction", new JsonObject {
                ["textDocument"] = new JsonObject { ["uri"] = uri },
                ["range"] = diagnostic?["range"]?.DeepClone(),
                ["context"] = new JsonObject { ["diagnostics"] = new JsonArray(diagnostic?.DeepClone()) }
            }, LintTimeout, cancellationToken));
            JsonArray actions = [];
            foreach (JsonNode? result in await Task.WhenAll(requests).ConfigureAwait(false)) {
                foreach (JsonNode? action in result as JsonArray ?? []) {
                    actions.Add(action?.DeepClone());
                }
            }
            return MapLints(text, uri, diagnostics, actions);
        }
        catch (TimeoutException) {
            Log?.Invoke("Harper did not answer in time; its findings are skipped for this run.");
            return [];
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException or ObjectDisposedException) {
            Log?.Invoke($"Harper error: {exception.Message}");
            return [];
        }
        finally {
            client.ForgetDiagnostics(uri);
            try {
                client.Notify("textDocument/didClose", new JsonObject { ["textDocument"] = new JsonObject { ["uri"] = uri } });
            }
            catch (IOException) {
                // Server gone.
            }
        }
    }

    /// <summary>Converts LSP diagnostics and code actions into offsets and quick fixes.</summary>
    internal static IReadOnlyList<HarperLint> MapLints(string text, string uri, JsonArray diagnostics, JsonArray actions) {
        LineMap lines = new(text);
        List<(int Offset, int Length, string Message, string? Code, List<TextFix> Fixes)> lints = [];
        foreach (JsonNode? diagnostic in diagnostics) {
            if (diagnostic?["range"] is not JsonObject range) {
                continue;
            }
            (int start, int end) = lines.GetSpan(range);
            if (end <= start) {
                continue;
            }
            JsonNode? code = diagnostic["code"];
            lints.Add((start, end - start, diagnostic["message"]?.ToString() ?? "Harper finding.", code?.ToString(), []));
        }
        foreach (JsonNode? action in actions) {
            string title = action?["title"]?.ToString() ?? "";
            JsonArray? edits = action?["edit"]?["changes"]?[uri] as JsonArray
                ?? (action?["edit"]?["documentChanges"] as JsonArray)?.FirstOrDefault()?["edits"] as JsonArray;
            if ((edits is null) || (edits.Count != 1) || (edits[0]?["range"] is not JsonObject editRange)) {
                continue;
            }
            (int editStart, int editEnd) = lines.GetSpan(editRange);
            string newText = edits[0]?["newText"]?.ToString() ?? "";
            for (int i = 0; i < lints.Count; i++) {
                (int offset, int length, _, _, List<TextFix> fixes) = lints[i];
                if ((editStart < offset) || (editEnd > offset + length)) {
                    continue;
                }
                // Express the edit as a replacement of the whole flagged span.
                string span = text.Substring(offset, length);
                string replacement = span[..(editStart - offset)] + newText + span[(editEnd - offset)..];
                if ((replacement != span) && !fixes.Any(fix => fix.Replacement == replacement)) {
                    fixes.Add(new TextFix(title.Length > 0 ? title : TextFix.ReplaceWith(replacement).Title, replacement));
                }
                break;
            }
        }
        return lints.Select(static lint => new HarperLint(lint.Offset, lint.Length, lint.Message, lint.Code, lint.Fixes)).ToList();
    }

    private JsonObject CreateConfiguration() {
        return new JsonObject {
            ["harper-ls"] = new JsonObject {
                ["dialect"] = Settings.Dialect,
                ["diagnosticSeverity"] = "warning",
                // WritingIDE's NW3003 already reports long sentences (as a style message, not a warning).
                ["linters"] = new JsonObject { ["LongSentences"] = false }
            }
        };
    }

    private JsonNode? HandleServerRequest(string method, JsonNode? parameters) {
        if (method == "workspace/configuration") {
            JsonArray result = [];
            int count = (parameters?["items"] as JsonArray)?.Count ?? 1;
            for (int i = 0; i < count; i++) {
                // harper-ls expects the whole object, keyed by "harper-ls".
                result.Add(CreateConfiguration());
            }
            return result;
        }
        return null;
    }

    public void Dispose() {
        _client?.Dispose();
        _client = null;
    }

    /// <summary>LSP positions (line, UTF-16 character) \u2194 string offsets. Lines end at \n, \r\n or \r.</summary>
    internal sealed class LineMap {
        private readonly List<int> _starts = [0];
        private readonly int _length;

        public LineMap(string text) {
            _length = text.Length;
            for (int i = 0; i < text.Length; i++) {
                if (text[i] == '\r') {
                    if ((i + 1 < text.Length) && (text[i + 1] == '\n')) {
                        i++;
                    }
                    _starts.Add(i + 1);
                }
                else if (text[i] == '\n') {
                    _starts.Add(i + 1);
                }
            }
        }

        public int GetOffset(int line, int character) {
            if (line >= _starts.Count) {
                return _length;
            }
            return Math.Clamp(_starts[Math.Max(0, line)] + character, 0, _length);
        }

        public (int Line, int Character) GetPosition(int offset) {
            int index = _starts.BinarySearch(offset);
            if (index < 0) {
                index = ~index - 1;
            }
            return (index, offset - _starts[index]);
        }

        public (int Start, int End) GetSpan(JsonObject range) {
            int start = GetOffset(range["start"]?["line"]?.GetValue<int>() ?? 0, range["start"]?["character"]?.GetValue<int>() ?? 0);
            int end = GetOffset(range["end"]?["line"]?.GetValue<int>() ?? 0, range["end"]?["character"]?.GetValue<int>() ?? 0);
            return (start, Math.Max(start, end));
        }
    }
}

/// <summary>The grammar rule that surfaces Harper's findings in the editor and Error List.</summary>
public sealed class HarperRule(HarperPlugin plugin) : GrammarRule(HarperPlugin.RuleId, "Harper grammar checker (plugin)", "Harper", DiagnosticSeverity.Warning,
    "Findings from Harper, Automattic's open-source grammar checker, running locally as a plugin. Story element names and dictionary words are never reported as misspelled.") {
    public override bool IsPlugin => true;

    public override IEnumerable<GrammarDiagnostic> Analyze(AnalysisContext context) {
        foreach (HarperLint lint in plugin.Lint(context.Text, context.CancellationToken)) {
            string flagged = context.Text.Substring(lint.Offset, lint.Length).Trim();
            // Harper does not know the project's characters and places; WritingIDE does.
            if ((flagged.Length > 0) && !flagged.Contains(' ') && context.IsKnownWord(flagged)) {
                continue;
            }
            bool spelling = lint.Message.Contains("spell", StringComparison.OrdinalIgnoreCase) || (lint.Code?.Contains("Spell", StringComparison.OrdinalIgnoreCase) ?? false);
            // Single letters ("an S-rank", "Plan B") are deliberate in fiction; don't call them misspellings.
            if (spelling && (flagged.Count(char.IsLetter) <= 1)) {
                continue;
            }
            yield return new GrammarDiagnostic(Id, spelling ? DiagnosticSeverity.Error : DiagnosticSeverity.Warning, lint.Code is null ? Category : $"Harper \u00B7 {lint.Code}",
                lint.Offset, lint.Length, lint.Message) {
                Fixes = lint.Fixes,
                Word = spelling && !flagged.Contains(' ') ? flagged : null
            };
        }
    }
}