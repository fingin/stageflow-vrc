namespace StageFlow.Core.Projects;

/// <summary>Immutable project snapshots. Commands name edits for undo/redo UI.</summary>
public sealed class EditHistory<T>(T initial, int capacity = 100) where T : notnull
{
    private readonly List<(string Name, T Value)> _undo = [];
    private readonly Stack<(string Name, T Value)> _redo = [];
    public T Current { get; private set; } = initial;
    public string? UndoName => _undo.Count > 0 ? _undo[^1].Name : null;
    public string? RedoName => _redo.TryPeek(out var item) ? item.Name : null;
    public void Apply(string name, T value)
    {
        if (EqualityComparer<T>.Default.Equals(Current, value)) return;
        _undo.Add((name, Current));
        if (_undo.Count > capacity) _undo.RemoveAt(0);
        Current = value; _redo.Clear();
    }
    public bool Undo()
    {
        if (_undo.Count == 0) return false;
        var item = _undo[^1]; _undo.RemoveAt(_undo.Count - 1);
        _redo.Push((item.Name, Current)); Current = item.Value; return true;
    }
    public bool Redo()
    {
        if (!_redo.TryPop(out var item)) return false;
        _undo.Add((item.Name, Current)); Current = item.Value; return true;
    }
}
