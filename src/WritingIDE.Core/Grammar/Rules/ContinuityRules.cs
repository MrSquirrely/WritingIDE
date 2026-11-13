using System.Globalization;
using System.Text.RegularExpressions;

using WritingIDE.Core.Analysis;
using WritingIDE.Core.Model;
using WritingIDE.Core.Text;

namespace WritingIDE.Core.Grammar.Rules;

/// <summary>Shared helpers for rules that compare the manuscript with what the story elements say.</summary>
internal static class Continuity {
    public const string Colors = "amber|blue|green|brown|hazel|grey|gray|black|white|red|gold|golden|silver|violet|purple|yellow|orange|pink|auburn|blond|blonde|russet|chestnut|copper|emerald|sapphire|crimson|scarlet|ginger|brunette|platinum|turquoise|teal|indigo|ebony|ivory|cobalt|jade";

    private static readonly string[] Units = ["zero", "one", "two", "three", "four", "five", "six", "seven", "eight", "nine", "ten", "eleven", "twelve", "thirteen", "fourteen", "fifteen", "sixteen", "seventeen", "eighteen", "nineteen"];

    private static readonly string[] Tens = ["", "", "twenty", "thirty", "forty", "fifty", "sixty", "seventy", "eighty", "ninety"];

    /// <summary>Digits or a spelled number up to ninety-nine ("39", "thirty-nine").</summary>
    public static readonly string Number = $@"\d{{1,3}}|(?:{string.Join("|", Tens.Skip(2))})(?:[- ](?:{string.Join("|", Units.Skip(1).Take(9))}))?|{string.Join("|", Units.Skip(1).Reverse())}";

    public static IEnumerable<StoryEntity> Characters(AnalysisContext context) {
        return context.Options.Entities.Entities.Where(static entity => entity.Kind == EntityKind.Character);
    }

    /// <summary>A regex alternation of the entity's name and aliases, longest first, at word boundaries.</summary>
    public static string NamePattern(StoryEntity entity) {
        string alternation = string.Join("|", entity.AllNames.OrderByDescending(static name => name.Length).Select(Regex.Escape));
        return $@"(?<![\p{{L}}\p{{N}}])(?<name>{alternation})(?![\p{{L}}\p{{N}}])";
    }

    public static string? Attribute(StoryEntity entity, params string[] keys) {
        return entity.Attributes.FirstOrDefault(attribute => keys.Any(key => attribute.Key.Equals(key, StringComparison.OrdinalIgnoreCase)) && (attribute.Value.Trim().Length > 0))?.Value.Trim();
    }

    /// <summary>Everything the project says about an entity: summary, notes and attributes.</summary>
    public static string Description(StoryEntity entity) {
        return string.Join("\n", new[] { entity.Summary, entity.Notes }.Concat(entity.Attributes.Select(static attribute => $"{attribute.Key}: {attribute.Value}")));
    }

    public static string NormalizeColor(string color) {
        return color.ToLowerInvariant() switch {
            "gray" => "grey",
            "blonde" => "blond",
            "golden" => "gold",
            string other => other
        };
    }

    public static int? ParseNumber(string value) {
        value = value.Trim().ToLowerInvariant();
        if (int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out int digits)) {
            return digits;
        }
        string[] parts = value.Split(['-', ' '], StringSplitOptions.RemoveEmptyEntries);
        int total = 0;
        foreach (string part in parts) {
            int ten = Array.IndexOf(Tens, part);
            int unit = Array.IndexOf(Units, part);
            if (ten >= 2) {
                total += ten * 10;
            }
            else if (unit >= 0) {
                total += unit;
            }
            else {
                return null;
            }
        }
        return parts.Length == 0 ? null : total;
    }

    public static string ToWords(int number) {
        if ((number < 0) || (number > 99)) {
            return number.ToString(CultureInfo.InvariantCulture);
        }
        if (number < 20) {
            return Units[number];
        }
        return number % 10 == 0 ? Tens[number / 10] : $"{Tens[number / 10]}-{Units[number % 10]}";
    }

    private const string NotAnAge = @"(?![\p{L}\d-]|\s+(?:minutes?|hours?|days?|weeks?|months?|steps?|feet|foot|miles?|yards?|times?|percent|paces?|seconds?|of|in|and|or|to|more|people|men|women))";

    /// <summary>
    /// Ages written next to a character's name ("Rowan, thirty-nine,", "the 41-year-old Rowan", "Rowan was 35"), once
    /// each, ignoring numbers under 10.
    /// </summary>
    public static IEnumerable<(Group Age, int Written)> FindStatedAges(string text, StoryEntity character) {
        string names = NamePattern(character);
        Regex[] patterns = [
            new($@"{names},\s+(?:who\s+was\s+|aged\s+|age\s+)?(?<age>{Number})(?:,|\s+years?\s+old)", RegexOptions.IgnoreCase),
            new($@"\b(?<age>{Number})-year-old\s+(?:[\p{{L}}]+\s+){{0,2}}?{names}", RegexOptions.IgnoreCase),
            new($@"{names}\s+(?:was|is|had\s+turned|turned|had\s+been)\s+(?<age>{Number})(?:\s+years?\s+old)?{NotAnAge}", RegexOptions.IgnoreCase)
        ];
        HashSet<int> reported = [];
        foreach (Match match in patterns.SelectMany(pattern => pattern.Matches(text))) {
            Group group = match.Groups["age"];
            int? written = ParseNumber(group.Value);
            if ((written is int value) && (value >= 10) && reported.Add(group.Index)) {
                yield return (group, value);
            }
        }
    }

    /// <summary>True when the offset is inside a quotation in its paragraph (dialogue or reported speech).</summary>
    public static bool IsInsideQuotes(string text, int offset) {
        int paragraphStart = offset;
        while ((paragraphStart > 0) && text[paragraphStart - 1] is not ('\n' or '\r')) {
            paragraphStart--;
        }
        bool inside = false;
        for (int i = paragraphStart; i < offset; i++) {
            char c = text[i];
            if (c == '\u201C') {
                inside = true;
            }
            else if (c == '\u201D') {
                inside = false;
            }
            else if (c == '"') {
                inside = !inside;
            }
        }
        return inside;
    }
}

/// <summary>NW4002: a word that is almost a story element's name ("Rowen" for "Rowan").</summary>
public sealed class NameTypoRule() : GrammarRule("NW4002", "Misspelled story element name", RuleCategory.Continuity, DiagnosticSeverity.Warning,
    "Finds capitalized words that are one or two letters away from a character, place or other story element name.") {
    public override IEnumerable<GrammarDiagnostic> Analyze(AnalysisContext context) {
        foreach (Token token in context.Tokens) {
            if ((token.Kind != TokenKind.Word) || (token.Length < 4) || !char.IsUpper(token.Text[0])) {
                continue;
            }
            string? name = context.FindSimilarName(token.Text);
            if ((name is null) || (context.Spelling?.IsCorrect(token.Text) ?? false)) {
                continue;
            }
            string word = Tokenizer.StripPossessive(token.Text);
            string suffix = token.Text[word.Length..];
            yield return Report(token.Offset, token.Length, $"'{word}' looks like a misspelling of the story element name '{name}'.", TextFix.ReplaceWith(name + suffix));
        }
    }
}

/// <summary>NW4003: eye or hair colour that contradicts the character's description.</summary>
public sealed class AppearanceContinuityRule() : GrammarRule("NW4003", "Appearance continuity", RuleCategory.Continuity, DiagnosticSeverity.Warning,
    "Checks eye and hair colour written next to a character's name ('Kael's blue eyes') against the colours in the character's description and attributes.") {
    private static readonly (string Noun, string Pattern, string[] Keys)[] Features = [
        ("eyes", "eyes?", ["Eyes", "Eye color", "Eye colour", "Eye"]),
        ("hair", "hair", ["Hair", "Hair color", "Hair colour"])
    ];

    public override IEnumerable<GrammarDiagnostic> Analyze(AnalysisContext context) {
        foreach (StoryEntity character in Continuity.Characters(context)) {
            string description = Continuity.Description(character);
            string names = Continuity.NamePattern(character);
            foreach ((string noun, string nounPattern, string[] keys) in Features) {
                HashSet<string> known = KnownColors(description, nounPattern, Continuity.Attribute(character, keys));
                if (known.Count == 0) {
                    continue;
                }
                Regex before = new($@"{names}(?:'s|\u2019s)\s+(?:[\p{{L}}-]+\s+){{0,2}}?(?<color>{Continuity.Colors})(?:[- ](?<color2>{Continuity.Colors}))?\s+{nounPattern}\b", RegexOptions.IgnoreCase);
                Regex after = new($@"{names}(?:'s|\u2019s)\s+{nounPattern}\s+(?:were|was|are|is|,|\u2014)\s*(?:[\p{{L}}]+\s+)?(?<color>{Continuity.Colors})\b", RegexOptions.IgnoreCase);
                foreach (Match match in before.Matches(context.Text).Concat(after.Matches(context.Text))) {
                    foreach (Group group in new[] { match.Groups["color"], match.Groups["color2"] }.Where(static group => group.Success)) {
                        if (known.Contains(Continuity.NormalizeColor(group.Value))) {
                            continue;
                        }
                        string expected = known.First();
                        yield return Report(group.Index, group.Length,
                            $"{character.Name}'s {noun} are {string.Join(" or ", known)} in the story elements, but '{group.Value}' here.",
                            TextFix.ReplaceWith(MatchCase(group.Value, expected)));
                    }
                }
            }
        }
    }

    private static HashSet<string> KnownColors(string description, string nounPattern, string? attribute) {
        HashSet<string> colors = [];
        foreach (Match match in Regex.Matches(description, $@"\b(?<color>{Continuity.Colors})(?:[- ](?<color2>{Continuity.Colors}))?\s+{nounPattern}\b", RegexOptions.IgnoreCase)) {
            colors.Add(Continuity.NormalizeColor(match.Groups["color"].Value));
            if (match.Groups["color2"].Success) {
                colors.Add(Continuity.NormalizeColor(match.Groups["color2"].Value));
            }
        }
        if (attribute is not null) {
            foreach (Match match in Regex.Matches(attribute, $@"\b(?:{Continuity.Colors})\b", RegexOptions.IgnoreCase)) {
                colors.Add(Continuity.NormalizeColor(match.Value));
            }
        }
        return colors;
    }
}

/// <summary>NW4004: a character's age that contradicts the Age attribute.</summary>
public sealed class AgeContinuityRule() : GrammarRule("NW4004", "Age continuity", RuleCategory.Continuity, DiagnosticSeverity.Warning,
    "Checks ages written next to a character's name ('Rowan, thirty-nine,', 'the 41-year-old Rowan') against the character's Age attribute.") {
    public override IEnumerable<GrammarDiagnostic> Analyze(AnalysisContext context) {
        foreach (StoryEntity character in Continuity.Characters(context)) {
            string? ageText = Continuity.Attribute(character, "Age");
            Match ageDigits = ageText is null ? Match.Empty : Regex.Match(ageText, @"\d{1,3}");
            if (!ageDigits.Success) {
                continue;
            }
            int age = int.Parse(ageDigits.Value, CultureInfo.InvariantCulture);
            foreach ((Group group, int written) in Continuity.FindStatedAges(context.Text, character)) {
                if (written == age) {
                    continue;
                }
                string replacement = char.IsDigit(group.Value[0]) ? age.ToString(CultureInfo.InvariantCulture) : MatchCase(group.Value, Continuity.ToWords(age));
                yield return Report(group.Index, group.Length, $"{character.Name} is {age} in the story elements, but {written} here.", TextFix.ReplaceWith(replacement));
            }
        }
    }
}

/// <summary>NW4005: a possessive pronoun that contradicts the character's pronouns ("Wren tossed his hair" for she/her).</summary>
public sealed class PronounContinuityRule() : GrammarRule("NW4005", "Pronoun continuity", RuleCategory.Continuity, DiagnosticSeverity.Warning,
    "Checks self-directed gestures ('Rowan flicked his ears') against the character's Pronouns attribute.") {
    private const string Verbs = "nodded|shook|tilted|cocked|scratched|rubbed|crossed|folded|raised|lowered|flicked|tucked|cleared|clenched|unclenched|narrowed|bowed|ducked|turned|lifted|dropped|bit|licked|pursed|wrung|cracked|rolled|wiped|squared|hunched|shrugged|stretched|pressed|shut|closed|opened|furrowed|arched|curled|flexed|twitched|tossed|smoothed|straightened";

    private const string Body = "head|ears?|tail|eyes?|hands?|arms?|shoulders?|throat|lips?|jaw|brows?|muzzle|antlers|chin|fists?|fingers?|neck|nose|whiskers|knuckles|teeth|mouth|forehead|paws?|claws?|hair|fur|mane|face";

    public override IEnumerable<GrammarDiagnostic> Analyze(AnalysisContext context) {
        foreach (StoryEntity character in Continuity.Characters(context)) {
            string? expected = ExpectedPossessive(Continuity.Attribute(character, "Pronouns", "Pronoun"));
            if (expected is null) {
                continue;
            }
            Regex pattern = new($@"{Continuity.NamePattern(character)}\s+(?:[\p{{L}}]+ly\s+)?(?:{Verbs})\s+(?<pronoun>his|her|their)\s+(?:[\p{{L}}-]+\s+)?(?:{Body})\b", RegexOptions.IgnoreCase);
            foreach (Match match in pattern.Matches(context.Text)) {
                Group pronoun = match.Groups["pronoun"];
                if (pronoun.Value.Equals(expected, StringComparison.OrdinalIgnoreCase)) {
                    continue;
                }
                yield return Report(pronoun.Index, pronoun.Length,
                    $"{character.Name} uses {Continuity.Attribute(character, "Pronouns", "Pronoun")} pronouns; expected '{expected}' here.",
                    TextFix.ReplaceWith(MatchCase(pronoun.Value, expected)));
            }
        }
    }

    internal static string? ExpectedPossessive(string? pronouns) {
        if (pronouns is null) {
            return null;
        }
        string[] words = pronouns.ToLowerInvariant().Split(['/', ',', ' ', ';'], StringSplitOptions.RemoveEmptyEntries);
        if (words.Contains("they") || words.Contains("them")) {
            return words.Contains("she") || words.Contains("he") ? null : "their";
        }
        if (words.Contains("she") || words.Contains("hers")) {
            return words.Contains("he") ? null : "her";
        }
        return words.Contains("he") || words.Contains("him") ? "his" : null;
    }
}

/// <summary>NW4006: a character who died earlier in the manuscript speaks or acts.</summary>
public sealed class DeathContinuityRule() : GrammarRule("NW4006", "Dead character continuity", RuleCategory.Continuity, DiagnosticSeverity.Warning,
    "Flags characters who speak or act after the chapter their 'Died' attribute names (dialogue and memories in quotes are ignored).") {
    private const string Actions = "said|says|asked|asks|replied|whispered|shouted|yelled|muttered|answered|called|laughed|smiled|grinned|nodded|walked|ran|stood|sat|turned|looked|reached|took|grabbed|stepped|entered|arrived";

    public override IEnumerable<GrammarDiagnostic> Analyze(AnalysisContext context) {
        ManuscriptPosition? position = context.Options.Position;
        if (position is null) {
            yield break;
        }
        foreach (StoryEntity character in Continuity.Characters(context)) {
            int? deathIndex = FindDeathChapter(character, position);
            if ((deathIndex is null) || (position.Index <= deathIndex)) {
                continue;
            }
            string names = Continuity.NamePattern(character);
            Regex[] patterns = [
                new($@"{names}\s+(?:[\p{{L}}]+ly\s+)?(?<verb>{Actions})\b"),
                new($@"\b(?<verb>said|asked|whispered|shouted|replied|called)\s+{names}")
            ];
            HashSet<int> reported = [];
            foreach (Match match in patterns.SelectMany(pattern => pattern.Matches(context.Text))) {
                Group name = match.Groups["name"];
                if (Continuity.IsInsideQuotes(context.Text, name.Index) || !reported.Add(name.Index)) {
                    continue;
                }
                yield return Report(name.Index, name.Length,
                    $"{character.Name} died in '{position.ChapterTitles[deathIndex.Value]}', but {match.Groups["verb"].Value} here. A flashback or a ghost? Otherwise check the timeline.");
            }
        }
    }

    internal static int? FindDeathChapter(StoryEntity character, ManuscriptPosition position) {
        string? died = Continuity.Attribute(character, "Died", "Death", "Died in", "Dies in", "Death chapter", "Killed in");
        if (died is not null) {
            return position.Resolve(died) ?? ResolveEmbeddedChapter(died, position);
        }
        string? status = Continuity.Attribute(character, "Status", "State", "Alive");
        if ((status is not null) && Regex.IsMatch(status, @"\b(?:dead|deceased|died|killed|slain|no)\b", RegexOptions.IgnoreCase)) {
            return ResolveEmbeddedChapter(status, position);
        }
        return null;
    }

    private static int? ResolveEmbeddedChapter(string text, ManuscriptPosition position) {
        Match chapter = Regex.Match(text, @"(?:chapter|ch\.?)\s*\d{1,3}", RegexOptions.IgnoreCase);
        return chapter.Success ? position.Resolve(chapter.Value) : null;
    }
}