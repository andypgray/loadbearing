namespace Zphil.LoadBearing.Codebase;

/// <summary>
///     One instance of a model edge: the project whose compilation made the reference, and the project
///     whose copy of the target it reached (GRAMMAR §4.1).
/// </summary>
internal readonly struct EdgeInstance
{
    internal EdgeInstance(string sourceProject, string targetProject)
    {
        SourceProject = sourceProject;
        TargetProject = targetProject;
    }

    /// <summary>The declarer of the edge's source that compiled this instance.</summary>
    internal string SourceProject { get; }

    /// <summary>The project whose copy of the edge's target this instance reached.</summary>
    internal string TargetProject { get; }

    /// <summary>
    ///     Whether this instance stayed inside one project — the source compiled its own copy of the
    ///     target, so no project file declares a dependency for it.
    /// </summary>
    internal bool IsIntraProject => string.Equals(SourceProject, TargetProject, StringComparison.Ordinal);
}

/// <summary>
///     The instances one model edge stands for where a source file compiles into several projects
///     (GRAMMAR §4.1): one per declarer of the edge's source, reaching that declarer's own copy of the
///     target where it compiles one and the target's attributed declarer otherwise.
/// </summary>
/// <remarks>
///     <para>
///         The model carries one node per fully-qualified name and one edge per type pair, so a linked
///         <c>&lt;Compile Include&gt;</c> collapses N compilations of the same reference into one entry.
///         This is the enumeration that unfolds it again, and it is the single statement of the rule:
///         the checker reads it to decide which instances a rule owns and what the far end is at each,
///         and the survey reads it to render a project pair per instance that crosses a boundary. Two
///         readings of one sentence is what keeps a verdict and the survey that led an author to it from
///         disagreeing.
///     </para>
///     <para>
///         A struct enumerable over a struct enumerator, walked by index: this runs once per candidate
///         edge per rule wherever anything is conflated, and an <see cref="IEnumerable{T}" /> would
///         allocate a state machine there. Never call it for an ordinary edge — both callers keep a
///         fast path for the case where neither endpoint is multiply declared, where the one instance
///         it would yield is the edge itself.
///     </para>
/// </remarks>
internal readonly struct EdgeInstances
{
    private readonly TypeNode _source;
    private readonly TypeNode _target;

    private EdgeInstances(TypeNode source, TypeNode target)
    {
        _source = source;
        _target = target;
    }

    /// <summary>The instances of the edge running from <paramref name="source" /> to <paramref name="target" />.</summary>
    internal static EdgeInstances Of(TypeNode source, TypeNode target)
    {
        return new EdgeInstances(source, target);
    }

    /// <summary>The struct enumerator <c>foreach</c> binds to.</summary>
    /// <remarks>
    ///     Public, with <see cref="Enumerator.Current" /> and <see cref="Enumerator.MoveNext" />, because
    ///     the compiler's collection pattern will not bind to an internal member. Nothing escapes the
    ///     assembly: the declaring types are internal, so this is the accessibility the pattern costs.
    /// </remarks>
    public Enumerator GetEnumerator()
    {
        return new Enumerator(_source, _target);
    }

    /// <summary>
    ///     Walks the source's declarer roster — <see cref="TypeNode.ProjectName" /> first, then
    ///     <see cref="TypeNode.AlsoDeclaredBy" /> — minting the instance each declarer compiled.
    /// </summary>
    internal struct Enumerator
    {
        private readonly TypeNode _source;
        private readonly TypeNode _target;
        private int _index;

        internal Enumerator(TypeNode source, TypeNode target)
        {
            _source = source;
            _target = target;
            _index = -1;
            Current = default;
        }

        /// <summary>The instance the walk is standing on.</summary>
        public EdgeInstance Current { get; private set; }

        /// <summary>Advances to the next declarer, or reports the roster exhausted.</summary>
        public bool MoveNext()
        {
            _index++;
            if (_index > _source.AlsoDeclaredBy.Count) return false;

            string declarer = _index == 0 ? _source.ProjectName : _source.AlsoDeclaredBy[_index - 1];

            // A declarer that compiles the target too reached its own copy; every other one reached the
            // declaration the node's facts follow, which is the only copy it could have bound.
            string reached = _target.IsDeclaredBy(declarer) ? declarer : _target.ProjectName;
            Current = new EdgeInstance(declarer, reached);
            return true;
        }
    }
}
