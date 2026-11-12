namespace WritingIDE.Core.Text;

/// <summary>Maps character offsets to 1-based line/column positions.</summary>
public sealed class LineIndex {
    private readonly List<int> _lineStarts = [0];

    public LineIndex(string text) {
        for (int i = 0; i < text.Length; i++) {
            if (text[i] == '\n') {
                _lineStarts.Add(i + 1);
            }
            else if ((text[i] == '\r') && ((i + 1 >= text.Length) || (text[i + 1] != '\n'))) {
                _lineStarts.Add(i + 1);
            }
        }
    }

    public int LineCount => _lineStarts.Count;

    public (int Line, int Column) GetLocation(int offset) {
        int index = _lineStarts.BinarySearch(offset);
        if (index < 0) {
            index = ~index - 1;
        }
        return (index + 1, offset - _lineStarts[index] + 1);
    }

    public int GetLineStart(int line) {
        return _lineStarts[Math.Clamp(line - 1, 0, _lineStarts.Count - 1)];
    }
}