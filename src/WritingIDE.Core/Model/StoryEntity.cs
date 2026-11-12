using System.Collections.ObjectModel;

namespace WritingIDE.Core.Model;

/// <summary>A key/value attribute of a story element, e.g. "Age = 34".</summary>
public sealed class EntityAttribute : ObservableObject {
    public EntityAttribute() {
    }

    public EntityAttribute(string key, string value) {
        Key = key;
        Value = value;
    }

    public string Key { get; set => SetProperty(ref field, value); } = "";

    public string Value { get; set => SetProperty(ref field, value); } = "";
}

/// <summary>A labelled link from one story element to another, e.g. Wren "sister of" Kael.</summary>
public sealed class EntityRelationship : ObservableObject {
    public EntityRelationship() {
    }

    public EntityRelationship(Guid targetId, string label) {
        TargetId = targetId;
        Label = label;
    }

    public Guid TargetId { get; set => SetProperty(ref field, value); }

    public string Label { get; set => SetProperty(ref field, value); } = "";
}

/// <summary>A story element (character, place, item, ...). The novel equivalent of a class.</summary>
public sealed class StoryEntity : ObservableObject {
    private static readonly char[] AliasSeparators = [',', ';'];

    public StoryEntity(EntityKind kind, string name) {
        Kind = kind;
        Name = name;
        Attributes.CollectionChanged += (_, e) => {
            if (e.NewItems is not null) {
                foreach (EntityAttribute attribute in e.NewItems) {
                    attribute.PropertyChanged += (_, _) => OnPropertyChanged(nameof(Attributes));
                }
            }
            OnPropertyChanged(nameof(Attributes));
        };
        Relationships.CollectionChanged += (_, e) => {
            if (e.NewItems is not null) {
                foreach (EntityRelationship relationship in e.NewItems) {
                    relationship.PropertyChanged += (_, _) => OnPropertyChanged(nameof(Relationships));
                }
            }
            OnPropertyChanged(nameof(Relationships));
        };
    }

    /// <summary>How this element relates to others ("sister of", "rival of"). Drawn on the relationship map.</summary>
    public ObservableCollection<EntityRelationship> Relationships { get; } = [];

    /// <summary>Position on the relationship map as a fraction of its width and height, once placed (null: laid out automatically).</summary>
    public double? MapX { get; set => SetProperty(ref field, value); }

    public double? MapY { get; set => SetProperty(ref field, value); }

    public Guid Id { get; init; } = Guid.NewGuid();

    public EntityKind Kind { get; }

    public EntityKindInfo KindInfo => EntityKindInfo.Get(Kind);

    public string Name { get; set => SetProperty(ref field, value); } = "";

    /// <summary>Comma separated alternative names ("Liz, Lizzie").</summary>
    public string Aliases {
        get;
        set {
            if (SetProperty(ref field, value)) {
                OnPropertyChanged(nameof(AliasList));
            }
        }
    } = "";

    public string Summary { get; set => SetProperty(ref field, value); } = "";

    public string Notes { get; set => SetProperty(ref field, value); } = "";

    public ObservableCollection<EntityAttribute> Attributes { get; } = [];

    public IReadOnlyList<string> AliasList => Aliases
        .Split(AliasSeparators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Where(alias => !string.Equals(alias, Name, StringComparison.Ordinal))
        .Distinct(StringComparer.Ordinal)
        .ToList();

    /// <summary>The name followed by every alias.</summary>
    public IEnumerable<string> AllNames {
        get {
            if (!string.IsNullOrWhiteSpace(Name)) {
                yield return Name.Trim();
            }
            foreach (string alias in AliasList) {
                yield return alias;
            }
        }
    }

    public static StoryEntity CreateWithDefaults(EntityKind kind, string name) {
        StoryEntity entity = new(kind, name);
        foreach (string key in EntityKindInfo.Get(kind).DefaultAttributes) {
            entity.Attributes.Add(new EntityAttribute(key, ""));
        }
        return entity;
    }

    public override string ToString() {
        return $"{KindInfo.DisplayName} {Name}";
    }
}