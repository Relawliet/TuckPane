namespace TuckPane.Core;

internal enum GridGapPrimaryAxis
{
    Horizontal,
    Vertical
}

internal sealed class GridGapPath
{
    private readonly int _count;
    private readonly int _columns;
    private readonly int _source;
    private readonly List<int> _slots;

    internal GridGapPath(int count, int columns, int source)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(count);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(columns);
        ArgumentOutOfRangeException.ThrowIfNegative(source);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(source, count);
        _count = count;
        _columns = columns;
        _source = source;
        _slots = [source];
    }

    internal int Count => _count;
    internal int Columns => _columns;
    internal int GapIndex => _slots[^1];
    internal int Revision { get; private set; }

    internal bool MoveGapTo(int target, GridGapPrimaryAxis primaryAxis)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(target);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(target, _count);
        if (target == GapIndex) return false;

        int existing = _slots.IndexOf(target);
        if (existing >= 0)
        {
            _slots.RemoveRange(existing + 1, _slots.Count - existing - 1);
            Revision++;
            return true;
        }

        foreach (int slot in FindShortestRoute(GapIndex, target, primaryAxis).Skip(1))
        {
            existing = _slots.IndexOf(slot);
            if (existing >= 0) _slots.RemoveRange(existing + 1, _slots.Count - existing - 1);
            else _slots.Add(slot);
        }
        Revision++;
        return true;
    }

    internal bool Reset()
    {
        if (_slots.Count == 1) return false;
        _slots.RemoveRange(1, _slots.Count - 1);
        Revision++;
        return true;
    }

    internal int GetVisualSlot(int originalSlot)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(originalSlot);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(originalSlot, _count);
        if (originalSlot == _source) return GapIndex;
        int pathIndex = _slots.IndexOf(originalSlot);
        return pathIndex > 0 ? _slots[pathIndex - 1] : originalSlot;
    }

    internal int[] CreateVisualIndices()
    {
        var result = new int[_count];
        for (int original = 0; original < _count; original++) result[original] = GetVisualSlot(original);
        return result;
    }

    private int[] FindShortestRoute(int start, int target, GridGapPrimaryAxis primaryAxis)
    {
        var previous = new int[_count];
        Array.Fill(previous, -1);
        previous[start] = start;
        var pending = new Queue<int>();
        pending.Enqueue(start);

        while (pending.Count > 0 && previous[target] < 0)
        {
            int current = pending.Dequeue();
            foreach (int neighbor in OrderedNeighbors(current, target, primaryAxis))
            {
                if (previous[neighbor] >= 0) continue;
                previous[neighbor] = current;
                pending.Enqueue(neighbor);
            }
        }

        if (previous[target] < 0) throw new InvalidOperationException("Occupied grid slots are not connected.");
        var route = new List<int>();
        for (int slot = target;; slot = previous[slot])
        {
            route.Add(slot);
            if (slot == start) break;
        }
        route.Reverse();
        return route.ToArray();
    }

    private IEnumerable<int> OrderedNeighbors(int slot, int target, GridGapPrimaryAxis primaryAxis)
    {
        int row = slot / _columns;
        int column = slot % _columns;
        int targetRow = target / _columns;
        int targetColumn = target % _columns;
        var result = new List<int>(4);

        void Add(int candidateRow, int candidateColumn)
        {
            if (candidateRow < 0 || candidateColumn < 0 || candidateColumn >= _columns) return;
            int candidate = candidateRow * _columns + candidateColumn;
            if (candidate >= _count || result.Contains(candidate)) return;
            result.Add(candidate);
        }

        void AddHorizontalToward() => Add(row, column + Math.Sign(targetColumn - column));
        void AddVerticalToward() => Add(row + Math.Sign(targetRow - row), column);

        if (primaryAxis == GridGapPrimaryAxis.Horizontal)
        {
            if (column != targetColumn) AddHorizontalToward();
            if (row != targetRow) AddVerticalToward();
        }
        else
        {
            if (row != targetRow) AddVerticalToward();
            if (column != targetColumn) AddHorizontalToward();
        }
        Add(row, column - 1);
        Add(row, column + 1);
        Add(row - 1, column);
        Add(row + 1, column);
        return result;
    }
}
