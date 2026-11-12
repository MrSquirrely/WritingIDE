namespace WritingIDE.Core.Model;

/// <summary>The kinds of story elements a project can contain (the "types" of a novel project).</summary>
public enum EntityKind {
    Character,
    Place,
    Item,
    Faction,
    Event,
    Lore
}

/// <summary>Static metadata describing an <see cref="EntityKind"/>: names, icon, colour, IntelliSense trigger keywords and default attributes.</summary>
public sealed record EntityKindInfo(
    EntityKind Kind,
    string DisplayName,
    string PluralName,
    string Glyph,
    string ColorHex,
    IReadOnlyList<string> TriggerKeywords,
    IReadOnlyList<string> DefaultAttributes,
    string Description) {
    public static IReadOnlyList<EntityKindInfo> All { get; } = [
        new(EntityKind.Character, "Character", "Characters", "\uE77B", "#267F99",
            ["person", "character", "char"],
            ["Role", "Age", "Pronouns", "Appearance", "Personality", "Goal"],
            "A person in your story: protagonist, antagonist or supporting cast."),
        new(EntityKind.Place, "Place", "Places", "\uE707", "#098658",
            ["place", "location", "loc"],
            ["Type", "Region", "Atmosphere"],
            "A location: a city, a building, a room, a world."),
        new(EntityKind.Item, "Item", "Items", "\uE7B8", "#795E26",
            ["item", "object"],
            ["Type", "Owner", "Significance"],
            "An object that matters to the plot: an heirloom, a weapon, a letter."),
        new(EntityKind.Faction, "Faction", "Factions", "\uE716", "#0451A5",
            ["faction", "group", "org"],
            ["Leader", "Purpose", "Headquarters"],
            "A group, family, organisation or nation."),
        new(EntityKind.Event, "Event", "Events", "\uE787", "#AF00DB",
            ["event"],
            ["Date", "Location", "Outcome"],
            "Something that happened or will happen: a battle, a wedding, a storm."),
        new(EntityKind.Lore, "Lore", "Lore", "\uE7BE", "#C25E00",
            ["lore", "term", "concept"],
            ["Category", "Rules"],
            "Worldbuilding: a magic system, an institution, a custom, a creature type or a glossary term.")
    ];

    public static EntityKindInfo Get(EntityKind kind) {
        return All[(int)kind];
    }

    /// <summary>Resolves an IntelliSense trigger keyword (e.g. "person") to an entity kind.</summary>
    public static bool TryGetKindForKeyword(string keyword, out EntityKind kind) {
        foreach (EntityKindInfo info in All) {
            foreach (string trigger in info.TriggerKeywords) {
                if (string.Equals(trigger, keyword, StringComparison.OrdinalIgnoreCase)) {
                    kind = info.Kind;
                    return true;
                }
            }
        }
        kind = default;
        return false;
    }
}