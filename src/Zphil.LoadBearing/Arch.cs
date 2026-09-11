using System.Linq.Expressions;
using System.Runtime.CompilerServices;
using Zphil.LoadBearing.Fluent;
using Zphil.LoadBearing.Internal;
using Zphil.LoadBearing.Model;
using Zphil.LoadBearing.Validation;

namespace Zphil.LoadBearing;

/// <summary>
///     The stage-machine entry point handed to <see cref="IArchitectureSpec.Define" /> (GRAMMAR §3.2).
/// </summary>
/// <remarks>
///     Noun factories mint <see cref="Selection" />s stamped with this owner; <c>Rule</c> and
///     <c>Scope</c> register an anchor immediately and return a builder whose trailers mutate the
///     registered node. One <see cref="Arch" /> is shared across all specs in a single build, so
///     duplicate IDs across spec classes are caught in one pass (GRAMMAR §8 item 1).
/// </remarks>
public sealed class Arch
{
    private readonly List<LayerRegistration> _layers = [];
    private readonly List<Registration> _registrations = [];

    internal Arch()
    {
    }

    /// <summary>All types declared in the solution.</summary>
    public Selection Types => new RefinedSelection(this, TypesNoun.Instance, Array.Empty<SelectionAdjective>());

    /// <summary>
    ///     All projects the solution declares, as build artifacts rather than as sets of types
    ///     (GRAMMAR §4.10) — the subject the packaging verbs range over. Refine it with
    ///     <c>.Named</c>/<c>.Matching</c>/<c>.Packable</c>; <see cref="Project" /> is the unrelated noun
    ///     naming the <em>types</em> a project declares, and both keep their meanings.
    /// </summary>
    /// <remarks>
    ///     A property rather than a method, so a <c>Projects(params string[])</c> naming form cannot
    ///     coexist with it (CS0102) — the same constraint that makes
    ///     <see cref="AnyOf(System.Type,System.Type[])" /> the
    ///     multi-type noun beside <see cref="Types" />. <c>arch.Projects.Named("A", "B")</c> is the naming
    ///     form here, and it reads as the adjective it is.
    /// </remarks>
    public ProjectSelection Projects => new RefinedProjectSelection(this, Array.Empty<ProjectAdjective>());

    /// <summary>The registered rule and scope anchors, in authoring order.</summary>
    internal IReadOnlyList<Registration> Registrations => _registrations;

    /// <summary>The declared layers with their purposes, in authoring order.</summary>
    internal IReadOnlyList<LayerRegistration> Layers => _layers;

    /// <summary>
    ///     Defines a named layer from one or more namespace globs. The <c>(name, glob, more)</c>
    ///     shape makes a zero-glob layer uncompilable (GRAMMAR §3.3).
    /// </summary>
    public Layer Layer(string name, string glob, params string[] more)
    {
        var globs = new List<string>(1 + more.Length) { glob };
        globs.AddRange(more);
        var noun = new LayerNoun(name, globs);
        _layers.Add(new LayerRegistration(noun));
        return new Layer(this, noun);
    }

    /// <summary>Appends a <c>Purpose</c> to the registration of the layer minted with <paramref name="noun" />.</summary>
    /// <remarks>
    ///     The lookup always succeeds: <see cref="Zphil.LoadBearing.Layer" />'s constructor is internal,
    ///     <see cref="Layer(string,string,string[])" /> is the only mint, and every
    ///     <see cref="Zphil.LoadBearing.Layer" /> built later over a layer subject reuses the same
    ///     <see cref="LayerNoun" /> instance — so there is no null branch and no defensive throw.
    /// </remarks>
    internal void AddLayerPurpose(LayerNoun noun, string prose)
    {
        LayerRegistration registration = _layers.First(candidate => ReferenceEquals(candidate.Noun, noun));
        registration.Purposes.Add(prose);
    }

    /// <summary>Types in a namespace glob (dot-segment aware, GRAMMAR §4.2).</summary>
    public Selection Namespace(string glob)
    {
        return new RefinedSelection(this, new NamespaceNoun(glob), Array.Empty<SelectionAdjective>());
    }

    /// <summary>Types in a named project.</summary>
    public Selection Project(string name)
    {
        return new RefinedSelection(this, new ProjectNoun(name), Array.Empty<SelectionAdjective>());
    }

    /// <summary>A single type.</summary>
    public Selection Type(Type type)
    {
        return new RefinedSelection(this, new TypeNoun(type), Array.Empty<SelectionAdjective>());
    }

    /// <summary>A single type — <c>arch.Type&lt;SqlConnection&gt;()</c> ≡ <c>arch.Type(typeof(T))</c>.</summary>
    public Selection Type<T>()
    {
        return Type(typeof(T));
    }

    /// <summary>
    ///     The union of the given selections — <c>arch.AnyOf(arch.Project("A"), arch.Project("B"))</c>
    ///     names every type either operand names (GRAMMAR §5.1). Operands may be any selection: a
    ///     <see cref="Layer" />, a <see cref="Registered()" /> noun, an already-refined selection, or another
    ///     union (nested unions flatten at mint). One operand is legal and is an identity. Adjectives apply
    ///     to the union, not through it — <c>AnyOf(a, b).Except(c)</c> is <c>(a ∪ b) − c</c>.
    /// </summary>
    public Selection AnyOf(Selection first, params Selection[] more)
    {
        Guard.NotNull(more, nameof(more));
        IReadOnlyList<Selection> parts = OperandList.OneOrMore(first, more, selection => selection);
        return UnionSelection.Create(this, parts);
    }

    /// <summary>
    ///     The union of the given types — <c>arch.AnyOf(typeof(A), typeof(B))</c> ≡
    ///     <c>arch.AnyOf(arch.Type(a), arch.Type(b))</c> (GRAMMAR §5.1). Pure authoring sugar on the
    ///     selection overload: identical model, identical prose. This is the multi-type noun; a
    ///     <c>Types(params Type[])</c> method cannot coexist with the <see cref="Types" /> property.
    /// </summary>
    public Selection AnyOf(Type first, params Type[] more)
    {
        Guard.NotNull(more, nameof(more));
        // A method group here would read as the bare word Type, which at this call site is far more
        // naturally the System.Type the operands already are. The lambda names the call.
        // ReSharper disable once ConvertClosureToMethodGroup
        IReadOnlyList<Selection> parts = OperandList.OneOrMore(first, more, type => Type(type));
        return UnionSelection.Create(this, parts);
    }

    /// <summary>
    ///     Types named in a source-visible container registration with the given lifetime — service and
    ///     implementation types alike (GRAMMAR §4.7). The natural operand of the <c>MustNotInject</c> verb.
    /// </summary>
    public Selection Registered(Lifetime lifetime)
    {
        return new RefinedSelection(this, new RegisteredNoun(lifetime), Array.Empty<SelectionAdjective>());
    }

    /// <summary>Types named in a source-visible container registration with any lifetime (GRAMMAR §4.7).</summary>
    public Selection Registered()
    {
        return new RefinedSelection(this, new RegisteredNoun(null), Array.Empty<SelectionAdjective>());
    }

    /// <summary>
    ///     A member-access ban target — a declaring type plus a member name (GRAMMAR §4.5). A
    ///     target-only leaf for
    ///     <see cref="SelectionConstraints.MustNotUse(Selection,Zphil.LoadBearing.Member,Zphil.LoadBearing.Member[])" />, not
    ///     a
    ///     <see cref="Selection" />; matching is by declaring type + member name, so one ban covers
    ///     every overload. Canonical call: <c>arch.Member(typeof(DateTime), nameof(DateTime.Now))</c>.
    /// </summary>
    public Member Member(Type type, string name,
        [CallerFilePath] string? filePath = null, [CallerLineNumber] int lineNumber = 0)
    {
        return new Member(this, type, name, SpecSourceLocation.Capture(filePath, lineNumber));
    }

    /// <summary>
    ///     A member-access ban target from a typed instance-member lambda whose member returns a value
    ///     (GRAMMAR §4.5): <c>arch.Member&lt;Task&lt;int&gt;&gt;(t =&gt; t.Result)</c> anchors
    ///     <c>Task&lt;T&gt;.Result</c>. Pure authoring sugar — desugars at mint to the same leaf as
    ///     <see cref="Member(System.Type,System.String,System.String,System.Int32)" />, with the type↔member pairing
    ///     additionally
    ///     compiler-checked; the anchor is the lambda's resolved member (a constructed generic normalized
    ///     to its definition), and an unresolvable lambda is reported at spec build (GRAMMAR §8).
    /// </summary>
    public Member Member<T>(Expression<Func<T, object?>> member,
        [CallerFilePath] string? filePath = null, [CallerLineNumber] int lineNumber = 0)
    {
        return MemberExpressionResolver.Resolve(this, Guard.NotNull(member, nameof(member)), SpecSourceLocation.Capture(filePath, lineNumber));
    }

    /// <summary>
    ///     A member-access ban target from a typed instance-member lambda whose member returns void
    ///     (GRAMMAR §4.5): <c>arch.Member&lt;Task&gt;(t =&gt; t.Wait())</c> anchors <c>Task.Wait</c>. The
    ///     void twin of the value-returning typed overload; same desugaring.
    /// </summary>
    public Member Member<T>(Expression<Action<T>> member,
        [CallerFilePath] string? filePath = null, [CallerLineNumber] int lineNumber = 0)
    {
        return MemberExpressionResolver.Resolve(this, Guard.NotNull(member, nameof(member)), SpecSourceLocation.Capture(filePath, lineNumber));
    }

    /// <summary>
    ///     A member-access ban target from a parameterless static-member lambda whose member returns a
    ///     value (GRAMMAR §4.5): <c>arch.Member(() =&gt; DateTime.Now)</c> anchors <c>DateTime.Now</c>.
    ///     The static form of the typed instance overloads; same desugaring.
    /// </summary>
    public Member Member(Expression<Func<object?>> member,
        [CallerFilePath] string? filePath = null, [CallerLineNumber] int lineNumber = 0)
    {
        return MemberExpressionResolver.Resolve(this, Guard.NotNull(member, nameof(member)), SpecSourceLocation.Capture(filePath, lineNumber));
    }

    /// <summary>
    ///     A member-access ban target from a parameterless static-member lambda whose member returns void
    ///     (GRAMMAR §4.5): <c>arch.Member(() =&gt; GC.Collect())</c> anchors <c>GC.Collect</c>. The static
    ///     void twin; same desugaring.
    /// </summary>
    public Member Member(Expression<Action> member,
        [CallerFilePath] string? filePath = null, [CallerLineNumber] int lineNumber = 0)
    {
        return MemberExpressionResolver.Resolve(this, Guard.NotNull(member, nameof(member)), SpecSourceLocation.Capture(filePath, lineNumber));
    }

    /// <summary>Registers a rule anchor immediately and returns its posture-stage builder.</summary>
    public IRuleBuilder Rule(string id,
        [CallerFilePath] string? filePath = null, [CallerLineNumber] int lineNumber = 0)
    {
        var registration = new RuleRegistration(id) { Location = SpecSourceLocation.Capture(filePath, lineNumber) };
        _registrations.Add(registration);
        return new RuleBuilder(registration);
    }

    /// <summary>Registers a scope anchor immediately and returns its posture-stage builder.</summary>
    public IScopeBuilder Scope(string id,
        [CallerFilePath] string? filePath = null, [CallerLineNumber] int lineNumber = 0)
    {
        var registration = new ScopeRegistration(id) { Location = SpecSourceLocation.Capture(filePath, lineNumber) };
        _registrations.Add(registration);
        return new ScopeBuilder(registration);
    }
}
