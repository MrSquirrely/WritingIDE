namespace WritingIDE.Core.Model;

public enum PlotThreadStatus {
    Open,
    Resolved,
    Abandoned
}

/// <summary>
/// A plot thread or piece of foreshadowing: a promise the story makes ("the compass points out to sea") whose setups,
/// developments and payoffs are marked in the manuscript.
/// </summary>
public sealed class PlotThread(string name) : ObservableObject {
    public Guid Id { get; init; } = Guid.NewGuid();

    public string Name { get; set => SetProperty(ref field, value); } = name;

    public string Description { get; set => SetProperty(ref field, value); } = "";

    public PlotThreadStatus Status { get; set => SetProperty(ref field, value); } = PlotThreadStatus.Open;

    public override string ToString() {
        return Name;
    }
}