using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

using WritingIDE.Core.Model;
using WritingIDE.Sdk;
using WritingIDE.ViewModels;

namespace WritingIDE.Services;

/// <summary>
/// The local REST API: an HTTP server on http://localhost:&lt;port&gt;/api for scripts and other apps. Off by default;
/// listens on localhost only; every request (except the OpenAPI description) needs the token, as
/// "Authorization: Bearer &lt;token&gt;". Requests whose Host is not localhost are refused (DNS rebinding protection).
/// </summary>
public sealed class LocalApiServer(MainViewModel shell) {
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private HttpListener? _listener;
    private CancellationTokenSource? _stop;

    public bool IsRunning => _listener?.IsListening ?? false;

    public string BaseUrl => $"http://localhost:{shell.Settings.Api.Port}/api";

    public string Status { get; private set; } = "Off";

    public static string NewToken() {
        return Convert.ToHexString(RandomNumberGenerator.GetBytes(24)).ToLowerInvariant();
    }

    public bool Start() {
        Stop();
        if (shell.Settings.Api.Token.Length < 16) {
            shell.Settings.Api.Token = NewToken();
            shell.Settings.Save();
        }
        try {
            HttpListener listener = new();
            listener.Prefixes.Add($"http://localhost:{shell.Settings.Api.Port}/");
            listener.Start();
            _listener = listener;
            _stop = new CancellationTokenSource();
            _ = Task.Run(() => AcceptLoopAsync(listener, _stop.Token));
            Status = $"Listening on {BaseUrl}";
            shell.Log($"Local API listening on {BaseUrl} (localhost only; requests need the token).");
            return true;
        }
        catch (Exception exception) when (exception is HttpListenerException or InvalidOperationException) {
            Status = $"Could not start: {exception.Message}";
            shell.Log($"warning: the local API could not start on port {shell.Settings.Api.Port}: {exception.Message}");
            return false;
        }
    }

    public void Stop() {
        _stop?.Cancel();
        try {
            _listener?.Close();
        }
        catch (ObjectDisposedException) {
            // Already closed.
        }
        _listener = null;
        Status = "Off";
    }

    private async Task AcceptLoopAsync(HttpListener listener, CancellationToken cancellationToken) {
        while (!cancellationToken.IsCancellationRequested) {
            HttpListenerContext context;
            try {
                context = await listener.GetContextAsync();
            }
            catch (Exception exception) when (exception is HttpListenerException or ObjectDisposedException or InvalidOperationException) {
                return;
            }
            _ = Task.Run(() => HandleAsync(context), CancellationToken.None);
        }
    }

    private async Task HandleAsync(HttpListenerContext context) {
        HttpListenerRequest request = context.Request;
        HttpListenerResponse response = context.Response;
        try {
            string host = request.Url?.Host ?? "";
            if (!host.Equals("localhost", StringComparison.OrdinalIgnoreCase) && (host != "127.0.0.1") && (host != "[::1]")) {
                await WriteAsync(response, 403, new { error = "Only localhost may call this API." });
                return;
            }
            string path = request.Url!.AbsolutePath.TrimEnd('/');
            if (path.Equals("/api/openapi.json", StringComparison.OrdinalIgnoreCase)) {
                await WriteRawAsync(response, 200, OpenApi(), "application/json");
                return;
            }
            if (!Authorized(request)) {
                await WriteAsync(response, 401, new { error = "Missing or wrong token. Send \"Authorization: Bearer <token>\"; the token is in Tools > Extensions and API." });
                return;
            }
            (int status, object body) = Route(request.HttpMethod, path.Split('/', StringSplitOptions.RemoveEmptyEntries), await ReadBodyAsync(request));
            await WriteAsync(response, status, body);
        }
        catch (KeyNotFoundException exception) {
            await WriteAsync(response, 404, new { error = exception.Message });
        }
        catch (Exception exception) when (exception is InvalidOperationException or JsonException or FormatException or ArgumentException) {
            await WriteAsync(response, 400, new { error = exception.Message });
        }
        catch (Exception exception) {
            await WriteAsync(response, 500, new { error = exception.Message });
        }
    }

    private bool Authorized(HttpListenerRequest request) {
        string? header = request.Headers["Authorization"];
        string token = header is not null && header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) ? header[7..].Trim() : (request.Headers["X-WritingIDE-Token"] ?? "");
        byte[] expected = Encoding.UTF8.GetBytes(shell.Settings.Api.Token);
        byte[] actual = Encoding.UTF8.GetBytes(token);
        return (expected.Length > 0) && CryptographicOperations.FixedTimeEquals(expected, actual);
    }

    /// <summary>Routes /api/... requests. Segments: ["api", resource, id?, sub?].</summary>
    private (int Status, object Body) Route(string method, string[] segments, string body) {
        IProjectApi api = shell.Extensions.ProjectApi;
        if ((segments.Length == 0) || !segments[0].Equals("api", StringComparison.OrdinalIgnoreCase)) {
            throw new KeyNotFoundException("Unknown path. Start at /api.");
        }
        string resource = segments.Length > 1 ? segments[1].ToLowerInvariant() : "";
        if (resource == "") {
            return (200, new {
                name = "WritingIDE local API",
                version = ExtensionManager.SdkVersion.ToString(),
                openApi = $"{BaseUrl}/openapi.json",
                endpoints = new[] { "GET /api/project", "GET /api/chapters", "POST /api/chapters", "GET /api/chapters/{id}", "PUT /api/chapters/{id}/text",
                    "GET /api/chapters/{id}/comments", "POST /api/chapters/{id}/comments", "GET /api/entities", "POST /api/entities", "GET /api/diagnostics", "POST /api/save" }
            });
        }
        if (!api.IsOpen) {
            throw new InvalidOperationException("No project is open in WritingIDE.");
        }
        Guid? id = segments.Length > 2 ? Guid.Parse(segments[2]) : null;
        string sub = segments.Length > 3 ? segments[3].ToLowerInvariant() : "";
        switch (resource, method, id.HasValue, sub) {
            case ("project", "GET", false, ""):
                return (200, new { name = api.Name, author = api.Author, directory = api.DirectoryPath, chapters = api.GetChapters().Count, words = api.GetChapters().Sum(static chapter => chapter.Words) });
            case ("chapters", "GET", false, ""):
                return (200, api.GetChapters());
            case ("chapters", "POST", false, ""): {
                NewChapter request = Deserialize<NewChapter>(body);
                Guid created = api.AddChapter(request.Title ?? "New Chapter", request.Text ?? "", request.BookId);
                return (201, new { id = created });
            }
            case ("chapters", "GET", true, ""): {
                ChapterInfo chapter = api.GetChapters().FirstOrDefault(chapter => chapter.Id == id) ?? throw new KeyNotFoundException($"No chapter with id {id}.");
                return (200, new { chapter, text = api.GetChapterText(id!.Value) });
            }
            case ("chapters", "PUT", true, "text"): {
                string text = body.TrimStart().StartsWith('{') ? Deserialize<TextBody>(body).Text ?? "" : body;
                api.SetChapterText(id!.Value, text);
                return (200, new { ok = true });
            }
            case ("chapters", "GET", true, "comments"):
                return (200, api.GetComments(id!.Value));
            case ("chapters", "POST", true, "comments"): {
                NewComment request = Deserialize<NewComment>(body);
                return (201, new { id = api.AddComment(id!.Value, request.Offset, request.Length, request.Text ?? "") });
            }
            case ("entities", "GET", false, ""):
                return (200, api.GetEntities());
            case ("entities", "POST", false, ""): {
                NewEntity request = Deserialize<NewEntity>(body);
                if (string.IsNullOrWhiteSpace(request.Name)) {
                    throw new ArgumentException("A name is required.");
                }
                return (201, new { id = api.AddEntity(request.Kind, request.Name.Trim(), request.Summary ?? "") });
            }
            case ("diagnostics", "GET", false, ""):
                return (200, api.GetDiagnostics());
            case ("save", "POST", false, ""):
                api.Save();
                return (200, new { ok = true });
            default:
                throw new KeyNotFoundException($"No endpoint for {method} {string.Join('/', segments)}. See /api for the list.");
        }
    }

    private static T Deserialize<T>(string body) {
        return JsonSerializer.Deserialize<T>(body.Length == 0 ? "{}" : body, Json) ?? throw new JsonException("Empty request body.");
    }

    private static async Task<string> ReadBodyAsync(HttpListenerRequest request) {
        if (!request.HasEntityBody) {
            return "";
        }
        using StreamReader reader = new(request.InputStream, request.ContentEncoding);
        return await reader.ReadToEndAsync();
    }

    private static Task WriteAsync(HttpListenerResponse response, int status, object body) {
        return WriteRawAsync(response, status, JsonSerializer.Serialize(body, Json), "application/json");
    }

    private static async Task WriteRawAsync(HttpListenerResponse response, int status, string body, string contentType) {
        try {
            byte[] bytes = Encoding.UTF8.GetBytes(body);
            response.StatusCode = status;
            response.ContentType = contentType + "; charset=utf-8";
            response.ContentLength64 = bytes.Length;
            await response.OutputStream.WriteAsync(bytes);
        }
        finally {
            response.Close();
        }
    }

    private sealed record NewChapter(string? Title, string? Text, Guid? BookId);

    private sealed record TextBody(string? Text);

    private sealed record NewComment(int Offset, int Length, string? Text);

    private sealed record NewEntity(EntityKind Kind, string? Name, string? Summary);

    /// <summary>A compact OpenAPI 3 description, for tools such as Swagger UI or Postman.</summary>
    private string OpenApi() {
        static object Op(string summary, string? body = null) {
            Dictionary<string, object> operation = new() { ["summary"] = summary, ["responses"] = new Dictionary<string, object> { ["200"] = new { description = "OK" } } };
            if (body is not null) {
                operation["requestBody"] = new { content = new Dictionary<string, object> { ["application/json"] = new { schema = new { type = "object", description = body } } } };
            }
            return operation;
        }
        object idParameter = new[] { new { name = "id", @in = "path", required = true, schema = new { type = "string", format = "uuid" } } };
        object spec = new Dictionary<string, object> {
            ["openapi"] = "3.0.3",
            ["info"] = new { title = "WritingIDE local API", version = ExtensionManager.SdkVersion.ToString(), description = "Read and edit the project open in WritingIDE. Send Authorization: Bearer <token>." },
            ["servers"] = new[] { new { url = $"http://localhost:{shell.Settings.Api.Port}" } },
            ["components"] = new { securitySchemes = new { token = new { type = "http", scheme = "bearer" } } },
            ["security"] = new[] { new Dictionary<string, string[]> { ["token"] = [] } },
            ["paths"] = new Dictionary<string, object> {
                ["/api/project"] = new { get = Op("The open project: name, author, chapter count, words") },
                ["/api/chapters"] = new { get = Op("Chapters in reading order"), post = Op("Add a chapter", "{ title, text, bookId? }") },
                ["/api/chapters/{id}"] = new { get = Op("A chapter and its text"), parameters = idParameter },
                ["/api/chapters/{id}/text"] = new { put = Op("Replace a chapter's text (plain text, or { text }); undoable in an open editor", "{ text }"), parameters = idParameter },
                ["/api/chapters/{id}/comments"] = new { get = Op("Comments, bookmarks and plot-thread markers"), post = Op("Add a comment", "{ offset, length, text }"), parameters = idParameter },
                ["/api/entities"] = new { get = Op("Story elements"), post = Op("Add a story element", "{ kind: Character|Place|Item|Faction|Event|Lore, name, summary }") },
                ["/api/diagnostics"] = new { get = Op("The Error List") },
                ["/api/save"] = new { post = Op("Save the project") }
            }
        };
        return JsonSerializer.Serialize(spec, Json);
    }
}