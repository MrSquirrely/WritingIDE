using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace WritingIDE.Core.Ai;

/// <summary>A note an AI made on a passage: the exact quote it refers to, and what it says.</summary>
public sealed record AiNote(string Quote, string Comment);

/// <summary>A note placed in the chapter: where its quote was found.</summary>
public sealed record AnchoredNote(AiNote Note, int Offset, int Length);

public enum RewriteKind {
    Tighten,
    Vivid,
    FixGrammar,
    Simplify,
    Custom
}

/// <summary>The prompts behind WritingIDE's AI tools, and parsing of what comes back.</summary>
public static partial class AiTools {
    private const string Persona = "You are an experienced fiction editor helping a novelist. Be specific and honest, keep the author's voice, and never invent facts about their story.";

    public static IReadOnlyList<AiMessage> Summarize(string chapterTitle, string text) {
        return [
            AiMessage.System(Persona),
            AiMessage.User($"Write a synopsis of this chapter in 2 to 4 sentences, present tense, naming who does what and what changes. Reply with the synopsis only.\n\nChapter: {chapterTitle}\n\n{text}")
        ];
    }

    public static IReadOnlyList<AiMessage> Critique(string chapterTitle, string text, string context) {
        return [
            AiMessage.System(Persona),
            AiMessage.User(
                "Read this chapter as a beta reader. Note up to 12 specific places worth revising: unclear moments, pacing problems, clunky sentences, "
                + "weak dialogue, missed emotional beats, and also what works especially well. "
                + NotesFormat
                + (context.Length > 0 ? $"\n\nAbout the book:\n{context}" : "")
                + $"\n\nChapter: {chapterTitle}\n\n{text}")
        ];
    }

    public static IReadOnlyList<AiMessage> ContinuityCheck(string chapterTitle, string text, string bible) {
        return [
            AiMessage.System(Persona),
            AiMessage.User(
                "Check this chapter against the story bible and story elements below. Report only contradictions: names, ages, appearance, "
                + "relationships, places, timeline, who knows what, objects, rules of the world. If there are none, reply with []. "
                + NotesFormat
                + $"\n\nStory bible and elements:\n{bible}\n\nChapter: {chapterTitle}\n\n{text}")
        ];
    }

    public static IReadOnlyList<AiMessage> Rewrite(string passage, RewriteKind kind, string customInstruction, string surrounding) {
        string instruction = kind switch {
            RewriteKind.Tighten => "Tighten this passage: cut filler and redundancy, keep every story beat and the voice.",
            RewriteKind.Vivid => "Make this passage more vivid and specific: concrete sensory detail and stronger verbs, same events, same voice, similar length.",
            RewriteKind.FixGrammar => "Correct grammar, spelling and punctuation only. Change nothing else.",
            RewriteKind.Simplify => "Make this passage simpler and clearer to read, keeping the meaning and voice.",
            _ => customInstruction.Trim()
        };
        return [
            AiMessage.System(Persona + " When asked to rewrite, reply with the rewritten passage only: no preamble, no quotes, no notes."),
            AiMessage.User($"{instruction}\n\n" + (surrounding.Length > 0 ? $"Context around it (do not rewrite this):\n{surrounding}\n\n" : "") + $"Passage:\n{passage}")
        ];
    }

    /// <summary>The first message of an "Ask about your book" chat: the book's context, then the question.</summary>
    public static IReadOnlyList<AiMessage> Ask(string context, IReadOnlyList<AiMessage> history, string question) {
        List<AiMessage> messages = [
            AiMessage.System(Persona + " Answer questions about the author's book using only the material provided. Cite chapters by title. If the material does not say, say so."),
            AiMessage.User($"Here is my book:\n\n{context}"),
            AiMessage.Assistant("Thanks, I have read it. What would you like to know?")
        ];
        messages.AddRange(history);
        messages.Add(AiMessage.User(question));
        return messages;
    }

    private const string NotesFormat = "Reply with a JSON array only, no other text: [{\"quote\": \"exact words copied from the chapter (5 to 15 words)\", \"comment\": \"your note\"}]. "
        + "The quote must appear in the chapter exactly as written so the note can be attached to it.";

    /// <summary>Reads notes from a reply: a JSON array (possibly inside a ```json fence or surrounded by chatter).</summary>
    public static IReadOnlyList<AiNote> ParseNotes(string reply) {
        int start = reply.IndexOf('[');
        int end = reply.LastIndexOf(']');
        if ((start < 0) || (end <= start)) {
            return [];
        }
        try {
            JsonArray? array = JsonNode.Parse(reply[start..(end + 1)])?.AsArray();
            return array?.Select(static item => new AiNote(
                    item?["quote"]?.GetValue<string>()?.Trim() ?? "",
                    item?["comment"]?.GetValue<string>()?.Trim() ?? ""))
                .Where(static note => note.Comment.Length > 0)
                .ToList() ?? [];
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException) {
            return [];
        }
    }

    /// <summary>
    /// Finds each note's quote in the text (exactly, then ignoring case, then ignoring differences in whitespace and
    /// quote marks). Notes whose quote cannot be found are anchored at the start of the chapter.
    /// </summary>
    public static IReadOnlyList<AnchoredNote> Anchor(string text, IReadOnlyList<AiNote> notes) {
        List<AnchoredNote> anchored = [];
        foreach (AiNote note in notes) {
            string quote = note.Quote.Trim('"', '\u201C', '\u201D', '.', ' ');
            int index = quote.Length == 0 ? -1 : text.IndexOf(quote, StringComparison.Ordinal);
            int length = quote.Length;
            if ((index < 0) && (quote.Length > 0)) {
                index = text.IndexOf(quote, StringComparison.OrdinalIgnoreCase);
            }
            if ((index < 0) && (quote.Length > 0)) {
                string pattern = string.Join(@"[\s\u00A0]+", quote.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Select(static word => QuoteChars().Replace(Regex.Escape(word), "[\"'\u2018\u2019\u201C\u201D]")));
                Match match = Regex.Match(text, pattern, RegexOptions.IgnoreCase);
                if (match.Success) {
                    index = match.Index;
                    length = match.Length;
                }
            }
            anchored.Add(index < 0 ? new AnchoredNote(note, 0, 0) : new AnchoredNote(note, index, length));
        }
        return anchored;
    }

    /// <summary>Strips quotes or a code fence an AI may wrap around a rewrite.</summary>
    public static string CleanRewrite(string reply) {
        string text = reply.Trim();
        Match fence = Fence().Match(text);
        if (fence.Success) {
            text = fence.Groups["body"].Value.Trim();
        }
        if ((text.Length > 1) && (((text[0] == '"') && (text[^1] == '"')) || ((text[0] == '\u201C') && (text[^1] == '\u201D'))) && (text.Count(static c => c is '"' or '\u201C' or '\u201D') == 2)) {
            text = text[1..^1];
        }
        return text;
    }

    [GeneratedRegex("[\"'\u2018\u2019\u201C\u201D]")]
    private static partial Regex QuoteChars();

    [GeneratedRegex(@"^```[a-zA-Z]*\s*\n(?<body>.*?)\n```$", RegexOptions.Singleline)]
    private static partial Regex Fence();
}