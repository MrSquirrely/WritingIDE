namespace WritingIDE.Core.Text;

public enum TokenKind {
    Word,
    Number,
    Punctuation,
    Whitespace,
    NewLine
}

public readonly record struct Token(TokenKind Kind, int Offset, string Text) {
    public int Length => Text.Length;

    public int End => Offset + Text.Length;

    public bool IsWord => Kind == TokenKind.Word;

    public bool Is(string text) {
        return string.Equals(Text, text, StringComparison.OrdinalIgnoreCase);
    }

    public override string ToString() {
        return $"{Kind}:{Text}";
    }
}

/// <summary>Splits prose into words, numbers, punctuation, whitespace and line breaks.</summary>
public static class Tokenizer {
    public static IReadOnlyList<Token> Tokenize(string text) {
        List<Token> tokens = [];
        int i = 0;
        while (i < text.Length) {
            char c = text[i];
            int start = i;
            if (c is '\r' or '\n') {
                i += ((c == '\r') && (i + 1 < text.Length) && (text[i + 1] == '\n')) ? 2 : 1;
                tokens.Add(new Token(TokenKind.NewLine, start, text[start..i]));
            }
            else if (char.IsWhiteSpace(c)) {
                while ((i < text.Length) && char.IsWhiteSpace(text[i]) && text[i] is not ('\r' or '\n')) {
                    i++;
                }
                tokens.Add(new Token(TokenKind.Whitespace, start, text[start..i]));
            }
            else if (char.IsLetter(c)) {
                i++;
                while (i < text.Length) {
                    char current = text[i];
                    if (char.IsLetterOrDigit(current)) {
                        i++;
                    }
                    else if (IsInnerWordJoiner(current) && (i + 1 < text.Length) && char.IsLetter(text[i + 1])) {
                        i += 2;
                    }
                    else {
                        break;
                    }
                }
                tokens.Add(new Token(TokenKind.Word, start, text[start..i]));
            }
            else if (char.IsDigit(c)) {
                i++;
                while (i < text.Length) {
                    char current = text[i];
                    if (char.IsDigit(current) || char.IsLetter(current)) {
                        i++;
                    }
                    else if ((current is ',' or '.' or ':') && (i + 1 < text.Length) && char.IsDigit(text[i + 1])) {
                        i += 2;
                    }
                    else {
                        break;
                    }
                }
                tokens.Add(new Token(TokenKind.Number, start, text[start..i]));
            }
            else if ((c == '.') && (i + 2 < text.Length) && (text[i + 1] == '.') && (text[i + 2] == '.')) {
                i += 3;
                tokens.Add(new Token(TokenKind.Punctuation, start, "..."));
            }
            else {
                i++;
                tokens.Add(new Token(TokenKind.Punctuation, start, text[start..i]));
            }
        }
        return tokens;
    }

    public static bool IsInnerWordJoiner(char c) {
        return c is '\'' or '\u2019' or '-';
    }

    public static bool IsWordCharacter(char c) {
        return char.IsLetterOrDigit(c) || IsInnerWordJoiner(c);
    }

    /// <summary>Returns the word that contains (or ends at) <paramref name="offset"/>.</summary>
    public static (int Start, int Length) GetWordAt(string text, int offset) {
        if ((offset < 0) || (offset > text.Length)) {
            return (offset, 0);
        }
        int start = offset;
        while ((start > 0) && IsWordCharacter(text[start - 1])) {
            start--;
        }
        int end = offset;
        while ((end < text.Length) && IsWordCharacter(text[end])) {
            end++;
        }
        while ((end > start) && IsInnerWordJoiner(text[end - 1])) {
            end--;
        }
        while ((start < end) && IsInnerWordJoiner(text[start])) {
            start++;
        }
        return (start, end - start);
    }

    /// <summary>Strips a trailing possessive ("Elena's" becomes "Elena").</summary>
    public static string StripPossessive(string word) {
        if ((word.Length > 2) && ((word[^1] == 's') || (word[^1] == 'S')) && word[^2] is '\'' or '\u2019') {
            return word[..^2];
        }
        return word;
    }
}