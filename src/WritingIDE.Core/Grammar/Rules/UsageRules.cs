namespace WritingIDE.Core.Grammar.Rules;

/// <summary>NW1006: commonly confused words and phrases ("could of", "your welcome", "more then").</summary>
public sealed class ConfusedWordsRule() : PatternRule("NW1006", "Commonly confused words", RuleCategory.Usage, DiagnosticSeverity.Warning,
    "Catches homophones and phrases that are easy to mix up: could of/could have, then/than, loose/lose, its/it's and more.") {
    protected override IReadOnlyList<PatternEntry> Entries { get; } = [
        new(@"(could|should|would|must|might|may) of", "$1 have", "'{0}' should be '{1}'."),
        new(@"alot", "a lot", "'{0}' should be written as two words: '{1}'."),
        new(@"irregardless", "regardless", "'{0}' is non-standard; use '{1}'."),
        new(@"their (is|are|was|were|will be)", "there $1", "'their' is possessive; did you mean '{1}'?"),
        new(@"your (welcome|going to|gonna|not|so|too|really|always|never|tired|late|wrong)", "you're $1", "'your' is possessive; did you mean '{1}'?"),
        new(@"its (a|an|the|been|not|going|time to|never|always|only|just|about)", "it's $1", "'its' is possessive; did you mean '{1}'?"),
        new(@"it's own", "its own", "'it's' means 'it is'; use '{1}'."),
        new(@"(more|less|better|worse|rather|greater|larger|smaller|bigger|older|younger|faster|slower|taller|shorter|stronger|weaker|longer|higher|lower|other) then", "$1 than", "Use 'than' for comparisons: '{1}'."),
        new(@"(to|will|ll|might|could|would|can|cannot|not|never|don't|didn't|don\u2019t|didn\u2019t) loose", "$1 lose", "'loose' means not tight; did you mean '{1}'?"),
        new(@"suppose to", "supposed to", "'{0}' should be '{1}'."),
        new(@"for all intensive purposes", "for all intents and purposes", "'{0}' is an eggcorn; use '{1}'."),
        new(@"could care less", "couldn't care less", "The idiom is '{1}'."),
        new(@"based off of", "based on", "Prefer '{1}'."),
        new(@"in regards to", "in regard to", "Prefer '{1}'."),
        new(@"(peak|peek) (my|his|her|your|their|our) (interest|curiosity)", "pique $2 $3", "Interest is 'piqued': '{1}'."),
        new(@"sneak peak", "sneak peek", "A quick look is a 'peek': '{1}'."),
        new(@"whose (going|coming|there|that|this)", "who's $1", "'whose' is possessive; did you mean '{1}'?"),
        new(@"weather or not", "whether or not", "'{0}' should be '{1}'."),
        new(@"mute point", "moot point", "The idiom is '{1}'."),
        new(@"slight of hand", "sleight of hand", "The idiom is '{1}'."),
        new(@"bare with me", "bear with me", "The idiom is '{1}'."),
        new(@"one in the same", "one and the same", "The idiom is '{1}'."),
        new(@"in tact", "intact", "'{0}' is one word: '{1}'."),
        new(@"free reign", "free rein", "The idiom is '{1}' (from horse riding)."),
        new(@"tow the line", "toe the line", "The idiom is '{1}'."),
        new(@"baited breath", "bated breath", "The idiom is '{1}'."),
        new(@"(a|the) lightening", "$1 lightning", "Flashes in the sky are 'lightning'."),
        new(@"(lay|lays|laying) down (on|in) (the|a|his|her|my|their)", null, "Did you mean 'lie down'? People lie; objects are laid."),
        new(@"definately", "definitely", "'{0}' is misspelled: '{1}'."),
        new(@"(the|an|a) affect (of|on)", "$1 effect $2", "The noun is usually 'effect': '{1}'.")
    ];
}

/// <summary>NW3005: redundant phrases ("nodded his head", "each and every").</summary>
public sealed class RedundancyRule() : PatternRule("NW3005", "Redundant phrase", RuleCategory.Style, DiagnosticSeverity.Message,
    "Finds wordy or redundant phrases that can be tightened: 'nodded his head', 'shrugged her shoulders', 'in order to'.") {
    private const string Possessive = "his|her|their|my|your|our";

    protected override IReadOnlyList<PatternEntry> Entries { get; } = [
        new($@"(nodded|nods|nodding) ({Possessive}) head", "$1", "'{0}' is redundant: you can only nod your head. Use '{1}'."),
        new($@"(shrugged|shrugs|shrugging) ({Possessive}) shoulders", "$1", "'{0}' is redundant. Use '{1}'."),
        new($@"(blinked|blinks|blinking) ({Possessive}) eyes", "$1", "'{0}' is redundant. Use '{1}'."),
        new($@"(clapped|claps|clapping) ({Possessive}) hands", "$1", "'{0}' is redundant. Use '{1}'."),
        new(@"(thought|think|thinks|thinking) to (himself|herself|themselves|myself|yourself|ourselves)", "$1", "Thoughts are always to oneself. Use '{1}'."),
        new(@"(whispered|whispers) quietly", "$1", "Whispering is already quiet. Use '{1}'."),
        new(@"(shouted|shouts|yelled|yells) loudly", "$1", "'{0}' is redundant. Use '{1}'."),
        new(@"each and every", "every", "'{0}' is redundant. Use '{1}'."),
        new(@"free gift", "gift", "Gifts are free. Use '{1}'."),
        new(@"past history", "history", "'{0}' is redundant. Use '{1}'."),
        new(@"end result", "result", "'{0}' is redundant. Use '{1}'."),
        new(@"close proximity", "proximity", "'{0}' is redundant. Use '{1}'."),
        new(@"added bonus", "bonus", "'{0}' is redundant. Use '{1}'."),
        new(@"unexpected surprise", "surprise", "'{0}' is redundant. Use '{1}'."),
        new(@"(revert|return) back", "$1", "'{0}' is redundant. Use '{1}'."),
        new(@"repeat again", "repeat", "'{0}' is redundant. Use '{1}'."),
        new(@"in order to", "to", "'{0}' can usually be shortened to '{1}'."),
        new(@"due to the fact that", "because", "'{0}' is wordy. Use '{1}'."),
        new(@"at this point in time", "now", "'{0}' is wordy. Use '{1}'."),
        new(@"the reason why", "the reason", "'{0}' is redundant. Use '{1}'."),
        new(@"first and foremost", "first", "'{0}' is redundant. Use '{1}'."),
        new(@"very unique", "unique", "Unique cannot be graded. Use '{1}'."),
        new(@"(sat|sit|sits) down on", "$1 on", "Consider '{1}'."),
        new(@"(stood|stand|stands) up from", "$1 from", "Consider '{1}'.")
    ];
}

/// <summary>NW3004: clichés.</summary>
public sealed class ClicheRule() : PatternRule("NW3004", "Cliché", RuleCategory.Style, DiagnosticSeverity.Message,
    "Highlights tired expressions. Fresh imagery makes prose memorable.") {
    protected override IReadOnlyList<PatternEntry> Entries { get; } = new[] {
        "at the end of the day", "dead as a doornail", "in the nick of time", "avoid (it )?like the plague", "crystal clear",
        "all of a sudden", "time will tell", "only time will tell", "scared to death", "cold as ice", "light as a feather",
        "quiet as a mouse", "white as a ghost", "white as a sheet", "a blessing in disguise", "better late than never",
        "every cloud has a silver lining", "heart of gold", "the calm before the storm", "a dark and stormy night",
        "let the cat out of the bag", "the last straw", "fit as a fiddle", "in the blink of an eye", "sent shivers down (his|her|their|my) spine",
        "blood ran cold", "heart skipped a beat", "let out a breath (he|she|they|I) didn't know (he|she|they|I) (was|were) holding",
        "piece of cake", "think outside the box", "few and far between", "tip of the iceberg", "under the weather"
    }.Select(static phrase => new PatternEntry(phrase, null, "'{0}' is a cliché. Consider a fresher image.")).ToList();
}