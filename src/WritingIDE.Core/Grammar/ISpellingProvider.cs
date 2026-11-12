namespace WritingIDE.Core.Grammar;

public readonly record struct SpellingIssue(int Offset, int Length, string? Replacement);

/// <summary>Pluggable spell checker (the WPF app uses the Windows spell checking API).</summary>
public interface ISpellingProvider {
    bool IsAvailable { get; }

    IReadOnlyList<SpellingIssue> Check(string text);

    IReadOnlyList<string> Suggest(string word);

    bool IsCorrect(string word);
}