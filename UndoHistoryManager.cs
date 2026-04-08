using System;
using System.Collections.Generic;

namespace FlowInk;

internal sealed class UndoHistoryManager<TAction, TContext>
    where TAction : class
{
    private readonly Stack<TAction> _undoStack = new();
    private readonly Stack<TAction> _redoStack = new();
    private readonly int _maxHistory;
    private readonly Action<TAction, TContext> _undoExecutor;
    private readonly Action<TAction, TContext> _redoExecutor;

    public UndoHistoryManager(
        int maxHistory,
        Action<TAction, TContext> undoExecutor,
        Action<TAction, TContext> redoExecutor)
    {
        if (maxHistory <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxHistory));
        }

        _maxHistory = maxHistory;
        _undoExecutor = undoExecutor ?? throw new ArgumentNullException(nameof(undoExecutor));
        _redoExecutor = redoExecutor ?? throw new ArgumentNullException(nameof(redoExecutor));
    }

    public int MaxHistory => _maxHistory;
    public int UndoCount => _undoStack.Count;
    public int RedoCount => _redoStack.Count;
    public bool CanUndo => _undoStack.Count > 0;
    public bool CanRedo => _redoStack.Count > 0;

    public void Push(TAction action)
    {
        if (action == null)
        {
            return;
        }

        _undoStack.Push(action);
        _redoStack.Clear();

        TrimUndoIfNeeded();
    }

    public TAction? Undo(TContext context)
    {
        if (_undoStack.Count == 0)
        {
            return null;
        }

        TAction action = _undoStack.Pop();
        _undoExecutor(action, context);
        _redoStack.Push(action);
        return action;
    }

    public TAction? Redo(TContext context)
    {
        if (_redoStack.Count == 0)
        {
            return null;
        }

        TAction action = _redoStack.Pop();
        _redoExecutor(action, context);
        _undoStack.Push(action);
        return action;
    }

    public void Clear()
    {
        _undoStack.Clear();
        _redoStack.Clear();
    }

    private void TrimUndoIfNeeded()
    {
        if (_undoStack.Count <= _maxHistory)
        {
            return;
        }

        TAction[] items = _undoStack.ToArray(); // newest -> oldest
        Array.Reverse(items);                   // oldest -> newest

        int keepStart = items.Length - _maxHistory;

        _undoStack.Clear();

        for (int i = keepStart; i < items.Length; i++)
        {
            _undoStack.Push(items[i]);
        }
    }
}
