using System.Globalization;
using System.Text.RegularExpressions;

using WritingIDE.Core.Model;

namespace WritingIDE.Core.Grammar.Rules;

/// <summary>NW4007: an item described as someone else's ("Wren's compass") when its Owner attribute names another character.</summary>
public sealed class ItemOwnershipRule() : GrammarRule("NW4007", "Item ownership", RuleCategory.Continuity, DiagnosticSeverity.Warning,
    "Checks possessives in front of an item ('Wren's compass') against the item's Owner attribute.") {
    public override IEnumerable<GrammarDiagnostic> Analyze(AnalysisContext context) {
        List<StoryEntity> characters = Continuity.Characters(context).ToList();
        foreach (StoryEntity item in context.Options.Entities.Entities.Where(static entity => entity.Kind == EntityKind.Item)) {
            string? ownerName = Continuity.Attribute(item, "Owner", "Belongs to", "Owned by");
            StoryEntity? owner = ownerName is null ? null : context.Options.Entities.FindByName(ownerName);
            if (owner is null) {
                continue;
            }
            string itemNames = string.Join("|", item.AllNames.Concat(item.AllNames.Select(static name => name.Split(' ')[^1]))
                .Where(static name => name.Length > 2).Distinct(StringComparer.OrdinalIgnoreCase).OrderByDescending(static name => name.Length).Select(Regex.Escape));
            foreach (StoryEntity character in characters.Where(character => character != owner)) {
                Regex pattern = new($@"{Continuity.NamePattern(character)}(?:'s|\u2019s)\s+(?:[\p{{L}}-]+\s+){{0,2}}?(?<item>{itemNames})(?![\p{{L}}\p{{N}}])", RegexOptions.IgnoreCase);
                foreach (Match match in pattern.Matches(context.Text)) {
                    if (Continuity.IsInsideQuotes(context.Text, match.Index)) {
                        continue;
                    }
                    Group name = match.Groups["name"];
                    yield return Report(name.Index, match.Length, $"{item.Name} belongs to {owner.Name} in the story elements, not {character.Name}. Update its Owner attribute if it changed hands.");
                }
            }
        }
    }
}

/// <summary>NW4008: an age that does not fit the character's birth year and the chapter's story date.</summary>
public sealed partial class BirthYearAgeRule() : GrammarRule("NW4008", "Age from birth year", RuleCategory.Continuity, DiagnosticSeverity.Warning,
    "When a character has a Born year and the chapter a story date with a year, checks ages written next to their name.") {
    public override IEnumerable<GrammarDiagnostic> Analyze(AnalysisContext context) {
        Match storyYear = Year().Match(context.Options.StoryDate);
        if (!storyYear.Success) {
            yield break;
        }
        int year = int.Parse(storyYear.Value, CultureInfo.InvariantCulture);
        foreach (StoryEntity character in Continuity.Characters(context)) {
            if (Continuity.Attribute(character, "Age") is not null) {
                // NW4004 checks against the Age attribute instead.
                continue;
            }
            Match born = Year().Match(Continuity.Attribute(character, "Born", "Birth year", "Year of birth", "Birth") ?? "");
            if (!born.Success) {
                continue;
            }
            int expected = year - int.Parse(born.Value, CultureInfo.InvariantCulture);
            if ((expected < 0) || (expected > 150)) {
                continue;
            }
            foreach ((Group group, int written) in Continuity.FindStatedAges(context.Text, character)) {
                // A birthday may not have come round yet in the story year.
                if ((written != expected) && (written != expected - 1)) {
                    yield return Report(group.Index, group.Length, $"{character.Name} was born in {born.Value}, so is {expected - 1} or {expected} in {year}, not {written}.");
                }
            }
        }
    }

    [GeneratedRegex(@"(?<!\d)\d{3,4}(?!\d)")]
    private static partial Regex Year();
}

/// <summary>NW4009: a family or other relationship in the text ("Kael's sister Wren") that contradicts the defined relationships.</summary>
public sealed class RelationshipContinuityRule() : GrammarRule("NW4009", "Relationship continuity", RuleCategory.Continuity, DiagnosticSeverity.Warning,
    "Checks phrases like 'Kael's sister, Wren' against the relationships defined on the characters' pages.") {
    private static readonly Dictionary<string, string> Groups = new(StringComparer.OrdinalIgnoreCase) {
        ["sister"] = "sibling", ["brother"] = "sibling", ["sibling"] = "sibling", ["twin"] = "sibling", ["half-sister"] = "sibling", ["half-brother"] = "sibling",
        ["mother"] = "parent", ["father"] = "parent", ["parent"] = "parent", ["mum"] = "parent", ["mom"] = "parent", ["dad"] = "parent", ["stepmother"] = "parent", ["stepfather"] = "parent",
        ["son"] = "child", ["daughter"] = "child", ["child"] = "child", ["stepson"] = "child", ["stepdaughter"] = "child",
        ["wife"] = "spouse", ["husband"] = "spouse", ["spouse"] = "spouse", ["married"] = "spouse",
        ["fiancee"] = "betrothed", ["fiancée"] = "betrothed", ["fiance"] = "betrothed", ["fiancé"] = "betrothed", ["betrothed"] = "betrothed",
        ["cousin"] = "cousin",
        ["aunt"] = "elder-relative", ["uncle"] = "elder-relative",
        ["niece"] = "younger-relative", ["nephew"] = "younger-relative",
        ["grandmother"] = "grandparent", ["grandfather"] = "grandparent", ["grandparent"] = "grandparent",
        ["granddaughter"] = "grandchild", ["grandson"] = "grandchild", ["grandchild"] = "grandchild",
        ["mentor"] = "mentor", ["master"] = "mentor", ["teacher"] = "mentor",
        ["apprentice"] = "student", ["student"] = "student", ["pupil"] = "student"
    };

    private static readonly Dictionary<string, string> Reciprocal = new() {
        ["sibling"] = "sibling", ["parent"] = "child", ["child"] = "parent", ["spouse"] = "spouse", ["betrothed"] = "betrothed", ["cousin"] = "cousin",
        ["elder-relative"] = "younger-relative", ["younger-relative"] = "elder-relative", ["grandparent"] = "grandchild", ["grandchild"] = "grandparent",
        ["mentor"] = "student", ["student"] = "mentor"
    };

    public override IEnumerable<GrammarDiagnostic> Analyze(AnalysisContext context) {
        List<StoryEntity> characters = Continuity.Characters(context).ToList();
        if (!characters.Any(static character => character.Relationships.Count > 0)) {
            yield break;
        }
        string words = string.Join("|", Groups.Keys.OrderByDescending(static word => word.Length).Select(Regex.Escape));
        foreach (StoryEntity owner in characters) {
            foreach (StoryEntity relative in characters.Where(other => other != owner)) {
                List<(string Label, bool FromRelative)> labels = [
                    .. relative.Relationships.Where(r => r.TargetId == owner.Id).Select(static r => (r.Label, true)),
                    .. owner.Relationships.Where(r => r.TargetId == relative.Id).Select(static r => (r.Label, false))
                ];
                // Only judge pairs whose defined relationship is one this rule understands.
                List<(string Group, bool FromRelative, string Label)> known = labels
                    .Select(label => (Group: GroupOf(label.Label), label.FromRelative, label.Label))
                    .Where(static entry => entry.Group is not null)
                    .Select(static entry => (entry.Group!, entry.FromRelative, entry.Label))
                    .ToList();
                if (known.Count == 0) {
                    continue;
                }
                Regex pattern = new($@"{Continuity.NamePattern(owner)}(?:'s|\u2019s)\s+(?<word>{words}),?\s+{Continuity.NamePattern(relative).Replace("?<name>", "?<relative>")}", RegexOptions.IgnoreCase);
                foreach (Match match in pattern.Matches(context.Text)) {
                    string stated = Groups[match.Groups["word"].Value];
                    bool consistent = known.Any(entry => entry.FromRelative ? entry.Group == stated : entry.Group == Reciprocal[stated]);
                    if (!consistent) {
                        Group word = match.Groups["word"];
                        string defined = string.Join("; ", known.Select(entry => $"{(entry.FromRelative ? relative.Name : owner.Name)}: {entry.Label}"));
                        yield return Report(word.Index, word.Length, $"Here {relative.Name} is {owner.Name}'s {word.Value}, but the story elements say: {defined}.");
                    }
                }
            }
        }
    }

    private static string? GroupOf(string label) {
        foreach (Match word in Regex.Matches(label, @"[\p{L}-]+")) {
            if (Groups.TryGetValue(word.Value, out string? group)) {
                return group;
            }
        }
        return null;
    }
}