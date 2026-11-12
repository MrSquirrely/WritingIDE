using WritingIDE.Core.Text;

namespace WritingIDE.Core.Grammar.Rules;

/// <summary>NW3001: passive voice ("the lamp was lit by her").</summary>
public sealed class PassiveVoiceRule() : GrammarRule("NW3001", "Passive voice", RuleCategory.Style, DiagnosticSeverity.Message,
    "Points out passive constructions. Active voice is usually more direct and vivid.") {
    private static readonly HashSet<string> BeVerbs = new(StringComparer.OrdinalIgnoreCase) { "am", "is", "are", "was", "were", "be", "been", "being" };

    private static readonly HashSet<string> IrregularParticiples = new(StringComparer.OrdinalIgnoreCase) {
        "taken", "given", "written", "seen", "done", "made", "known", "shown", "broken", "chosen", "driven", "eaten", "fallen", "forgotten",
        "forgiven", "frozen", "hidden", "beaten", "bitten", "built", "bought", "caught", "held", "kept", "lost", "found", "told", "sold",
        "sent", "spent", "thrown", "worn", "torn", "sworn", "stolen", "spoken", "woken", "struck", "hung", "fed", "paid", "lit", "shot",
        "slain", "sung", "drawn", "grown", "blown", "flown", "shaken", "ridden", "risen", "swept", "taught", "thought", "brought", "won", "left"
    };

    // Adjectives that look like participles ("she was tired") are not passive voice.
    private static readonly HashSet<string> AdjectivalParticiples = new(StringComparer.OrdinalIgnoreCase) {
        "tired", "excited", "interested", "bored", "worried", "scared", "married", "supposed", "used", "pleased", "surprised", "confused",
        "concerned", "embarrassed", "exhausted", "determined", "convinced", "relieved", "frightened", "terrified", "amazed", "annoyed",
        "ashamed", "satisfied", "disappointed", "shocked", "stunned", "pleased", "prepared", "related", "naked", "wicked", "beloved",
        "aged", "learned", "crooked", "rugged", "ragged", "sacred", "kindred", "hundred", "red", "bed", "need", "seed", "shed", "bred",
        "sled", "wed", "fled", "tied", "dead", "alive", "dressed", "involved", "located", "finished", "gone", "closed", "locked", "crowded"
    };

    public override IEnumerable<GrammarDiagnostic> Analyze(AnalysisContext context) {
        foreach (Sentence sentence in context.Sentences) {
            IReadOnlyList<Token> words = sentence.Words;
            for (int i = 0; i + 1 < words.Count; i++) {
                if (!BeVerbs.Contains(words[i].Text)) {
                    continue;
                }
                int participleIndex = i + 1;
                if (words[participleIndex].Text.EndsWith("ly", StringComparison.OrdinalIgnoreCase) && (participleIndex + 1 < words.Count)) {
                    participleIndex++;
                }
                Token participle = words[participleIndex];
                if (!IsPastParticiple(participle.Text)) {
                    continue;
                }
                bool hasAgent = (participleIndex + 1 < words.Count) && words[participleIndex + 1].Is("by");
                string phrase = context.Text[words[i].Offset..participle.End];
                yield return Report(words[i].Offset, participle.End - words[i].Offset,
                    hasAgent
                        ? $"Passive voice: '{phrase} by...'. Consider making the actor the subject."
                        : $"Possible passive voice: '{phrase}'. Consider an active construction.");
                i = participleIndex;
            }
        }
    }

    private static bool IsPastParticiple(string word) {
        if (AdjectivalParticiples.Contains(word)) {
            return false;
        }
        if (IrregularParticiples.Contains(word)) {
            return true;
        }
        return (word.Length > 4) && word.EndsWith("ed", StringComparison.OrdinalIgnoreCase) && !word.EndsWith("eed", StringComparison.OrdinalIgnoreCase);
    }
}

/// <summary>NW3002: weak intensifiers and filler ("very", "really", "suddenly"), with stronger alternatives for "very + adjective".</summary>
public sealed class WeakWordsRule() : GrammarRule("NW3002", "Weak words and intensifiers", RuleCategory.Style, DiagnosticSeverity.Message,
    "Flags filler words that weaken prose and suggests stronger words for 'very + adjective'.") {
    private static readonly Dictionary<string, string[]> StrongerWords = new(StringComparer.OrdinalIgnoreCase) {
        ["big"] = ["huge", "enormous", "massive"], ["small"] = ["tiny", "minuscule"], ["tired"] = ["exhausted", "weary", "drained"],
        ["happy"] = ["elated", "overjoyed", "delighted"], ["sad"] = ["miserable", "sorrowful", "heartbroken"], ["angry"] = ["furious", "livid", "enraged"],
        ["scared"] = ["terrified", "petrified"], ["afraid"] = ["terrified", "fearful"], ["cold"] = ["freezing", "frigid", "icy"],
        ["hot"] = ["scorching", "sweltering", "blistering"], ["good"] = ["excellent", "superb"], ["bad"] = ["terrible", "awful", "dreadful"],
        ["fast"] = ["rapid", "swift"], ["quick"] = ["rapid", "swift"], ["slow"] = ["sluggish", "plodding"], ["quiet"] = ["silent", "hushed"],
        ["loud"] = ["deafening", "thunderous"], ["old"] = ["ancient", "decrepit"], ["beautiful"] = ["gorgeous", "stunning", "exquisite"],
        ["ugly"] = ["hideous", "grotesque"], ["hungry"] = ["starving", "ravenous"], ["dark"] = ["pitch-black", "murky"], ["bright"] = ["dazzling", "brilliant"],
        ["smart"] = ["brilliant", "ingenious"], ["tall"] = ["towering", "lofty"], ["important"] = ["crucial", "vital", "essential"],
        ["sure"] = ["certain", "positive"], ["wet"] = ["soaked", "drenched"], ["dry"] = ["parched", "arid"], ["rich"] = ["wealthy", "affluent"],
        ["poor"] = ["destitute", "impoverished"], ["clean"] = ["spotless", "immaculate"], ["dirty"] = ["filthy", "grimy"], ["strong"] = ["mighty", "powerful"],
        ["weak"] = ["feeble", "frail"], ["hard"] = ["arduous", "grueling"], ["easy"] = ["effortless", "simple"], ["much"] = ["greatly"],
        ["many"] = ["numerous", "countless"], ["sorry"] = ["apologetic", "remorseful"], ["funny"] = ["hilarious"], ["pretty"] = ["lovely", "beautiful"],
        ["nervous"] = ["anxious", "jittery"], ["calm"] = ["serene", "tranquil"], ["thin"] = ["gaunt", "slender"], ["long"] = ["endless", "lengthy"]
    };

    private static readonly Dictionary<string, string> FillerWords = new(StringComparer.OrdinalIgnoreCase) {
        ["very"] = "'very' is a weak intensifier.",
        ["really"] = "'really' is a weak intensifier.",
        ["extremely"] = "'extremely' is a weak intensifier.",
        ["suddenly"] = "'suddenly' tells the reader something is sudden instead of showing it.",
        ["literally"] = "'literally' is often filler.",
        ["basically"] = "'basically' is filler.",
        ["actually"] = "'actually' is often filler.",
        ["totally"] = "'totally' is often filler.",
        ["somehow"] = "'somehow' can signal vague writing.",
        ["quite"] = "'quite' weakens what follows."
    };

    public override IEnumerable<GrammarDiagnostic> Analyze(AnalysisContext context) {
        foreach (Sentence sentence in context.Sentences) {
            IReadOnlyList<Token> words = sentence.Words;
            for (int i = 0; i < words.Count; i++) {
                Token word = words[i];
                if (FillerWords.TryGetValue(word.Text, out string? message)) {
                    bool isIntensifier = word.Is("very") || word.Is("really") || word.Is("extremely");
                    if (isIntensifier && (i + 1 < words.Count) && StrongerWords.TryGetValue(words[i + 1].Text, out string[]? alternatives)) {
                        Token adjective = words[i + 1];
                        string phrase = context.Text[word.Offset..adjective.End];
                        List<TextFix> fixes = alternatives.Select(alternative => TextFix.ReplaceWith(MatchCase(word.Text, alternative))).ToList();
                        yield return Report(word.Offset, adjective.End - word.Offset, $"'{phrase}' is weak; try a stronger word: {string.Join(", ", alternatives)}.", [.. fixes]);
                        i++;
                        continue;
                    }
                    int removeEnd = word.End;
                    while ((removeEnd < context.Text.Length) && (context.Text[removeEnd] == ' ')) {
                        removeEnd++;
                    }
                    bool atSentenceStart = (word.Offset == sentence.Offset) || IsFirstWord(sentence, word);
                    TextFix[] removeFixes = atSentenceStart || (removeEnd == word.End) ? [] : [new TextFix($"Remove '{word.Text}'", "")];
                    yield return new GrammarDiagnostic(Id, Severity, Category, word.Offset, removeFixes.Length > 0 ? removeEnd - word.Offset : word.Length, message) {
                        Fixes = removeFixes
                    };
                }
                else if ((word.Is("began") || word.Is("started")) && (i + 2 < words.Count) && words[i + 1].Is("to")) {
                    yield return Report(word.Offset, words[i + 2].End - word.Offset,
                        $"'{word.Text} to {words[i + 2].Text}' delays the action. Consider using the verb directly.");
                }
            }
        }
    }

    private static bool IsFirstWord(Sentence sentence, Token word) {
        return (sentence.Words.Count > 0) && (sentence.Words[0].Offset == word.Offset);
    }
}

/// <summary>NW3006: adverbs in dialogue tags ("she said quietly").</summary>
public sealed class DialogueTagAdverbRule() : GrammarRule("NW3006", "Adverb in dialogue tag", RuleCategory.Style, DiagnosticSeverity.Message,
    "Adverbs attached to dialogue tags ('he said angrily') usually tell what the dialogue should show.") {
    private static readonly HashSet<string> Tags = new(StringComparer.OrdinalIgnoreCase) {
        "said", "says", "asked", "asks", "replied", "whispered", "shouted", "yelled", "muttered", "answered", "called", "cried", "added", "snapped", "murmured"
    };

    private static readonly HashSet<string> NotAdverbs = new(StringComparer.OrdinalIgnoreCase) { "only", "early", "family", "holy", "ugly", "silly", "reply", "fly", "lily", "belly" };

    public override IEnumerable<GrammarDiagnostic> Analyze(AnalysisContext context) {
        foreach (Sentence sentence in context.Sentences) {
            IReadOnlyList<Token> words = sentence.Words;
            for (int i = 0; i + 1 < words.Count; i++) {
                Token adverb = words[i + 1];
                if (!Tags.Contains(words[i].Text) || !adverb.Text.EndsWith("ly", StringComparison.OrdinalIgnoreCase) || (adverb.Length < 5) || NotAdverbs.Contains(adverb.Text)) {
                    continue;
                }
                int start = words[i].End;
                yield return Report(start, adverb.End - start,
                    $"Adverb '{adverb.Text}' in a dialogue tag. Let the dialogue or an action beat carry the emotion.",
                    new TextFix($"Remove '{adverb.Text}'", ""));
            }
        }
    }
}

/// <summary>NW3003: very long sentences.</summary>
public sealed class LongSentenceRule() : GrammarRule("NW3003", "Long sentence", RuleCategory.Style, DiagnosticSeverity.Message,
    "Sentences with more than 40 words can be hard to follow.") {
    public const int MaxWords = 40;

    public override IEnumerable<GrammarDiagnostic> Analyze(AnalysisContext context) {
        foreach (Sentence sentence in context.Sentences) {
            int count = sentence.WordCount;
            if (count > MaxWords) {
                yield return Report(sentence.Offset, sentence.Length, $"Long sentence ({count} words). Consider splitting it.");
            }
        }
    }
}