namespace Zphil.LoadBearing.PolyglotAppSpec;

/// <summary>
///     A spec for the PolyglotApp fixture whose one rule holds over the project this product can read, so
///     <c>check</c> exits 0 with a genuinely clean report. The rule is deliberately unremarkable — the
///     fixture's subject is the <em>solution</em>, not the law — and the assertion it enables is that a clean
///     verdict still names the <c>.fsproj</c> the model never contained.
/// </summary>
public sealed class PolyglotAppSpec : IArchitectureSpec
{
    /// <inheritdoc />
    public void Define(Arch arch)
    {
        // A shape rule rather than a reference rule, so the report is clean with nothing else in it. The
        // subject has to SELECT something or the rule fails on an empty subject; a reference rule would
        // also need a TARGET that selects, or it reports itself inert and the run carries a warning. A
        // naming rule over an interface the bed declares satisfies both without a second project.
        arch.Rule("naming/interfaces")
            .Enforce(arch.Types.OfKind(TypeKind.Interface).InNamespace("PolyglotApp.Core.*").MustHavePrefix("I"))
            .Because("House naming convention; agents grep by I-prefix.");
    }
}
