namespace UELT.Hikaro;

public record TranslationSnapshot(int RowIndex, string OldTranslation, string NewTranslation);

public class UndoRedoAction
{
    public string Description { get; init; } = "";
    public List<TranslationSnapshot> Changes { get; init; } = [];
}

public class UndoRedoManager
{
    private const int MaxHistory = 100;
    private readonly List<UndoRedoAction> _history = new();
    private int _index = -1;

    public bool CanUndo => _index >= 0;
    public bool CanRedo => _index < _history.Count - 1;

    public void Push(UndoRedoAction action)
    {
        if (action.Changes.Count == 0)
            return;

        if (_index < _history.Count - 1)
            _history.RemoveRange(_index + 1, _history.Count - _index - 1);

        _history.Add(action);
        _index++;

        if (_history.Count > MaxHistory)
        {
            _history.RemoveAt(0);
            _index--;
        }
    }

    public UndoRedoAction Undo()
    {
        if (!CanUndo) return null;
        return _history[_index--];
    }

    public UndoRedoAction Redo()
    {
        if (!CanRedo) return null;
        return _history[++_index];
    }

    public void Clear()
    {
        _history.Clear();
        _index = -1;
    }
}