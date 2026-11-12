using System.Windows;
using System.Windows.Media;

using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Rendering;

using WritingIDE.Core.Analysis;
using WritingIDE.Infrastructure;

namespace WritingIDE.Editor;

public sealed class MentionSegment(EntityMention mention) : TextSegment {
    public EntityMention Mention { get; } = mention;
}

/// <summary>"Semantic highlighting": colours every mention of a character, place, item... by its kind.</summary>
public sealed class EntityHighlighter : DocumentColorizingTransformer {
    private readonly TextSegmentCollection<MentionSegment> _mentions;

    public EntityHighlighter(TextDocument document) {
        _mentions = new TextSegmentCollection<MentionSegment>(document);
    }

    public bool IsEnabled { get; set; } = true;

    public void SetMentions(IEnumerable<EntityMention> mentions, int documentLength) {
        _mentions.Clear();
        foreach (EntityMention mention in mentions) {
            if (mention.End <= documentLength) {
                _mentions.Add(new MentionSegment(mention) { StartOffset = mention.Offset, Length = mention.Length });
            }
        }
    }

    public MentionSegment? GetMentionAt(int offset) {
        return _mentions.FindSegmentsContaining(offset).FirstOrDefault();
    }

    protected override void ColorizeLine(DocumentLine line) {
        if (!IsEnabled) {
            return;
        }
        foreach (MentionSegment segment in _mentions.FindOverlappingSegments(line.Offset, line.Length)) {
            int start = Math.Max(segment.StartOffset, line.Offset);
            int end = Math.Min(segment.EndOffset, line.EndOffset);
            if (end <= start) {
                continue;
            }
            Brush brush = Visuals.GetFrozenKindBrush(segment.Mention.Entity.Kind);
            ChangeLinePart(start, end, element => {
                element.TextRunProperties.SetForegroundBrush(brush);
                Typeface typeface = element.TextRunProperties.Typeface;
                element.TextRunProperties.SetTypeface(new Typeface(typeface.FontFamily, typeface.Style, FontWeights.SemiBold, typeface.Stretch));
            });
        }
    }
}