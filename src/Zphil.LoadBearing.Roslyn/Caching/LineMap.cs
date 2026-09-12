namespace Zphil.LoadBearing.Roslyn.Caching;

/// <summary>
///     Where each 1-based line of one text of a document went in another text of it — what
///     <see cref="SourceShape.TryMapLines" /> answers, and everything a stored <see cref="FragmentSite" />
///     needs to move without being re-walked.
/// </summary>
/// <remarks>
///     The map is neither total nor injective: a line carrying no token start has no entry at all, and
///     lines joined onto one line share theirs. Value equality is what lets two independently computed
///     maps for one path be compared for agreement rather than for identity.
/// </remarks>
internal sealed class LineMap : IEquatable<LineMap>
{
    // Indexed by (old line - 1) and holding 1-based new lines, so 0 can mean "no entry" without a
    // parallel occupancy array.
    private readonly int[] _newLineByOldLine;

    /// <summary>
    ///     Wraps <paramref name="newLineByOldLine" />: one entry per 1-based old line, holding the 1-based
    ///     new line it maps to, or 0 where it has none. Taken by reference rather than copied, so the
    ///     caller must not keep or mutate the array it hands over.
    /// </summary>
    internal LineMap(int[] newLineByOldLine)
    {
        _newLineByOldLine = newLineByOldLine;
    }

    /// <summary>Whether every mapped line maps to itself — the shape a whitespace-only edit produces.</summary>
    internal bool IsIdentity => IsIdentityMapping(_newLineByOldLine);

    /// <summary>
    ///     Whether the map has an entry for <paramref name="oldLine" />, setting <paramref name="newLine" />
    ///     to the line it moved to. False where there is no entry — including any line outside the mapped
    ///     text.
    /// </summary>
    internal bool TryMap(int oldLine, out int newLine)
    {
        newLine = oldLine >= 1 && oldLine <= _newLineByOldLine.Length ? _newLineByOldLine[oldLine - 1] : 0;
        return newLine != 0;
    }

    /// <inheritdoc />
    public bool Equals(LineMap? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;

        return _newLineByOldLine.AsSpan()
            .SequenceEqual(other._newLineByOldLine);
    }

    /// <inheritdoc />
    public override bool Equals(object? obj)
    {
        return obj is LineMap other && Equals(other);
    }

    /// <inheritdoc />
    public override int GetHashCode()
    {
        var code = new HashCode();
        foreach (int newLine in _newLineByOldLine) code.Add(newLine);

        return code.ToHashCode();
    }

    private static bool IsIdentityMapping(int[] newLineByOldLine)
    {
        for (var index = 0; index < newLineByOldLine.Length; index++)
            if (newLineByOldLine[index] != 0 && newLineByOldLine[index] != index + 1)
                return false;

        return true;
    }
}
