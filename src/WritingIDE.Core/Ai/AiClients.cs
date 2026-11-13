using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace WritingIDE.Core.Ai;

public enum AiProviderKind {
    Gemini,
    Groq,
    OpenRouter,
    Ollama,

    /// <summary>Any server that speaks the OpenAI chat completions API (LM Studio, llama.cpp, a proxy).</summary>
    OpenAiCompatible
}

/// <summary>What WritingIDE knows about a provider: where it lives, a good free model, and where to get a key.</summary>
public sealed record AiProviderInfo(AiProviderKind Kind, string DisplayName, string Endpoint, string DefaultModel, bool NeedsKey, string KeyUrl, string Notes) {
    public static IReadOnlyList<AiProviderInfo> All { get; } = [
        new(AiProviderKind.Gemini, "Google Gemini", "https://generativelanguage.googleapis.com/v1beta", "gemini-2.5-flash", true,
            "https://aistudio.google.com/apikey", "Free tier with a very large context window: whole books fit. Get a key in Google AI Studio."),
        new(AiProviderKind.Groq, "Groq", "https://api.groq.com/openai/v1", "llama-3.3-70b-versatile", true,
            "https://console.groq.com/keys", "Free tier, very fast. Smaller context: works chapter by chapter."),
        new(AiProviderKind.OpenRouter, "OpenRouter (free models)", "https://openrouter.ai/api/v1", "meta-llama/llama-3.3-70b-instruct:free", true,
            "https://openrouter.ai/keys", "One key for many models; those ending in \":free\" cost nothing (rate limited)."),
        new(AiProviderKind.Ollama, "Ollama (on this PC)", "http://localhost:11434/v1", "llama3.2", false,
            "https://ollama.com/download", "Runs on your computer: free and private, no key. Install Ollama, then run \"ollama pull llama3.2\"."),
        new(AiProviderKind.OpenAiCompatible, "Other (OpenAI-compatible)", "http://localhost:1234/v1", "", false,
            "", "Any server with an OpenAI-style /chat/completions endpoint, such as LM Studio.")
    ];

    public static AiProviderInfo Get(AiProviderKind kind) {
        return All.First(info => info.Kind == kind);
    }
}

/// <summary>A configured provider: endpoint, model and key.</summary>
public sealed record AiConnection(AiProviderKind Kind, string Endpoint, string Model, string ApiKey);

/// <summary>A chat message: role is "system", "user" or "assistant".</summary>
public sealed record AiMessage(string Role, string Content) {
    public static AiMessage System(string content) {
        return new("system", content);
    }

    public static AiMessage User(string content) {
        return new("user", content);
    }

    public static AiMessage Assistant(string content) {
        return new("assistant", content);
    }
}

/// <summary>A provider error with a message fit to show the writer.</summary>
public sealed class AiException(string message, Exception? inner = null) : Exception(message, inner);

public interface IAiClient {
    Task<string> CompleteAsync(IReadOnlyList<AiMessage> messages, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<string>> ListModelsAsync(CancellationToken cancellationToken = default);
}

public static class AiClientFactory {
    public static IAiClient Create(AiConnection connection, HttpClient http) {
        if (AiProviderInfo.Get(connection.Kind).NeedsKey && string.IsNullOrWhiteSpace(connection.ApiKey)) {
            throw new AiException($"{AiProviderInfo.Get(connection.Kind).DisplayName} needs an API key. Add one in AI > AI Settings.");
        }
        return connection.Kind == AiProviderKind.Gemini ? new GeminiClient(http, connection) : new OpenAiCompatibleClient(http, connection);
    }
}

/// <summary>OpenAI-style chat completions: Groq, OpenRouter, Ollama and compatible servers.</summary>
public sealed class OpenAiCompatibleClient(HttpClient http, AiConnection connection) : IAiClient {
    public async Task<string> CompleteAsync(IReadOnlyList<AiMessage> messages, CancellationToken cancellationToken = default) {
        JsonObject body = new() {
            ["model"] = connection.Model,
            ["temperature"] = 0.4,
            ["messages"] = new JsonArray(messages.Select(static message => (JsonNode)new JsonObject { ["role"] = message.Role, ["content"] = message.Content }).ToArray())
        };
        using HttpRequestMessage request = Request(HttpMethod.Post, "chat/completions");
        request.Content = JsonContent.Create(body);
        JsonNode? json = await AiHttp.SendAsync(http, request, connection.Kind, cancellationToken);
        string? text = json?["choices"]?[0]?["message"]?["content"]?.GetValue<string>();
        return text ?? throw new AiException("The AI returned an empty answer.");
    }

    public async Task<IReadOnlyList<string>> ListModelsAsync(CancellationToken cancellationToken = default) {
        using HttpRequestMessage request = Request(HttpMethod.Get, "models");
        JsonNode? json = await AiHttp.SendAsync(http, request, connection.Kind, cancellationToken);
        return json?["data"]?.AsArray().Select(static model => model?["id"]?.GetValue<string>()).OfType<string>().Order(StringComparer.OrdinalIgnoreCase).ToList() ?? [];
    }

    private HttpRequestMessage Request(HttpMethod method, string path) {
        HttpRequestMessage request = new(method, connection.Endpoint.TrimEnd('/') + "/" + path);
        if (!string.IsNullOrWhiteSpace(connection.ApiKey)) {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", connection.ApiKey.Trim());
        }
        if (connection.Kind == AiProviderKind.OpenRouter) {
            request.Headers.Add("X-Title", "WritingIDE");
        }
        return request;
    }
}

/// <summary>Google Gemini's generateContent API.</summary>
public sealed class GeminiClient(HttpClient http, AiConnection connection) : IAiClient {
    public async Task<string> CompleteAsync(IReadOnlyList<AiMessage> messages, CancellationToken cancellationToken = default) {
        string system = string.Join("\n\n", messages.Where(static message => message.Role == "system").Select(static message => message.Content));
        JsonObject body = new() {
            ["contents"] = new JsonArray(messages.Where(static message => message.Role != "system").Select(static message => (JsonNode)new JsonObject {
                ["role"] = message.Role == "assistant" ? "model" : "user",
                ["parts"] = new JsonArray(new JsonObject { ["text"] = message.Content })
            }).ToArray()),
            ["generationConfig"] = new JsonObject { ["temperature"] = 0.4 }
        };
        if (system.Length > 0) {
            body["systemInstruction"] = new JsonObject { ["parts"] = new JsonArray(new JsonObject { ["text"] = system }) };
        }
        string model = connection.Model.StartsWith("models/", StringComparison.Ordinal) ? connection.Model : "models/" + connection.Model;
        using HttpRequestMessage request = Request(HttpMethod.Post, $"{model}:generateContent");
        request.Content = JsonContent.Create(body);
        JsonNode? json = await AiHttp.SendAsync(http, request, connection.Kind, cancellationToken);
        JsonArray? parts = json?["candidates"]?[0]?["content"]?["parts"]?.AsArray();
        string text = string.Concat(parts?.Select(static part => part?["text"]?.GetValue<string>() ?? "") ?? []);
        if (text.Length == 0) {
            string? reason = json?["candidates"]?[0]?["finishReason"]?.GetValue<string>() ?? json?["promptFeedback"]?["blockReason"]?.GetValue<string>();
            throw new AiException(reason is null ? "Gemini returned an empty answer." : $"Gemini returned no text ({reason}).");
        }
        return text;
    }

    public async Task<IReadOnlyList<string>> ListModelsAsync(CancellationToken cancellationToken = default) {
        using HttpRequestMessage request = Request(HttpMethod.Get, "models?pageSize=200");
        JsonNode? json = await AiHttp.SendAsync(http, request, connection.Kind, cancellationToken);
        return json?["models"]?.AsArray()
            .Where(static model => model?["supportedGenerationMethods"]?.AsArray().Any(static method => method?.GetValue<string>() == "generateContent") ?? false)
            .Select(static model => model?["name"]?.GetValue<string>()?.Replace("models/", "", StringComparison.Ordinal))
            .OfType<string>()
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToList() ?? [];
    }

    private HttpRequestMessage Request(HttpMethod method, string path) {
        HttpRequestMessage request = new(method, connection.Endpoint.TrimEnd('/') + "/" + path);
        // The key goes in a header, never in the URL.
        request.Headers.Add("x-goog-api-key", connection.ApiKey.Trim());
        return request;
    }
}

internal static class AiHttp {
    public static async Task<JsonNode?> SendAsync(HttpClient http, HttpRequestMessage request, AiProviderKind kind, CancellationToken cancellationToken) {
        string name = AiProviderInfo.Get(kind).DisplayName;
        HttpResponseMessage response;
        try {
            response = await http.SendAsync(request, cancellationToken);
        }
        catch (HttpRequestException exception) {
            throw new AiException(kind == AiProviderKind.Ollama
                ? $"Could not reach Ollama at {request.RequestUri?.GetLeftPart(UriPartial.Authority)}. Is it installed and running?"
                : $"Could not reach {name}: {exception.Message}", exception);
        }
        catch (TaskCanceledException exception) when (!cancellationToken.IsCancellationRequested) {
            throw new AiException($"{name} did not answer in time.", exception);
        }
        using (response) {
            string content = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode) {
                string detail = ExtractError(content);
                throw new AiException((int)response.StatusCode switch {
                    401 or 403 => $"{name} rejected the API key ({detail}).",
                    404 => $"{name} does not know that model or endpoint ({detail}).",
                    429 => $"{name}'s free-tier limit was reached; wait a minute and try again ({detail}).",
                    _ => $"{name} returned an error {(int)response.StatusCode}: {detail}"
                });
            }
            try {
                return JsonNode.Parse(content);
            }
            catch (JsonException exception) {
                throw new AiException($"{name} sent a reply WritingIDE could not read.", exception);
            }
        }
    }

    private static string ExtractError(string content) {
        try {
            JsonNode? json = JsonNode.Parse(content);
            JsonNode? error = json?["error"];
            string? message = error is JsonObject ? error["message"]?.GetValue<string>() : error?.GetValue<string>();
            if (!string.IsNullOrWhiteSpace(message)) {
                return message.Trim();
            }
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException) {
            // Not JSON: fall through to the raw text.
        }
        string raw = content.Trim();
        return raw.Length <= 200 ? raw : raw[..200] + "...";
    }
}