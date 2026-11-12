namespace WritingIDE.Core.Text;

/// <summary>A sentence: a contiguous run of tokens.</summary>
public sealed class Sentence(IReadOnlyList<Token> tokens) {
    public IReadOnlyList<Token> Tokens { get; } = tokens;

    public IReadOnlyList<Token> Words { get; } = tokens.Where(static token => token.Kind == TokenKind.Word).ToList();

    public int Offset => Tokens[0].Offset;

    public int End => Tokens[^1].End;

    public int Length => End - Offset;

    public int WordCount => Tokens.Count(static token => token.Kind is TokenKind.Word or TokenKind.Number);
}

/// <summary>Splits tokens into sentences, aware of abbreviations, initials, ellipses and dialogue tags.</summary>
public static class SentenceSplitter {
    private static readonly HashSet<string> Abbreviations = new(StringComparer.OrdinalIgnoreCase) {
        "mr", "mrs", "ms", "dr", "st", "jr", "sr", "prof", "vs", "etc", "mt", "capt", "col", "gen", "lt", "sgt", "rev", "fr", "no", "approx"
    };

    private static readonly HashSet<string> ClosingCharacters = ["\"", "\u201D", "'", "\u2019", ")", "]"];

    public static IReadOnlyList<Sentence> Split(IReadOnlyList<Token> tokens) {
        List<Sentence> sentences = [];
        List<Token> current = [];

        void Flush() {
            while ((current.Count > 0) && current[^1].Kind == TokenKind.Whitespace) {
                current.RemoveAt(current.Count - 1);
            }
            if (current.Count > 0) {
                sentences.Add(new Sentence(current.ToList()));
            }
            current.Clear();
        }

        for (int i = 0; i < tokens.Count; i++) {
            Token token = tokens[i];
            if (token.Kind == TokenKind.NewLine) {
                Flush();
                continue;
            }
            if ((current.Count == 0) && (token.Kind == TokenKind.Whitespace)) {
                continue;
            }
            current.Add(token);
            if (!IsTerminator(token)) {
                continue;
            }

            int lookahead = i + 1;
            bool consumedQuote = false;
            while ((lookahead < tokens.Count) && ClosingCharacters.Contains(tokens[lookahead].Text)) {
                current.Add(tokens[lookahead]);
                consumedQuote = true;
                lookahead++;
            }
            i = lookahead - 1;

            if (ContinuesSentence(tokens, current, token, lookahead, consumedQuote)) {
                continue;
            }
            Flush();
        }
        Flush();
        return sentences;
    }

    private static bool IsTerminator(Token token) {
        return (token.Kind == TokenKind.Punctuation) && token.Text is "." or "!" or "?" or "..." or "\u2026";
    }

    private static bool ContinuesSentence(IReadOnlyList<Token> tokens, List<Token> current, Token terminator, int nextIndex, bool consumedQuote) {
        // "example.com" or "U.S" style: no whitespace after the period.
        if ((nextIndex < tokens.Count) && tokens[nextIndex].Kind is TokenKind.Word or TokenKind.Number) {
            return true;
        }

        int previousIndex = current.Count - 2;
        while ((previousIndex >= 0) && (current[previousIndex].Kind != TokenKind.Word) && (current[previousIndex].Offset >= terminator.Offset)) {
            previousIndex--;
        }
        if ((terminator.Text == ".") && (previousIndex >= 0) && (current[previousIndex].Kind == TokenKind.Word) && (current[previousIndex].End == terminator.Offset)) {
            string previousWord = current[previousIndex].Text;
            if (Abbreviations.Contains(previousWord) || ((previousWord.Length == 1) && char.IsUpper(previousWord[0]))) {
                return true;
            }
        }

        Token? next = NextSignificant(tokens, nextIndex);
        if (next is null || (next.Value.Kind != TokenKind.Word)) {
            return false;
        }
        bool nextIsLower = char.IsLower(next.Value.Text[0]);
        if (!nextIsLower) {
            return false;
        }
        // "Help!" she cried.   /   He paused... and then left.
        return consumedQuote || (terminator.Text is "..." or "\u2026" or "!" or "?");
    }

    private static Token? NextSignificant(IReadOnlyList<Token> tokens, int index) {
        for (int i = index; i < tokens.Count; i++) {
            if (tokens[i].Kind == TokenKind.NewLine) {
                return null;
            }
            if (tokens[i].Kind != TokenKind.Whitespace) {
                return tokens[i];
            }
        }
        return null;
    }
}