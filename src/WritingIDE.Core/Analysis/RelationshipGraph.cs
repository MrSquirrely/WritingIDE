using WritingIDE.Core.Model;

namespace WritingIDE.Core.Analysis;

/// <summary>
/// A link on the relationship map: the labels the writer gave it (either direction) and how often the two appear in
/// the same paragraph.
/// </summary>
public sealed record GraphEdge(StoryEntity From, StoryEntity To, IReadOnlyList<string> Labels, int CoOccurrences) {
    public bool IsExplicit => Labels.Count > 0;
}

/// <summary>Builds and lays out the relationship map.</summary>
public static class RelationshipGraph {
    /// <summary>
    /// Edges between <paramref name="entities"/>: every relationship the writer defined, plus pairs mentioned together
    /// in at least <paramref name="minimumCoOccurrences"/> paragraphs (0 turns co-occurrence links off).
    /// </summary>
    public static IReadOnlyList<GraphEdge> Build(IReadOnlyList<StoryEntity> entities, IEnumerable<string> texts, int minimumCoOccurrences) {
        HashSet<StoryEntity> included = [.. entities];
        Dictionary<(Guid, Guid), (StoryEntity A, StoryEntity B, List<string> Labels, int Count)> pairs = [];

        (Guid, Guid) Key(StoryEntity a, StoryEntity b) {
            return a.Id.CompareTo(b.Id) < 0 ? (a.Id, b.Id) : (b.Id, a.Id);
        }

        void Touch(StoryEntity a, StoryEntity b, string? label, int count) {
            (Guid, Guid) key = Key(a, b);
            if (!pairs.TryGetValue(key, out (StoryEntity A, StoryEntity B, List<string> Labels, int Count) entry)) {
                entry = (a, b, [], 0);
            }
            if (!string.IsNullOrWhiteSpace(label)) {
                entry.Labels.Add(label.Trim());
            }
            entry.Count += count;
            pairs[key] = entry;
        }

        Dictionary<Guid, StoryEntity> byId = entities.ToDictionary(static entity => entity.Id);
        foreach (StoryEntity entity in entities) {
            foreach (EntityRelationship relationship in entity.Relationships) {
                if (byId.TryGetValue(relationship.TargetId, out StoryEntity? target) && (target != entity)) {
                    string label = relationship.Label.Trim().Length == 0 ? "related" : relationship.Label.Trim();
                    // "Wren: sister of" reads unambiguously when both directions are labelled.
                    Touch(entity, target, $"{entity.Name}: {label}", 0);
                }
            }
        }

        if (minimumCoOccurrences > 0) {
            EntityIndex index = new(entities);
            foreach (string text in texts) {
                foreach (string paragraph in text.Split('\n')) {
                    if (paragraph.Length < 3) {
                        continue;
                    }
                    List<StoryEntity> present = index.FindMentions(paragraph).Select(static mention => mention.Entity).Where(included.Contains).Distinct().ToList();
                    for (int i = 0; i < present.Count; i++) {
                        for (int j = i + 1; j < present.Count; j++) {
                            Touch(present[i], present[j], null, 1);
                        }
                    }
                }
            }
        }

        return pairs.Values
            .Where(entry => (entry.Labels.Count > 0) || ((minimumCoOccurrences > 0) && (entry.Count >= minimumCoOccurrences)))
            .Select(static entry => new GraphEdge(entry.A, entry.B, entry.Labels, entry.Count))
            .ToList();
    }

    /// <summary>
    /// Force-directed layout (Fruchterman-Reingold), computed in a width x height area. Positions, in and out, are
    /// fractions of the map (0 to 1), so the map can be drawn at any size. Elements with a saved position
    /// (<see cref="StoryEntity.MapX"/>, <see cref="StoryEntity.MapY"/>) stay where the writer put them; the rest start
    /// on a circle (deterministic, so the map does not jump between refreshes).
    /// </summary>
    public static Dictionary<Guid, (double X, double Y)> Layout(IReadOnlyList<StoryEntity> nodes, IReadOnlyList<GraphEdge> edges, double width = 1000, double height = 620, int iterations = 300) {
        Dictionary<Guid, (double X, double Y)> pixels = LayoutPixels(nodes, edges, width, height, iterations);
        return pixels.ToDictionary(static pair => pair.Key, pair => (pair.Value.X / width, pair.Value.Y / height));
    }

    private static Dictionary<Guid, (double X, double Y)> LayoutPixels(IReadOnlyList<StoryEntity> nodes, IReadOnlyList<GraphEdge> edges, double width, double height, int iterations) {
        Dictionary<Guid, (double X, double Y)> positions = [];
        if (nodes.Count == 0) {
            return positions;
        }
        double centerX = width / 2;
        double centerY = height / 2;
        double radius = Math.Min(width, height) * 0.35;
        for (int i = 0; i < nodes.Count; i++) {
            StoryEntity node = nodes[i];
            double angle = (2 * Math.PI * i) / nodes.Count;
            positions[node.Id] = (node.MapX is double x) && (node.MapY is double y)
                ? (x * width, y * height)
                : (centerX + (radius * Math.Cos(angle)), centerY + (radius * Math.Sin(angle)));
        }
        HashSet<Guid> pinned = nodes.Where(static node => node.MapX.HasValue && node.MapY.HasValue).Select(static node => node.Id).ToHashSet();
        if (pinned.Count == nodes.Count) {
            return positions;
        }

        double k = Math.Sqrt((width * height) / nodes.Count) * 0.6;
        double temperature = width / 8;
        for (int iteration = 0; iteration < iterations; iteration++) {
            Dictionary<Guid, (double X, double Y)> displacement = nodes.ToDictionary(static node => node.Id, _ => (0.0, 0.0));
            for (int i = 0; i < nodes.Count; i++) {
                for (int j = i + 1; j < nodes.Count; j++) {
                    (double x1, double y1) = positions[nodes[i].Id];
                    (double x2, double y2) = positions[nodes[j].Id];
                    double dx = x1 - x2;
                    double dy = y1 - y2;
                    double distance = Math.Max(0.01, Math.Sqrt((dx * dx) + (dy * dy)));
                    double force = (k * k) / distance;
                    (double ix, double iy) = displacement[nodes[i].Id];
                    (double jx, double jy) = displacement[nodes[j].Id];
                    displacement[nodes[i].Id] = (ix + ((dx / distance) * force), iy + ((dy / distance) * force));
                    displacement[nodes[j].Id] = (jx - ((dx / distance) * force), jy - ((dy / distance) * force));
                }
            }
            foreach (GraphEdge edge in edges) {
                if (!positions.ContainsKey(edge.From.Id) || !positions.ContainsKey(edge.To.Id)) {
                    continue;
                }
                (double x1, double y1) = positions[edge.From.Id];
                (double x2, double y2) = positions[edge.To.Id];
                double dx = x1 - x2;
                double dy = y1 - y2;
                double distance = Math.Max(0.01, Math.Sqrt((dx * dx) + (dy * dy)));
                double weight = (edge.IsExplicit ? 1.5 : 0) + Math.Log(1 + edge.CoOccurrences, 4);
                double force = ((distance * distance) / k) * Math.Max(0.3, weight);
                (double fx, double fy) = displacement[edge.From.Id];
                (double tx, double ty) = displacement[edge.To.Id];
                displacement[edge.From.Id] = (fx - ((dx / distance) * force), fy - ((dy / distance) * force));
                displacement[edge.To.Id] = (tx + ((dx / distance) * force), ty + ((dy / distance) * force));
            }
            foreach (StoryEntity node in nodes) {
                if (pinned.Contains(node.Id)) {
                    continue;
                }
                (double dx, double dy) = displacement[node.Id];
                double length = Math.Max(0.01, Math.Sqrt((dx * dx) + (dy * dy)));
                (double x, double y) = positions[node.Id];
                x += (dx / length) * Math.Min(length, temperature);
                y += (dy / length) * Math.Min(length, temperature);
                positions[node.Id] = (Math.Clamp(x, width * 0.1, width * 0.9), Math.Clamp(y, height * 0.08, height * 0.92));
            }
            temperature = Math.Max(1, temperature * 0.97);
        }
        return positions;
    }
}