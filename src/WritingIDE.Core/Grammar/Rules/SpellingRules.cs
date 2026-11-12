using WritingIDE.Core.Text;

namespace WritingIDE.Core.Grammar.Rules;

/// <summary>NW1001: spelling, backed by the platform spell checker, aware of story element names and the project dictionary.</summary>
public sealed class SpellingRule() : GrammarRule("NW1001", "Spelling", RuleCategory.Spelling, DiagnosticSeverity.Error,
    "Flags words that are not in the dictionary. Character, place and other story element names are always accepted.") {
    private const int MaxSuggestedIssues = 250;

    public override IEnumerable<GrammarDiagnostic> Analyze(AnalysisContext context) {
        if (context.Spelling is null) {
            yield break;
        }
        int suggested = 0;
        foreach (SpellingIssue issue in context.Spelling.Check(context.Text)) {
            context.CancellationToken.ThrowIfCancellationRequested();
            if ((issue.Offset < 0) || (issue.Offset + issue.Length > context.Text.Length)) {
                continue;
            }
            string word = context.Text.Substring(issue.Offset, issue.Length);
            if (context.IsKnownWord(word) || word.Any(char.IsDigit)) {
                continue;
            }
            List<string> suggestions = [];
            if (issue.Replacement is not null) {
                suggestions.Add(issue.Replacement);
            }
            else if (suggested++ < MaxSuggestedIssues) {
                suggestions.AddRange(context.Spelling.Suggest(word).Take(5));
            }
            yield return new GrammarDiagnostic(Id, Severity, Category, issue.Offset, issue.Length, $"Unknown word '{word}'.") {
                Fixes = suggestions.Select(TextFix.ReplaceWith).ToList(),
                Word = word
            };
        }
    }
}

/// <summary>NW4001: a story element's name written with the wrong capitalisation ("elena" for "Elena").</summary>
public sealed class EntityCasingRule() : GrammarRule("NW4001", "Story element capitalization", RuleCategory.Story, DiagnosticSeverity.Warning,
    "Detects names of characters, places and other story elements written in the wrong case.") {
    public override IEnumerable<GrammarDiagnostic> Analyze(AnalysisContext context) {
        Dictionary<string, string> properNames = new(StringComparer.OrdinalIgnoreCase);
        foreach ((string name, _) in context.Options.Entities.Names) {
            foreach (string part in name.Split(' ', StringSplitOptions.RemoveEmptyEntries)) {
                if ((part.Length > 1) && char.IsUpper(part[0])) {
                    properNames.TryAdd(part, part);
                }
            }
        }
        if (properNames.Count == 0) {
            yield break;
        }
        Dictionary<string, bool> commonWordCache = new(StringComparer.OrdinalIgnoreCase);
        foreach (Token token in context.Tokens) {
            if (token.Kind != TokenKind.Word) {
                continue;
            }
            string word = Tokenizer.StripPossessive(token.Text);
            if (!properNames.TryGetValue(word, out string? proper) || string.Equals(word, proper, StringComparison.Ordinal)) {
                continue;
            }
            if (!char.IsLower(word[0])) {
                continue;
            }
            // Names that are also ordinary words ("Will", "Hope", "Rose") are only proper nouns when capitalised.
            if (!commonWordCache.TryGetValue(word, out bool isCommonWord)) {
                isCommonWord = context.Spelling?.IsCorrect(word.ToLowerInvariant()) ?? false;
                commonWordCache[word] = isCommonWord;
            }
            if (isCommonWord) {
                continue;
            }
            yield return Report(token.Offset, word.Length, $"'{proper}' is a story element; names are capitalized.", TextFix.ReplaceWith(proper));
        }
    }
}