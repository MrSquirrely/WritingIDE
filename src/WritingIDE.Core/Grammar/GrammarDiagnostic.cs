namespace WritingIDE.Core.Grammar;

/// <summary>Severity, matching the Visual Studio Error List.</summary>
public enum DiagnosticSeverity {
    Message,
    Warning,
    Error
}

/// <summary>A quick fix: replaces the diagnostic's span with <see cref="Replacement"/>.</summary>
public sealed record TextFix(string Title, string Replacement) {
    public static TextFix ReplaceWith(string replacement) {
        return new(replacement.Length == 0 ? "Remove" : $"Change to '{replacement}'", replacement);
    }
}

/// <summary>A problem found by a grammar rule.</summary>
public sealed record GrammarDiagnostic(
    string RuleId,
    DiagnosticSeverity Severity,
    string Category,
    int Offset,
    int Length,
    string Message) {
    public IReadOnlyList<TextFix> Fixes { get; init; } = [];

    /// <summary>The offending word for spelling diagnostics (used by "Add to dictionary").</summary>
    public string? Word { get; init; }

    public int End => Offset + Length;
}