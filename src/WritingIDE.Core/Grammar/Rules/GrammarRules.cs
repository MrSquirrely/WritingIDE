using WritingIDE.Core.Text;

namespace WritingIDE.Core.Grammar.Rules;

/// <summary>NW1002: the same word twice in a row ("the the").</summary>
public sealed class RepeatedWordRule() : GrammarRule("NW1002", "Repeated word", RuleCategory.Grammar, DiagnosticSeverity.Error,
    "Detects a word that is accidentally typed twice in a row.") {
    private static readonly HashSet<string> AllowedRepeats = new(StringComparer.OrdinalIgnoreCase) { "had", "that", "is", "no", "bye", "knock", "very", "far", "so", "ha", "la", "on", "and" };

    public override IEnumerable<GrammarDiagnostic> Analyze(AnalysisContext context) {
        IReadOnlyList<Token> tokens = context.Tokens;
        for (int i = 2; i < tokens.Count; i++) {
            Token second = tokens[i];
            Token gap = tokens[i - 1];
            Token first = tokens[i - 2];
            if ((second.Kind != TokenKind.Word) || (first.Kind != TokenKind.Word) || (gap.Kind != TokenKind.Whitespace)) {
                continue;
            }
            if (!string.Equals(first.Text, second.Text, StringComparison.OrdinalIgnoreCase) || AllowedRepeats.Contains(first.Text)) {
                continue;
            }
            yield return Report(gap.Offset, second.End - gap.Offset, $"Repeated word '{second.Text}'.", new TextFix($"Remove repeated '{second.Text}'", ""));
        }
    }
}

/// <summary>NW1003: "a" versus "an" by the sound of the following word.</summary>
public sealed class ArticleRule() : GrammarRule("NW1003", "Indefinite article", RuleCategory.Grammar, DiagnosticSeverity.Warning,
    "Use 'an' before a vowel sound ('an apple', 'an hour') and 'a' before a consonant sound ('a unicorn', 'a one-off').") {
    private static readonly string[] ConsonantSoundPrefixes = [
        "uni", "use", "usu", "usa", "uti", "uto", "ure", "uri", "ura", "uku", "ufo", "uganda", "eu", "ewe", "one", "once", "ouija"
    ];

    private static readonly string[] VowelSoundPrefixes = ["hour", "honest", "honor", "honour", "heir", "herb"];

    private static readonly string[] VowelSoundExceptions = ["unin", "unim", "unid", "unil", "unio", "unit", "unic", "oner"];

    private static readonly string[] ConsonantSoundOverrides = ["unit", "unic", "union", "unilateral", "unison"];

    public override IEnumerable<GrammarDiagnostic> Analyze(AnalysisContext context) {
        IReadOnlyList<Token> tokens = context.Tokens;
        for (int i = 0; i + 2 < tokens.Count; i++) {
            Token article = tokens[i];
            if ((article.Kind != TokenKind.Word) || !(article.Is("a") || article.Is("an"))) {
                continue;
            }
            if ((tokens[i + 1].Kind != TokenKind.Whitespace) || (i > 0 && tokens[i - 1].Kind == TokenKind.Punctuation && tokens[i - 1].Text is "-" or "'")) {
                continue;
            }
            Token next = tokens[i + 2];
            if (next.Kind is not (TokenKind.Word or TokenKind.Number)) {
                continue;
            }
            bool? vowel = StartsWithVowelSound(next.Text);
            if (vowel is null) {
                continue;
            }
            bool isAn = article.Is("an");
            if (vowel.Value == isAn) {
                continue;
            }
            string replacement = MatchCase(article.Text, vowel.Value ? "an" : "a");
            yield return Report(article.Offset, article.Length,
                $"Use '{replacement}' before '{next.Text}'.", TextFix.ReplaceWith(replacement));
        }
    }

    internal static bool? StartsWithVowelSound(string word) {
        if (word.Length == 0) {
            return null;
        }
        if (char.IsDigit(word[0])) {
            string digits = new(word.TakeWhile(char.IsDigit).ToArray());
            return digits.StartsWith('8') || digits is "11" or "18" || ((digits.Length is 5 or 6) && (digits.StartsWith("11") || digits.StartsWith("18")));
        }
        // Acronyms such as "FBI" or "MRI" are read letter by letter.
        if ((word.Length is > 1 and <= 5) && word.All(char.IsUpper)) {
            return "AEFHILMNORSX".Contains(word[0]);
        }
        string lower = word.ToLowerInvariant();
        foreach (string prefix in VowelSoundPrefixes) {
            if (lower.StartsWith(prefix, StringComparison.Ordinal)) {
                return true;
            }
        }
        foreach (string prefix in ConsonantSoundOverrides) {
            if (lower.StartsWith(prefix, StringComparison.Ordinal)) {
                return false;
            }
        }
        bool isVowelException = VowelSoundExceptions.Any(prefix => lower.StartsWith(prefix, StringComparison.Ordinal));
        if (!isVowelException) {
            foreach (string prefix in ConsonantSoundPrefixes) {
                if (lower.StartsWith(prefix, StringComparison.Ordinal)) {
                    return false;
                }
            }
        }
        return lower[0] is 'a' or 'e' or 'i' or 'o' or 'u';
    }
}

/// <summary>NW1004: pronoun/verb agreement ("they was", "he don't").</summary>
public sealed class SubjectVerbAgreementRule() : GrammarRule("NW1004", "Subject-verb agreement", RuleCategory.Grammar, DiagnosticSeverity.Warning,
    "Checks that verbs agree with personal pronouns: 'they were', 'he doesn't', 'I am'.") {
    private static readonly Dictionary<string, Dictionary<string, string>> Corrections = new(StringComparer.OrdinalIgnoreCase) {
        ["I"] = new(StringComparer.OrdinalIgnoreCase) { ["is"] = "am", ["are"] = "am", ["has"] = "have", ["does"] = "do", ["doesn't"] = "don't", ["isn't"] = "am not", ["wasn't"] = "wasn't" },
        ["he"] = ThirdPersonSingular(),
        ["she"] = ThirdPersonSingular(),
        ["it"] = ThirdPersonSingular(),
        ["we"] = Plural(),
        ["they"] = Plural(),
        ["you"] = Plural()
    };

    private static readonly HashSet<string> Auxiliaries = new(StringComparer.OrdinalIgnoreCase) {
        "do", "does", "did", "will", "would", "can", "could", "shall", "should", "may", "might", "must", "let", "make", "made", "help", "helped",
        "saw", "see", "watch", "watched", "heard", "hear", "if", "wish", "though", "whether", "lest", "doesn't", "didn't", "don't", "won't",
        "wouldn't", "can't", "couldn't", "shouldn't", "have", "had", "has", "to", "than", "as", "made", "makes", "let's"
    };

    public override IEnumerable<GrammarDiagnostic> Analyze(AnalysisContext context) {
        foreach (Sentence sentence in context.Sentences) {
            IReadOnlyList<Token> words = sentence.Words;
            for (int i = 0; i + 1 < words.Count; i++) {
                Token subject = words[i];
                Token verb = words[i + 1];
                if (!Corrections.TryGetValue(subject.Text, out Dictionary<string, string>? table) || !table.TryGetValue(NormalizeApostrophe(verb.Text), out string? fix)) {
                    continue;
                }
                if (string.Equals(fix, NormalizeApostrophe(verb.Text), StringComparison.OrdinalIgnoreCase)) {
                    continue;
                }
                if (!OnlyWhitespaceBetween(context.Text, subject, verb)) {
                    continue;
                }
                if ((i > 0) && Auxiliaries.Contains(words[i - 1].Text)) {
                    continue;
                }
                // "it" after a verb is usually an object ("make it are" never happens, but "found it was" does: fine).
                string replacement = MatchCase(verb.Text, fix);
                yield return Report(verb.Offset, verb.Length,
                    $"'{subject.Text} {verb.Text}' does not agree; use '{subject.Text} {replacement}'.", TextFix.ReplaceWith(replacement));
            }
        }
    }

    private static Dictionary<string, string> ThirdPersonSingular() {
        return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) {
            ["are"] = "is", ["am"] = "is", ["have"] = "has", ["don't"] = "doesn't", ["aren't"] = "isn't", ["haven't"] = "hasn't", ["weren't"] = "wasn't"
        };
    }

    private static Dictionary<string, string> Plural() {
        return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) {
            ["is"] = "are", ["am"] = "are", ["was"] = "were", ["has"] = "have", ["doesn't"] = "don't", ["isn't"] = "aren't", ["wasn't"] = "weren't", ["hasn't"] = "haven't"
        };
    }

    private static string NormalizeApostrophe(string word) {
        return word.Replace('\u2019', '\'');
    }

    private static bool OnlyWhitespaceBetween(string text, Token first, Token second) {
        for (int i = first.End; i < second.Offset; i++) {
            if (!char.IsWhiteSpace(text[i]) || text[i] is '\r' or '\n') {
                return false;
            }
        }
        return true;
    }
}

/// <summary>NW1005: sentences should start with a capital letter; "i" should be "I".</summary>
public sealed class CapitalizationRule() : GrammarRule("NW1005", "Capitalization", RuleCategory.Grammar, DiagnosticSeverity.Warning,
    "Sentences start with a capital letter, and the pronoun 'I' is always capitalized.") {
    private static readonly HashSet<string> LowercasePronouns = ["i", "i'm", "i'll", "i've", "i'd", "i\u2019m", "i\u2019ll", "i\u2019ve", "i\u2019d"];

    public override IEnumerable<GrammarDiagnostic> Analyze(AnalysisContext context) {
        HashSet<int> reported = [];
        foreach (Sentence sentence in context.Sentences) {
            Token first = sentence.Tokens.FirstOrDefault(static token => token.Kind is TokenKind.Word or TokenKind.Number);
            if ((first.Text is null) || (first.Kind != TokenKind.Word)) {
                continue;
            }
            // Skip sentences that continue an ellipsis or start with a dash.
            Token leading = sentence.Tokens[0];
            if ((leading.Kind == TokenKind.Punctuation) && leading.Text is "..." or "\u2026" or "-" or "\u2014") {
                continue;
            }
            if (char.IsLower(first.Text[0]) && !LooksLikeBrand(first.Text)) {
                string fixedWord = char.ToUpperInvariant(first.Text[0]) + first.Text[1..];
                reported.Add(first.Offset);
                yield return Report(first.Offset, first.Length, "A sentence should start with a capital letter.", TextFix.ReplaceWith(fixedWord));
            }
        }
        foreach (Token token in context.Tokens) {
            if ((token.Kind == TokenKind.Word) && LowercasePronouns.Contains(token.Text) && !reported.Contains(token.Offset)) {
                string fixedWord = "I" + token.Text[1..];
                yield return new GrammarDiagnostic(Id, DiagnosticSeverity.Error, Category, token.Offset, token.Length, "The pronoun 'I' is always capitalized.") {
                    Fixes = [TextFix.ReplaceWith(fixedWord)]
                };
            }
        }
    }

    private static bool LooksLikeBrand(string word) {
        return (word.Length > 1) && word.Skip(1).Any(char.IsUpper);
    }
}