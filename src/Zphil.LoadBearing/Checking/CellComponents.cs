namespace Zphil.LoadBearing.Checking;

/// <summary>
///     The strongly connected components of a family's cell graph (GRAMMAR §5.1) — the one piece of graph
///     theory the cycle gate needs. Cells are the nodes and an arrow is "some owned reference crosses from
///     this cell into that one"; two cells share a component exactly when each can reach the other, which
///     is to say they lie on a common circle.
/// </summary>
internal static class CellComponents
{
    /// <summary>
    ///     One component id per cell, positionally aligned with the cell list: cells sharing an id lie on a
    ///     circle together, and a cell alone in its component lies on none. The ids themselves carry no
    ///     meaning beyond equality.
    /// </summary>
    /// <param name="cellCount">How many cells the family declares.</param>
    /// <param name="hasArrow">Whether some owned reference crosses from the first cell into the second.</param>
    /// <remarks>
    ///     Tarjan's algorithm, driven in cell index order so the ids a given graph yields never depend on
    ///     hash order or walk order — the same determinism every other checker answer holds to. The
    ///     recursion is bounded by the cell count, and a family's cells are an author-written list.
    /// </remarks>
    internal static int[] Of(int cellCount, Func<int, int, bool> hasArrow)
    {
        var state = new TarjanWalk(cellCount, hasArrow);
        for (var cell = 0; cell < cellCount; cell++)
            if (state.Index[cell] < 0)
                state.Visit(cell);

        return state.Component;
    }

    // Tarjan's state, held together so the recursive step reads as one walk rather than a parameter list.
    // Index is the discovery order (-1 until discovered); LowLink the earliest discovery index reachable
    // from the cell through its own subtree plus one back-arrow; OnStack keeps the walk from following a
    // cross-arrow into a component already closed.
    private sealed class TarjanWalk
    {
        private readonly Func<int, int, bool> _hasArrow;
        private readonly int _cellCount;
        private readonly int[] _lowLink;
        private readonly bool[] _onStack;
        private readonly Stack<int> _stack = new();
        private int _nextIndex;
        private int _nextComponent;

        internal TarjanWalk(int cellCount, Func<int, int, bool> hasArrow)
        {
            _cellCount = cellCount;
            _hasArrow = hasArrow;
            _lowLink = new int[cellCount];
            Index = new int[cellCount];
            Component = new int[cellCount];
            for (var cell = 0; cell < cellCount; cell++)
                Index[cell] = -1;

            _onStack = new bool[cellCount];
        }

        internal int[] Index { get; }

        internal int[] Component { get; }

        internal void Visit(int cell)
        {
            Index[cell] = _nextIndex;
            _lowLink[cell] = _nextIndex;
            _nextIndex++;
            _stack.Push(cell);
            _onStack[cell] = true;

            for (var other = 0; other < _cellCount; other++)
            {
                if (other == cell || !_hasArrow(cell, other)) continue;

                if (Index[other] < 0)
                {
                    Visit(other);
                    _lowLink[cell] = Math.Min(_lowLink[cell], _lowLink[other]);
                }
                else if (_onStack[other])
                {
                    _lowLink[cell] = Math.Min(_lowLink[cell], Index[other]);
                }
            }

            if (_lowLink[cell] != Index[cell]) return;

            // A root of its component: everything above it on the stack, itself included, is the component.
            int member;
            do
            {
                member = _stack.Pop();
                _onStack[member] = false;
                Component[member] = _nextComponent;
            } while (member != cell);

            _nextComponent++;
        }
    }
}
