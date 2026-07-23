using System.Linq.Expressions;
using System.Runtime.CompilerServices;
using Zphil.LoadBearing.Internal;
using Zphil.LoadBearing.Model;
using Zphil.LoadBearing.Validation;

namespace Zphil.LoadBearing;

/// <summary>
///     The stage-machine entry point handed to <see cref="IArchitectureSpec.Define" /> (GRAMMAR
///     §3.2). Noun factories mint <see cref="Selection" />s stamped with this owner; <c>Rule</c> and
///     <c>Scope</c> register an anchor immediately and return a builder whose trailers mutate the
///     registered node. One <see cref="Arch" /> is shared across all specs in a single build, so
///     duplicate IDs across spec classes are caught in one pass (GRAMMAR §8 item 1).
/// </summary>
public sealed class Arch
{
    private readonly List<LayerNoun> _layers = [];
    private readonly List<Registration> _registrations = [];

    /// <summary>
    ///     Constructed by <see cref="ArchModelBuilder" /> (one fresh instance per build). Exposed to
    ///     tests via <c>InternalsVisibleTo</c> so a second <see cref="Arch" /> can be minted to
    ///     exercise the foreign-selection validation (§8 item 10).
    /// </summary>
    internal Arch()
    {
    }

    /// <summary>All types declared in the solution.</summary>
    public Selection Types => new RefinedSelection(this, TypesNoun.Instance, Array.Empty<SelectionAdjective>());

    /// <summary>The registered rule and scope anchors, in authoring order.</summary>
    internal IReadOnlyList<Registration> Registrations => _registrations;

    /// <summary>The declared layers, in authoring order.</summary>
    internal IReadOnlyList<LayerNoun> Layers => _layers;

    /// <summary>
    ///     Defines a named layer from one or more namespace globs. The <c>(name, glob, more)</c>
    ///     shape makes a zero-glob layer uncompilable (GRAMMAR §3.3).
    /// </summary>
    public Layer Layer(string name, string glob, params string[] more)
    {
        var globs = new List<string>(1 + more.Length) { glob };
        globs.AddRange(more);
        var noun = new LayerNoun(name, globs);
        _layers.Add(noun);
        return new Layer(this, noun);
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

    /// <summary>Registers a scope anchor immediately and returns its freeze-stage builder.</summary>
    public IScopeBuilder Scope(string id,
        [CallerFilePath] string? filePath = null, [CallerLineNumber] int lineNumber = 0)
    {
        var registration = new ScopeRegistration(id) { Location = SpecSourceLocation.Capture(filePath, lineNumber) };
        _registrations.Add(registration);
        return new ScopeBuilder(registration);
    }
}