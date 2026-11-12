using WritingIDE.Core.Analysis;
using WritingIDE.Core.Text;

namespace WritingIDE.Core.Grammar;

/// <summary>Options that shape an analysis run: known names, the custom dictionary and disabled rules.</summary>
public sealed record GrammarOptions(EntityIndex Entities, IReadOnlySet<string> CustomDictionary, IReadOnlySet<string> DisabledRules) {
    /// <summary>False to skip plugin rules (used by quiet background analyses so slow plugins never delay the editor).</summary>
    public bool RunPlugins { get; init; } = true;

    /// <summary>Where the analysed chapter sits in the manuscript; continuity rules about time need it.</summary>
    public ManuscriptPosition? Position { get; init; }

    public static GrammarOptions Default { get; } = new(EntityIndex.Empty, new HashSet<string>(), new HashSet<string>());
}

/// <summary>Everything a rule needs to analyse one document. Tokens and sentences are computed once and shared.</summary>
public sealed class AnalysisContext {
    private readonly HashSet<string> _knownWords;
    private readonly List<string> _nameParts;

    public AnalysisContext(string text, GrammarOptions options, ISpellingProvider? spelling, CancellationToken cancellationToken = default) {
        Text = text;
        Options = options;
        Spelling = spelling is { IsAvailable: true } ? spelling : null;
        CancellationToken = cancellationToken;
        Tokens = Tokenizer.Tokenize(text);
        Sentences = SentenceSplitter.Split(Tokens);
        _knownWords = new HashSet<string>(options.CustomDictionary, StringComparer.OrdinalIgnoreCase);
        HashSet<string> parts = new(StringComparer.Ordinal);
        foreach (string name in options.Entities.Names.Select(static entry => entry.Name)) {
            _knownWords.Add(name);
            foreach (string part in name.Split([' ', '-'], StringSplitOptions.RemoveEmptyEntries)) {
                _knownWords.Add(part);
                if ((part.Length >= 4) && char.IsUpper(part[0])) {
                    parts.Add(part);
                }
            }
        }
        _nameParts = parts.ToList();
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

    /// <summary>
    /// For an unknown capitalised word, the story element name it is probably a typo of ("Rowen" \u2192 "Rowan"):
    /// one edit away, or two for names of eight letters or more.
    /// </summary>
    public string? FindSimilarName(string word) {
        word = Tokenizer.StripPossessive(word);
        if ((word.Length < 4) || !char.IsUpper(word[0]) || IsKnownWord(word)) {
            return null;
        }
        string? best = null;
        int bestDistance = int.MaxValue;
        foreach (string part in _nameParts) {
            int allowed = part.Length >= 8 ? 2 : 1;
            if (Math.Abs(part.Length - word.Length) > allowed) {
                continue;
            }
            int distance = EditDistance(word, part, allowed);
            if ((distance <= allowed) && (distance < bestDistance)) {
                best = part;
                bestDistance = distance;
            }
        }
        return best;
    }

    /// <summary>Damerau-Levenshtein distance (adjacent swaps count as one edit), giving up early past <paramref name="limit"/>.</summary>
    internal static int EditDistance(string a, string b, int limit) {
        int[,] d = new int[a.Length + 1, b.Length + 1];
        for (int i = 0; i <= a.Length; i++) {
            d[i, 0] = i;
        }
        for (int j = 0; j <= b.Length; j++) {
            d[0, j] = j;
        }
        for (int i = 1; i <= a.Length; i++) {
            int rowMinimum = int.MaxValue;
            for (int j = 1; j <= b.Length; j++) {
                int cost = char.ToLowerInvariant(a[i - 1]) == char.ToLowerInvariant(b[j - 1]) ? 0 : 1;
                int value = Math.Min(Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1), d[i - 1, j - 1] + cost);
                if ((i > 1) && (j > 1) && (char.ToLowerInvariant(a[i - 1]) == char.ToLowerInvariant(b[j - 2])) && (char.ToLowerInvariant(a[i - 2]) == char.ToLowerInvariant(b[j - 1]))) {
                    value = Math.Min(value, d[i - 2, j - 2] + 1);
                }
                d[i, j] = value;
                rowMinimum = Math.Min(rowMinimum, value);
            }
            if (rowMinimum > limit) {
                return limit + 1;
            }
        }
        return d[a.Length, b.Length];
    }
}