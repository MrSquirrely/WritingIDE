using WritingIDE.Core.Analysis;
using WritingIDE.Core.Text;

namespace WritingIDE.Core.Grammar;

/// <summary>Options that shape an analysis run: known names, the custom dictionary and disabled rules.</summary>
public sealed record GrammarOptions(EntityIndex Entities, IReadOnlySet<string> CustomDictionary, IReadOnlySet<string> DisabledRules) {
    /// <summary>False to skip plugin rules (used by quiet background analyses so slow plugins never delay the editor).</summary>
    public bool RunPlugins { get; init; } = true;

    public static GrammarOptions Default { get; } = new(EntityIndex.Empty, new HashSet<string>(), new HashSet<string>());
}

/// <summary>Everything a rule needs to analyse one document. Tokens and sentences are computed once and shared.</summary>
public sealed class AnalysisContext {
    private readonly HashSet<string> _knownWords;

    public AnalysisContext(string text, GrammarOptions options, ISpellingProvider? spelling, CancellationToken cancellationToken = default) {
        Text = text;
        Options = options;
        Spelling = spelling is { IsAvailable: true } ? spelling : null;
        CancellationToken = cancellationToken;
        Tokens = Tokenizer.Tokenize(text);
        Sentences = SentenceSplitter.Split(Tokens);
        _knownWords = new HashSet<string>(options.CustomDictionary, StringComparer.OrdinalIgnoreCase);
        foreach (string name in options.Entities.Names.Select(static entry => entry.Name)) {
            _knownWords.Add(name);
            foreach (string part in name.Split([' ', '-'], StringSplitOptions.RemoveEmptyEntries)) {
                _knownWords.Add(part);
            }
        }
    }

    public string Text { get; }

    public GrammarOptions Options { get; }

    public ISpellingProvider? Spelling { get; }

    public CancellationToken CancellationToken { get; }

    public IReadOnlyList<Token> Tokens { get; }

    public IReadOnlyList<Sentence> Sentences { get; }

    /// <summary>True if the word is a story element name or is in the project dictionary.</summary>
    public bool IsKnownWord(string word) {
        return _knownWords.Contains(word) || _knownWords.Contains(Tokenizer.StripPossessive(word));
    }
}