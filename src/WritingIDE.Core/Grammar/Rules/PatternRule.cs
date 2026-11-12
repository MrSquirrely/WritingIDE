using System.Text.RegularExpressions;

namespace WritingIDE.Core.Grammar.Rules;

/// <summary>A regular-expression entry: what to look for, what to suggest instead and why.</summary>
/// <param name="Pattern">Regex, matched case-insensitively at word boundaries.</param>
/// <param name="Replacement">Replacement template ($1 etc.), or null when there is no automatic fix.</param>
/// <param name="Message">Message; {0} is the matched text and {1} the suggested replacement.</param>
public sealed record PatternEntry(string Pattern, string? Replacement, string Message) {
    public Regex Regex { get; } = new($@"\b(?:{Pattern})\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);
}

/// <summary>Base class for rules driven by a table of phrases.</summary>
public abstract class PatternRule(string id, string title, string category, DiagnosticSeverity severity, string description)
    : GrammarRule(id, title, category, severity, description) {
    protected abstract IReadOnlyList<PatternEntry> Entries { get; }

    public override IEnumerable<GrammarDiagnostic> Analyze(AnalysisContext context) {
        foreach (PatternEntry entry in Entries) {
            context.CancellationToken.ThrowIfCancellationRequested();
            foreach (Match match in entry.Regex.Matches(context.Text)) {
                if (entry.Replacement is null) {
                    yield return Report(match.Index, match.Length, string.Format(entry.Message, match.Value, ""));
                    continue;
                }
                string replacement = MatchCase(match.Value, match.Result(entry.Replacement));
                yield return Report(match.Index, match.Length, string.Format(entry.Message, match.Value, replacement), TextFix.ReplaceWith(replacement));
            }
        }
    }
}