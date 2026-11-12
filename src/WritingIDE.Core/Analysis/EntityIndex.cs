using System.Text.RegularExpressions;

using WritingIDE.Core.Model;

namespace WritingIDE.Core.Analysis;

/// <summary>An occurrence of a story element's name (or alias) in the text.</summary>
public sealed record EntityMention(int Offset, int Length, StoryEntity Entity, string MatchedName) {
    public int End => Offset + Length;
}

/// <summary>
/// Immutable snapshot of every story element name and alias, able to find mentions in text.
/// Built on the UI thread, safe to use from background analysis.
/// </summary>
public sealed class EntityIndex {
    private readonly Dictionary<string, StoryEntity> _byName;
    private readonly Regex? _regex;

    public EntityIndex(IEnumerable<StoryEntity> entities) {
        List<(string Name, StoryEntity Entity)> names = [];
        _byName = new Dictionary<string, StoryEntity>(StringComparer.Ordinal);
        foreach (StoryEntity entity in entities) {
            foreach (string name in entity.AllNames) {
                if ((name.Length > 0) && _byName.TryAdd(name, entity)) {
                    names.Add((name, entity));
                }
            }
        }
        Names = names;
        Entities = names.Select(static entry => entry.Entity).Distinct().ToList();
        if (names.Count > 0) {
            string alternation = string.Join("|", names.Select(static entry => entry.Name).OrderByDescending(static name => name.Length).Select(Regex.Escape));
            _regex = new Regex($@"(?<![\p{{L}}\p{{N}}])(?:{alternation})(?![\p{{L}}\p{{N}}])", RegexOptions.CultureInvariant);
        }
    }

    public static EntityIndex Empty { get; } = new([]);

    public IReadOnlyList<(string Name, StoryEntity Entity)> Names { get; }

    public IReadOnlyList<StoryEntity> Entities { get; }

    public StoryEntity? FindByName(string name) {
        if (_byName.TryGetValue(name, out StoryEntity? entity)) {
            return entity;
        }
        return _byName.FirstOrDefault(pair => string.Equals(pair.Key, name, StringComparison.OrdinalIgnoreCase)).Value;
    }

    public IReadOnlyList<EntityMention> FindMentions(string text) {
        if (_regex is null) {
            return [];
        }
        List<EntityMention> mentions = [];
        foreach (Match match in _regex.Matches(text)) {
            if (_byName.TryGetValue(match.Value, out StoryEntity? entity)) {
                mentions.Add(new EntityMention(match.Index, match.Length, entity, match.Value));
            }
        }
        return mentions;
    }

    /// <summary>Finds mentions of a single entity (its name and all aliases).</summary>
    public static IReadOnlyList<EntityMention> FindMentions(string text, StoryEntity entity) {
        return new EntityIndex([entity]).FindMentions(text);
    }
}