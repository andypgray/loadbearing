namespace Zphil.LoadBearing.Model;

/// <summary>
///     <c>.Fields.ThatAreStatic()</c> premodifies the member head: "static fields of types" (GRAMMAR
///     §5.7, §6) — the first member <em>shape</em> adjective, beside the attribute one.
/// </summary>
/// <remarks>
///     A head prefix for the same reference-position reason
///     <see cref="MemberAttributedWithAdjective" /> gives: a member subject renders its type selection in
///     reference position, so an inline "that are static" would land after that reference and read as a
///     claim about the types. Prefixing keeps the fact attached to the noun it narrows.
///     Member head prefixes <b>concatenate</b> in authoring order (they are an intersection, so the
///     sentence must say both), which this adjective makes observable for the first time: it is the first
///     prefix that can stand beside one from another family.
/// </remarks>
internal sealed class MemberThatAreStaticAdjective : MemberAdjective
{
    internal override AdjectivePlacement Placement => AdjectivePlacement.HeadPrefix;

    internal override string Fragment => "static ";
}
