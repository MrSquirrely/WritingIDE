using System.Windows.Media;

using WritingIDE.Core.Grammar;
using WritingIDE.Core.Model;
using WritingIDE.Infrastructure;

namespace WritingIDE.ViewModels;

/// <summary>A row in the Error List.</summary>
public sealed class ErrorItem(GrammarDiagnostic diagnostic, Chapter chapter, int line, int column) {
    public GrammarDiagnostic Diagnostic { get; } = diagnostic;

    public Chapter Chapter { get; } = chapter;

    public int Line { get; } = line;

    public int Column { get; } = column;

    public DiagnosticSeverity Severity => Diagnostic.Severity;

    public string Code => Diagnostic.RuleId;

    public string Description => Diagnostic.Message;

    public string Category => Diagnostic.Category;

    /// <summary>"Book \u203A Chapter" in a series, the chapter title in a novel.</summary>
    public string Document { get; init; } = chapter.Title;

    public string SeverityGlyph => Visuals.GetSeverityGlyph(Diagnostic.Severity);

    public Brush SeverityBrush => Visuals.GetSeverityBrush(Diagnostic.Severity);
}

/// <summary>A row in Find Symbol Results.</summary>
public sealed class ReferenceItem(Chapter chapter, int offset, int length, int line, int column, string preview) {
    public Chapter Chapter { get; } = chapter;

    public int Offset { get; } = offset;

    public int Length { get; } = length;

    public int Line { get; } = line;

    public int Column { get; } = column;

    public string Preview { get; } = preview;

    /// <summary>"Book \u203A Chapter" in a series, the chapter title in a novel.</summary>
    public string Document { get; init; } = chapter.Title;
}