namespace WritingIDE.Core.Grammar;

public static class RuleCategory {
    public const string Spelling = "Spelling";
    public const string Grammar = "Grammar";
    public const string Usage = "Usage";
    public const string Punctuation = "Punctuation";
    public const string Style = "Style";
    public const string Story = "Story";
}

/// <summary>Base class for grammar rules ("analyzers").</summary>
public abstract class GrammarRule(string id, string title, string category, DiagnosticSeverity severity, string description) {
    public string Id { get; } = id;

    public string Title { get; } = title;

    public string Category { get; } = category;

    public DiagnosticSeverity Severity { get; } = severity;

    public string Description { get; } = description;

    /// <summary>True for rules contributed by a plugin (such as Harper) rather than built into WritingIDE.</summary>
    public virtual bool IsPlugin => false;

    public abstract IEnumerable<GrammarDiagnostic> Analyze(AnalysisContext context);

    protected GrammarDiagnostic Report(int offset, int length, string message, params TextFix[] fixes) {
        return new GrammarDiagnostic(Id, Severity, Category, offset, length, message) { Fixes = fixes };
    }

    protected GrammarDiagnostic Report(int offset, int length, string message, IEnumerable<string> replacements) {
        return new GrammarDiagnostic(Id, Severity, Category, offset, length, message) { Fixes = replacements.Select(TextFix.ReplaceWith).ToList() };
    }

    /// <summary>Copies the capitalisation of <paramref name="original"/> onto <paramref name="replacement"/>.</summary>
    protected static string MatchCase(string original, string replacement) {
        if ((original.Length == 0) || (replacement.Length == 0)) {
            return replacement;
        }
        if ((original.Length > 1) && original.All(c => !char.IsLetter(c) || char.IsUpper(c))) {
            return replacement.ToUpperInvariant();
        }
        if (char.IsUpper(original[0])) {
            return char.ToUpperInvariant(replacement[0]) + replacement[1..];
        }
        return replacement;
    }
}