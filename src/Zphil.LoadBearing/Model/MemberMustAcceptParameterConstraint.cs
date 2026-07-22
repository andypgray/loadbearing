using Zphil.LoadBearing.Prose;

namespace Zphil.LoadBearing.Model;

/// <summary>
///     <c>.Methods.MustAcceptParameter(typeof(CancellationToken))</c> → "must accept a parameter of type
///     `CancellationToken`" (GRAMMAR §5.7, §4.6). The fragment is deliberately "a parameter of type `X`"
///     — article-safe for any type name — not "a/an `X` parameter". An open-generic anchor renders
///     declared type-parameter names (<c>typeof(IProgress&lt;&gt;)</c> → `IProgress&lt;T&gt;`); a
///     closed-generic anchor is refused at spec build (GRAMMAR §8 item 20).
/// </summary>
internal sealed class MemberMustAcceptParameterConstraint(MemberSelection subject, Type parameterType) : MemberConstraint(subject)
{
    /// <summary>The parameter-type anchor, matched definition-level (GRAMMAR §4.6).</summary>
    internal Type ParameterType { get; } = parameterType;

    internal override string VerbPhrase => "must accept a parameter of type " + ProseFormat.Backtick(TypeName.Simple(ParameterType));
}