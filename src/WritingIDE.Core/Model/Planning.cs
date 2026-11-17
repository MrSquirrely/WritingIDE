using System.Collections.ObjectModel;

namespace WritingIDE.Core.Model;

/// <summary>One day in the writing log.</summary>
/// <param name="Written">Words added that day, net of deletions.</param>
/// <param name="Total">The manuscript's length at the end of the day.</param>
public sealed record WritingDay(DateOnly Date, int Written, int Total);

/// <summary>A story beat: a turning point a structure expects at a certain place.</summary>
public sealed class Beat : ObservableObject {
    public string Name { get; set => SetProperty(ref field, value); } = "";

    /// <summary>What the beat is for, from the template.</summary>
    public string Description { get; set => SetProperty(ref field, value); } = "";

    /// <summary>Where the template expects the beat, as a percentage of the story.</summary>
    public double ExpectedPercent { get; set => SetProperty(ref field, value); }

    /// <summary>The chapter the beat happens in; null until placed.</summary>
    public Guid? ChapterId { get; set => SetProperty(ref field, value); }

    /// <summary>The writer's own notes on how the story hits this beat.</summary>
    public string Notes { get; set => SetProperty(ref field, value); } = "";
}

/// <summary>The project's beat sheet: a structure's beats, each placed in a chapter.</summary>
public sealed class BeatSheet(string template) {
    /// <summary>The template the sheet was started from, such as "Three-Act Structure".</summary>
    public string Template { get; } = template;

    public ObservableCollection<Beat> Beats { get; } = [];
}

/// <summary>A story structure to start a beat sheet from.</summary>
public sealed record BeatTemplate(string Name, string Description, IReadOnlyList<(string Name, double Percent, string Description)> Beats) {
    public static IReadOnlyList<BeatTemplate> All { get; } = [
        new("Three-Act Structure", "Setup, confrontation, resolution: the plainest map of a story.", [
            ("Hook", 0, "The opening image or question that makes the reader stay."),
            ("Inciting Incident", 12, "The event that disturbs the protagonist's ordinary life."),
            ("First Plot Point", 25, "The protagonist commits; there is no going back. End of Act One."),
            ("First Pinch Point", 37, "A reminder of what the antagonist can do."),
            ("Midpoint", 50, "A revelation or reversal: the protagonist stops reacting and starts acting."),
            ("Second Pinch Point", 62, "The antagonist's force is felt again, harder."),
            ("Second Plot Point", 75, "The last piece of information; the worst moment. End of Act Two."),
            ("Climax", 90, "The final confrontation, where the story's question is answered."),
            ("Resolution", 98, "The new normal, and the last image.")
        ]),
        new("Save the Cat", "Fifteen beats, from Opening Image to Final Image.", [
            ("Opening Image", 0, "A snapshot of the protagonist's world before the change."),
            ("Theme Stated", 5, "Someone says what the story is really about."),
            ("Set-Up", 6, "The protagonist's life, flaws and what is missing from it."),
            ("Catalyst", 10, "The event that sets the story going."),
            ("Debate", 14, "The protagonist hesitates: should I go?"),
            ("Break into Two", 20, "The protagonist chooses, and enters a new world."),
            ("B Story", 22, "A new character or relationship that carries the theme."),
            ("Fun and Games", 30, "The promise of the premise: what the reader came for."),
            ("Midpoint", 50, "A false victory or false defeat; the stakes rise."),
            ("Bad Guys Close In", 62, "Outside pressure and inner doubts tighten."),
            ("All Is Lost", 75, "The lowest point; something or someone is lost."),
            ("Dark Night of the Soul", 78, "The protagonist grieves, and finds the answer."),
            ("Break into Three", 80, "The solution, thanks to the A and B stories together."),
            ("Finale", 88, "The protagonist proves the change and resolves the story."),
            ("Final Image", 99, "The opposite of the opening image.")
        ]),
        new("Hero's Journey", "Twelve stages, from the Ordinary World and back again.", [
            ("Ordinary World", 0, "The hero at home, before the adventure."),
            ("Call to Adventure", 8, "A problem or challenge appears."),
            ("Refusal of the Call", 12, "The hero hesitates out of fear."),
            ("Meeting the Mentor", 17, "Advice, training or a gift for the road."),
            ("Crossing the Threshold", 25, "The hero leaves the ordinary world."),
            ("Tests, Allies, Enemies", 35, "The hero learns the rules of the new world."),
            ("Approach to the Inmost Cave", 45, "Preparations for the great danger."),
            ("The Ordeal", 50, "The hero faces death or the greatest fear."),
            ("Reward", 60, "The hero takes what was sought."),
            ("The Road Back", 75, "The consequences of the ordeal give chase."),
            ("Resurrection", 88, "One last test, in which the hero is changed for good."),
            ("Return with the Elixir", 97, "The hero comes home with something to share.")
        ]),
        new("Story Circle", "Dan Harmon's eight steps: a character leaves comfort and returns changed.", [
            ("You", 0, "A character in a zone of comfort."),
            ("Need", 12, "But they want something."),
            ("Go", 25, "They enter an unfamiliar situation."),
            ("Search", 37, "They adapt to it."),
            ("Find", 50, "They get what they wanted."),
            ("Take", 62, "And pay a heavy price for it."),
            ("Return", 75, "They go back to where they started."),
            ("Change", 90, "Having changed.")
        ]),
        new("Seven-Point Structure", "Dan Wells's seven points, planned from the ending backwards.", [
            ("Hook", 0, "The protagonist's starting state: the opposite of the resolution."),
            ("Plot Turn 1", 20, "The call to adventure: the world changes."),
            ("Pinch 1", 35, "Pressure that forces the protagonist to act."),
            ("Midpoint", 50, "The protagonist moves from reaction to action."),
            ("Pinch 2", 65, "Everything goes wrong; the protagonist is alone."),
            ("Plot Turn 2", 80, "The protagonist gets the last thing needed to win."),
            ("Resolution", 95, "What the story has been heading for.")
        ])
    ];

    public BeatSheet Create() {
        BeatSheet sheet = new(Name);
        foreach ((string name, double percent, string description) in Beats) {
            sheet.Beats.Add(new Beat { Name = name, ExpectedPercent = percent, Description = description });
        }
        return sheet;
    }
}