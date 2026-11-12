using WritingIDE.Core.Text;

namespace WritingIDE.Core.Analysis;

/// <summary>Word counts, readability and pacing figures for a piece of text.</summary>
public sealed record TextStatistics(
    int Words,
    int Sentences,
    int Paragraphs,
    int Characters,
    double AverageSentenceLength,
    double FleschReadingEase,
    TimeSpan ReadingTime,
    double DialoguePercent) {
    public static TextStatistics Empty { get; } = new(0, 0, 0, 0, 0, 0, TimeSpan.Zero, 0);

    public string ReadabilityLabel => FleschReadingEase switch {
        _ when Words == 0 => "-",
        >= 90 => "Very easy",
        >= 80 => "Easy",
        >= 70 => "Fairly easy",
        >= 60 => "Standard",
        >= 50 => "Fairly difficult",
        >= 30 => "Difficult",
        _ => "Very difficult"
    };

    public string ReadingTimeText => ReadingTime.TotalMinutes < 1 ? "< 1 min" : $"{Math.Round(ReadingTime.TotalMinutes)} min";

    public static TextStatistics Compute(string text) {
        IReadOnlyList<Token> tokens = Tokenizer.Tokenize(text);
        IReadOnlyList<Sentence> sentences = SentenceSplitter.Split(tokens);
        return Compute(text, tokens, sentences);
    }

    public static TextStatistics Compute(string text, IReadOnlyList<Token> tokens, IReadOnlyList<Sentence> sentences) {
        int words = 0;
        int syllables = 0;
        foreach (Token token in tokens) {
            if (token.Kind == TokenKind.Word) {
                words++;
                syllables += CountSyllables(token.Text);
            }
            else if (token.Kind == TokenKind.Number) {
                words++;
                syllables += 2;
            }
        }
        int paragraphs = text.Split('\n').Count(static line => !string.IsNullOrWhiteSpace(line));
        int sentenceCount = Math.Max(sentences.Count, words > 0 ? 1 : 0);
        double averageSentence = sentenceCount == 0 ? 0 : (double)words / sentenceCount;
        double flesch = words == 0 ? 0 : 206.835 - (1.015 * averageSentence) - (84.6 * ((double)syllables / words));
        return new TextStatistics(
            words,
            sentenceCount,
            paragraphs,
            text.Length,
            Math.Round(averageSentence, 1),
            Math.Round(Math.Clamp(flesch, 0, 120), 1),
            TimeSpan.FromMinutes(words / 250.0),
            Math.Round(ComputeDialoguePercent(text), 1));
    }

    public static int CountWords(string text) {
        int count = 0;
        bool inWord = false;
        foreach (char c in text) {
            bool isWordChar = char.IsLetterOrDigit(c) || (inWord && Tokenizer.IsInnerWordJoiner(c));
            if (isWordChar && !inWord) {
                count++;
            }
            inWord = isWordChar;
        }
        return count;
    }

    public static int CountSyllables(string word) {
        string lower = word.ToLowerInvariant();
        int count = 0;
        bool previousVowel = false;
        foreach (char c in lower) {
            bool vowel = c is 'a' or 'e' or 'i' or 'o' or 'u' or 'y';
            if (vowel && !previousVowel) {
                count++;
            }
            previousVowel = vowel;
        }
        if (lower.EndsWith('e') && !lower.EndsWith("le") && (count > 1)) {
            count--;
        }
        return Math.Max(1, count);
    }

    private static double ComputeDialoguePercent(string text) {
        if (text.Length == 0) {
            return 0;
        }
        int inside = 0;
        bool inQuote = false;
        foreach (char c in text) {
            if (c is '"' or '\u201C' or '\u201D') {
                inQuote = c switch {
                    '\u201C' => true,
                    '\u201D' => false,
                    _ => !inQuote
                };
                continue;
            }
            if (c == '\n') {
                inQuote = false;
            }
            if (inQuote && char.IsLetter(c)) {
                inside++;
            }
        }
        int letters = text.Count(char.IsLetter);
        return letters == 0 ? 0 : (100.0 * inside) / letters;
    }
}