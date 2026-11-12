using System.Text.RegularExpressions;

namespace WritingIDE.Core.Grammar.Rules;

/// <summary>NW2001: spacing around punctuation.</summary>
public sealed partial class PunctuationSpacingRule() : GrammarRule("NW2001", "Punctuation spacing", RuleCategory.Punctuation, DiagnosticSeverity.Warning,
    "Detects double spaces, spaces before punctuation and missing spaces after punctuation.") {
    public override IEnumerable<GrammarDiagnostic> Analyze(AnalysisContext context) {
        string text = context.Text;
        foreach (Match match in MultipleSpaces().Matches(text)) {
            yield return Report(match.Index, match.Length, "Multiple spaces between words.", new TextFix("Use a single space", " "));
        }
        foreach (Match match in SpaceBeforePunctuation().Matches(text)) {
            Group spaces = match.Groups["spaces"];
            string mark = match.Groups["mark"].Value;
            yield return Report(spaces.Index, spaces.Length, $"Unexpected space before '{mark}'.", new TextFix("Remove the space", ""));
        }
        foreach (Match match in MissingSpaceAfterPunctuation().Matches(text)) {
            Group mark = match.Groups["mark"];
            yield return Report(mark.Index, mark.Length, $"Missing space after '{mark.Value}'.", new TextFix("Insert a space", mark.Value + " "));
        }
    }

    [GeneratedRegex(@"(?<=\S) {2,}(?=\S)")]
    private static partial Regex MultipleSpaces();

    [GeneratedRegex(@"(?<=[\p{L}\p{N}])(?<spaces>[ \t]+)(?<mark>[,;:!?]|\.(?!\.))")]
    private static partial Regex SpaceBeforePunctuation();

    [GeneratedRegex(@"(?<=\p{Ll}{2})(?<mark>[,;!?]|\.(?=\p{Lu}\p{Ll}))(?=\p{L})")]
    private static partial Regex MissingSpaceAfterPunctuation();
}

/// <summary>NW2002: unbalanced quotation marks and brackets within a paragraph.</summary>
public sealed class BalancedPunctuationRule() : GrammarRule("NW2002", "Unbalanced quotes or brackets", RuleCategory.Punctuation, DiagnosticSeverity.Warning,
    "Finds quotation marks and brackets that are opened but never closed (or closed but never opened) within a paragraph.") {
    public override IEnumerable<GrammarDiagnostic> Analyze(AnalysisContext context) {
        string text = context.Text;
        int paragraphStart = 0;
        while (paragraphStart <= text.Length) {
            int paragraphEnd = text.IndexOf('\n', paragraphStart);
            if (paragraphEnd < 0) {
                paragraphEnd = text.Length;
            }
            foreach (GrammarDiagnostic diagnostic in AnalyzeParagraph(text, paragraphStart, paragraphEnd)) {
                yield return diagnostic;
            }
            paragraphStart = paragraphEnd + 1;
        }
    }

    private IEnumerable<GrammarDiagnostic> AnalyzeParagraph(string text, int start, int end) {
        Stack<int> parentheses = new();
        Stack<int> curlyQuotes = new();
        int straightQuoteCount = 0;
        int lastStraightQuote = -1;
        for (int i = start; i < end; i++) {
            switch (text[i]) {
                case '(':
                    parentheses.Push(i);
                    break;
                case ')':
                    if (parentheses.Count == 0) {
                        yield return Report(i, 1, "Closing parenthesis without a matching '('.");
                    }
                    else {
                        parentheses.Pop();
                    }
                    break;
                case '\u201C':
                    curlyQuotes.Push(i);
                    break;
                case '\u201D':
                    if (curlyQuotes.Count > 0) {
                        curlyQuotes.Pop();
                    }
                    break;
                case '"':
                    straightQuoteCount++;
                    lastStraightQuote = i;
                    break;
            }
        }
        foreach (int open in parentheses) {
            yield return Report(open, 1, "Parenthesis is never closed in this paragraph.", new TextFix("Remove '('", ""));
        }
        // An unclosed quote at the very end of a paragraph is the convention for multi-paragraph dialogue.
        if ((straightQuoteCount % 2 == 1) && !IsContinuedDialogue(text, lastStraightQuote, start, end)) {
            yield return Report(lastStraightQuote, 1, "Quotation mark is never closed in this paragraph.");
        }
        foreach (int open in curlyQuotes) {
            if (!IsContinuedDialogue(text, open, start, end)) {
                yield return Report(open, 1, "Quotation mark is never closed in this paragraph.");
            }
        }
    }

    private static bool IsContinuedDialogue(string text, int quoteOffset, int paragraphStart, int paragraphEnd) {
        int firstNonSpace = paragraphStart;
        while ((firstNonSpace < paragraphEnd) && char.IsWhiteSpace(text[firstNonSpace])) {
            firstNonSpace++;
        }
        // The paragraph opens with the quote and runs to the end without closing it.
        bool opensParagraph = quoteOffset == firstNonSpace;
        int nextParagraph = paragraphEnd + 1;
        while ((nextParagraph < text.Length) && char.IsWhiteSpace(text[nextParagraph])) {
            nextParagraph++;
        }
        bool nextOpensWithQuote = (nextParagraph < text.Length) && text[nextParagraph] is '"' or '\u201C';
        return opensParagraph && nextOpensWithQuote;
    }
}