using System.Text.RegularExpressions;

using WritingIDE.Core.Model;

namespace WritingIDE.Core.Analysis;

/// <summary>A chapter and its current text, in reading order.</summary>
public sealed record ChapterText(Chapter Chapter, string Text);

/// <summary>One character across the manuscript.</summary>
public sealed record CharacterStats(
    StoryEntity Character,
    int Mentions,
    int Chapters,
    int PovChapters,
    int DialogueLines,
    int DialogueWords,
    int? FirstChapterIndex,
    int? LastChapterIndex,
    IReadOnlyList<int> MentionsPerChapter);

/// <summary>Whose point of view a chapter is in, and whether that was set by the writer or detected.</summary>
public sealed record ChapterPointOfView(Chapter Chapter, int Index, string PointOfView, bool IsDetected, int Words) {
    public const string FirstPerson = "First person";
    public const string Unknown = "";
}

public sealed record CharacterReport(IReadOnlyList<CharacterStats> Characters, IReadOnlyList<ChapterPointOfView> Chapters);

/// <summary>Character presence, dialogue and point-of-view statistics for the whole manuscript.</summary>
public static partial class CharacterStatistics {
    /// <summary>How far outside a quotation a speaker tag is looked for ("..." said Kael / Kael said, "...").</summary>
    private const int AttributionWindow = 60;

    public static CharacterReport Compute(IReadOnlyList<ChapterText> chapters, IEnumerable<StoryEntity> entities) {
        List<StoryEntity> characters = entities.Where(static entity => entity.Kind == EntityKind.Character).ToList();
        EntityIndex index = new(characters);
        Dictionary<StoryEntity, int[]> perChapter = characters.ToDictionary(static character => character, _ => new int[chapters.Count]);
        Dictionary<StoryEntity, int> povChapters = characters.ToDictionary(static character => character, _ => 0);
        Dictionary<StoryEntity, (int Lines, int Words)> dialogue = characters.ToDictionary(static character => character, _ => (0, 0));
        List<ChapterPointOfView> povs = [];

        for (int i = 0; i < chapters.Count; i++) {
            (Chapter chapter, string text) = chapters[i];
            IReadOnlyList<EntityMention> mentions = index.FindMentions(text);
            IReadOnlyList<(int Start, int End)> quotes = FindQuotations(text);
            foreach (EntityMention mention in mentions) {
                perChapter[mention.Entity][i]++;
            }
            foreach ((int start, int end) in quotes) {
                StoryEntity? speaker = FindSpeaker(text, start, end, mentions, quotes);
                if (speaker is not null) {
                    (int lines, int words) = dialogue[speaker];
                    dialogue[speaker] = (lines + 1, words + TextStatistics.CountWords(text[start..end]));
                }
            }

            ChapterPointOfView pov = DetectPointOfView(chapter, i, text, mentions, quotes, index);
            povs.Add(pov);
            StoryEntity? povCharacter = index.FindByName(pov.PointOfView);
            if (povCharacter is not null) {
                povChapters[povCharacter]++;
            }
        }

        List<CharacterStats> stats = characters.Select(character => {
            int[] counts = perChapter[character];
            int first = Array.FindIndex(counts, static count => count > 0);
            int last = Array.FindLastIndex(counts, static count => count > 0);
            (int lines, int words) = dialogue[character];
            return new CharacterStats(character, counts.Sum(), counts.Count(static count => count > 0), povChapters[character], lines, words,
                first < 0 ? null : first, last < 0 ? null : last, counts);
        })
        .OrderByDescending(static stat => stat.Mentions)
        .ThenBy(static stat => stat.Character.Name, StringComparer.CurrentCultureIgnoreCase)
        .ToList();
        return new CharacterReport(stats, povs);
    }

    /// <summary>
    /// The chapter's POV: the writer's setting if there is one; otherwise first person when the narration is full of
    /// "I/me/my", else the character named most often outside dialogue.
    /// </summary>
    public static ChapterPointOfView DetectPointOfView(Chapter chapter, int chapterIndex, string text, IReadOnlyList<EntityMention> mentions, IReadOnlyList<(int Start, int End)> quotes, EntityIndex characters) {
        int words = TextStatistics.CountWords(text);
        if (!string.IsNullOrWhiteSpace(chapter.PointOfView)) {
            string name = characters.FindByName(chapter.PointOfView.Trim())?.Name ?? chapter.PointOfView.Trim();
            return new ChapterPointOfView(chapter, chapterIndex, name, false, words);
        }
        string narration = RemoveRanges(text, quotes);
        int narrationWords = TextStatistics.CountWords(narration);
        if (narrationWords == 0) {
            return new ChapterPointOfView(chapter, chapterIndex, ChapterPointOfView.Unknown, true, words);
        }
        int firstPerson = FirstPersonPronoun().Matches(narration).Count;
        if ((firstPerson >= 3) && ((double)firstPerson / narrationWords >= 0.02)) {
            return new ChapterPointOfView(chapter, chapterIndex, ChapterPointOfView.FirstPerson, true, words);
        }
        StoryEntity? top = mentions
            .Where(mention => !IsInside(mention.Offset, quotes))
            .GroupBy(static mention => mention.Entity)
            .Select(static group => (Entity: group.Key, Count: group.Count()))
            .Where(static entry => entry.Count >= 2)
            .OrderByDescending(static entry => entry.Count)
            .Select(static entry => entry.Entity)
            .FirstOrDefault();
        return new ChapterPointOfView(chapter, chapterIndex, top?.Name ?? ChapterPointOfView.Unknown, true, words);
    }

    /// <summary>Spans of quoted speech (inside the quotation marks), straight or curly; an unclosed quote ends at the paragraph.</summary>
    public static IReadOnlyList<(int Start, int End)> FindQuotations(string text) {
        List<(int, int)> spans = [];
        int open = -1;
        for (int i = 0; i < text.Length; i++) {
            char c = text[i];
            if (c == '\n') {
                if (open >= 0) {
                    spans.Add((open, i));
                    open = -1;
                }
                continue;
            }
            bool opening = (c == '\u201C') || ((c == '"') && (open < 0));
            bool closing = (c == '\u201D') || ((c == '"') && (open >= 0));
            if (opening && (open < 0)) {
                open = i + 1;
            }
            else if (closing && (open >= 0)) {
                spans.Add((open, i));
                open = -1;
            }
        }
        if (open >= 0) {
            spans.Add((open, text.Length));
        }
        return spans;
    }

    /// <summary>The character a quotation is attributed to: a name just after it in the same paragraph, else just before it.</summary>
    private static StoryEntity? FindSpeaker(string text, int start, int end, IReadOnlyList<EntityMention> mentions, IReadOnlyList<(int Start, int End)> quotes) {
        int afterStart = Math.Min(text.Length, end + 1);
        int afterEnd = Limit(text, afterStart, AttributionWindow, forward: true, quotes);
        EntityMention? after = mentions.FirstOrDefault(mention => (mention.Offset >= afterStart) && (mention.Offset < afterEnd));
        if (after is not null) {
            return after.Entity;
        }
        int beforeEnd = Math.Max(0, start - 1);
        int beforeStart = Limit(text, beforeEnd, AttributionWindow, forward: false, quotes);
        return mentions.LastOrDefault(mention => (mention.Offset >= beforeStart) && (mention.End <= beforeEnd))?.Entity;
    }

    /// <summary>The far end of an attribution window: stops at a line break, another quotation, or the window size.</summary>
    private static int Limit(string text, int from, int window, bool forward, IReadOnlyList<(int Start, int End)> quotes) {
        int position = from;
        for (int step = 0; step < window; step++) {
            int next = forward ? position : position - 1;
            if ((next < 0) || (next >= text.Length) || (text[next] == '\n') || (text[next] is '"' or '\u201C' or '\u201D')) {
                break;
            }
            position = forward ? position + 1 : position - 1;
        }
        return position;
    }

    private static bool IsInside(int offset, IReadOnlyList<(int Start, int End)> ranges) {
        foreach ((int start, int end) in ranges) {
            if ((offset >= start) && (offset < end)) {
                return true;
            }
        }
        return false;
    }

    private static string RemoveRanges(string text, IReadOnlyList<(int Start, int End)> ranges) {
        if (ranges.Count == 0) {
            return text;
        }
        System.Text.StringBuilder builder = new(text.Length);
        int position = 0;
        foreach ((int start, int end) in ranges) {
            builder.Append(text, position, start - position).Append(' ');
            position = end;
        }
        builder.Append(text, position, text.Length - position);
        return builder.ToString();
    }

    [GeneratedRegex(@"\b(?:I|me|my|mine|myself|I'm|I'd|I'll|I've|I\u2019m|I\u2019d|I\u2019ll|I\u2019ve)\b")]
    private static partial Regex FirstPersonPronoun();
}