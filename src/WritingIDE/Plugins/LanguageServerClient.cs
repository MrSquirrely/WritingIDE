using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace WritingIDE.Plugins;

/// <summary>
/// A minimal Language Server Protocol client over stdio: requests, notifications, server requests and
/// published diagnostics. Enough to drive harper-ls; it knows nothing Harper-specific.
/// </summary>
public sealed class LanguageServerClient : IDisposable {
    private readonly Process _process;
    private readonly Stream _input;
    private readonly Stream _output;
    private readonly Lock _writeLock = new();
    private readonly ConcurrentDictionary<int, TaskCompletionSource<JsonNode?>> _pending = new();
    private readonly ConcurrentDictionary<string, TaskCompletionSource<JsonArray>> _diagnosticWaiters = new();
    private readonly Func<string, JsonNode?, JsonNode?> _handleServerRequest;
    private int _nextId;
    private bool _disposed;

    private LanguageServerClient(Process process, Func<string, JsonNode?, JsonNode?> handleServerRequest) {
        _process = process;
        _input = process.StandardInput.BaseStream;
        _output = process.StandardOutput.BaseStream;
        _handleServerRequest = handleServerRequest;
    }

    /// <summary>Server log lines (stderr and window/logMessage).</summary>
    public event Action<string>? Log;

    /// <summary>Raised when the server process exits unexpectedly.</summary>
    public event Action? Exited;

    public bool IsRunning => !_disposed && !_process.HasExited;

    /// <param name="handleServerRequest">Answers requests the server sends to the client (method, params) \u2192 result.</param>
    public static LanguageServerClient Start(string executable, string arguments, Func<string, JsonNode?, JsonNode?> handleServerRequest) {
        ProcessStartInfo startInfo = new(executable, arguments) {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = Path.GetDirectoryName(executable) ?? Environment.CurrentDirectory
        };
        Process process = Process.Start(startInfo) ?? throw new InvalidOperationException($"Could not start '{executable}'.");
        process.EnableRaisingEvents = true;
        LanguageServerClient client = new(process, handleServerRequest);
        process.Exited += (_, _) => {
            client.FailAll(new IOException("The language server exited."));
            if (!client._disposed) {
                client.Exited?.Invoke();
            }
        };
        process.ErrorDataReceived += (_, e) => {
            if (!string.IsNullOrWhiteSpace(e.Data)) {
                client.Log?.Invoke(e.Data);
            }
        };
        process.BeginErrorReadLine();
        Thread reader = new(client.ReadLoop) { IsBackground = true, Name = "LSP reader" };
        reader.Start();
        return client;
    }

    public async Task<JsonNode?> RequestAsync(string method, JsonNode? parameters, TimeSpan timeout, CancellationToken cancellationToken = default) {
        int id = Interlocked.Increment(ref _nextId);
        TaskCompletionSource<JsonNode?> completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[id] = completion;
        Send(new JsonObject { ["jsonrpc"] = "2.0", ["id"] = id, ["method"] = method, ["params"] = parameters });
        try {
            return await completion.Task.WaitAsync(timeout, cancellationToken).ConfigureAwait(false);
        }
        finally {
            _pending.TryRemove(id, out _);
        }
    }

    public void Notify(string method, JsonNode? parameters) {
        Send(new JsonObject { ["jsonrpc"] = "2.0", ["method"] = method, ["params"] = parameters });
    }

    /// <summary>Registers interest in the next textDocument/publishDiagnostics for <paramref name="uri"/>. Call before opening the document.</summary>
    public Task<JsonArray> ExpectDiagnostics(string uri) {
        TaskCompletionSource<JsonArray> completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        _diagnosticWaiters[uri] = completion;
        return completion.Task;
    }

    public void ForgetDiagnostics(string uri) {
        _diagnosticWaiters.TryRemove(uri, out _);
    }

    private void Send(JsonObject message) {
        byte[] body = Encoding.UTF8.GetBytes(message.ToJsonString());
        byte[] header = Encoding.ASCII.GetBytes($"Content-Length: {body.Length}\r\n\r\n");
        lock (_writeLock) {
            if (!IsRunning) {
                throw new IOException("The language server is not running.");
            }
            _input.Write(header);
            _input.Write(body);
            _input.Flush();
        }
    }

    private void ReadLoop() {
        try {
            while (true) {
                int contentLength = ReadHeaders();
                if (contentLength < 0) {
                    break;
                }
                byte[] body = new byte[contentLength];
                int read = 0;
                while (read < contentLength) {
                    int chunk = _output.Read(body, read, contentLength - read);
                    if (chunk == 0) {
                        return;
                    }
                    read += chunk;
                }
                Dispatch(JsonNode.Parse(body));
            }
        }
        catch (Exception exception) when (exception is IOException or ObjectDisposedException or JsonException) {
            FailAll(exception);
        }
    }

    /// <summary>Reads "Header: value" lines up to the blank line. Returns Content-Length, or -1 at end of stream.</summary>
    private int ReadHeaders() {
        int contentLength = 0;
        StringBuilder line = new();
        while (true) {
            int next = _output.ReadByte();
            if (next < 0) {
                return -1;
            }
            if (next == '\n') {
                string header = line.ToString().TrimEnd('\r');
                line.Clear();
                if (header.Length == 0) {
                    return contentLength;
                }
                int colon = header.IndexOf(':');
                if ((colon > 0) && header[..colon].Equals("Content-Length", StringComparison.OrdinalIgnoreCase)) {
                    contentLength = int.Parse(header[(colon + 1)..].Trim(), System.Globalization.CultureInfo.InvariantCulture);
                }
                continue;
            }
            line.Append((char)next);
        }
    }

    private void Dispatch(JsonNode? message) {
        if (message is not JsonObject json) {
            return;
        }
        string? method = json["method"]?.GetValue<string>();
        JsonNode? idNode = json["id"];
        if (method is null) {
            if ((idNode is not null) && int.TryParse(idNode.ToString(), out int id) && _pending.TryGetValue(id, out TaskCompletionSource<JsonNode?>? completion)) {
                if (json["error"] is JsonNode error) {
                    completion.TrySetException(new InvalidOperationException(error["message"]?.ToString() ?? "Language server error."));
                }
                else {
                    completion.TrySetResult(json["result"]?.DeepClone());
                }
            }
            return;
        }
        if (idNode is not null) {
            // A request from the server (workspace/configuration, client/registerCapability...): always answer.
            JsonNode? result = null;
            try {
                result = _handleServerRequest(method, json["params"]);
            }
            catch (Exception exception) {
                Log?.Invoke($"Error answering {method}: {exception.Message}");
            }
            try {
                Send(new JsonObject { ["jsonrpc"] = "2.0", ["id"] = idNode.DeepClone(), ["result"] = result });
            }
            catch (IOException) {
                // The server is going away.
            }
            return;
        }
        switch (method) {
            case "textDocument/publishDiagnostics":
                string? uri = json["params"]?["uri"]?.GetValue<string>();
                if ((uri is not null) && _diagnosticWaiters.TryRemove(uri, out TaskCompletionSource<JsonArray>? waiter)) {
                    waiter.TrySetResult(json["params"]?["diagnostics"]?.DeepClone() as JsonArray ?? []);
                }
                break;
            case "window/logMessage":
            case "window/showMessage":
                Log?.Invoke(json["params"]?["message"]?.ToString() ?? "");
                break;
        }
    }

    private void FailAll(Exception exception) {
        foreach (TaskCompletionSource<JsonNode?> completion in _pending.Values) {
            completion.TrySetException(exception);
        }
        foreach (TaskCompletionSource<JsonArray> waiter in _diagnosticWaiters.Values) {
            waiter.TrySetException(exception);
        }
    }

    /// <summary>Polite LSP shutdown, then kills the process if it lingers.</summary>
    public async Task ShutdownAsync() {
        if (IsRunning) {
            try {
                await RequestAsync("shutdown", null, TimeSpan.FromSeconds(2)).ConfigureAwait(false);
                Notify("exit", null);
                await _process.WaitForExitAsync(new CancellationTokenSource(TimeSpan.FromSeconds(2)).Token).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is IOException or TimeoutException or OperationCanceledException or InvalidOperationException) {
                // Fall through to Dispose, which kills it.
            }
        }
        Dispose();
    }

    public void Dispose() {
        if (_disposed) {
            return;
        }
        _disposed = true;
        try {
            if (!_process.HasExited) {
                _process.Kill(entireProcessTree: true);
            }
        }
        catch (InvalidOperationException) {
            // Already gone.
        }
        _process.Dispose();
        FailAll(new ObjectDisposedException(nameof(LanguageServerClient)));
    }
}