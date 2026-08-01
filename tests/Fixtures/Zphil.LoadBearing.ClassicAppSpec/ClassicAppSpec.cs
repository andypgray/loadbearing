// C# 7.3 (net48's default language version) — braced namespaces on purpose; see the csproj.

namespace Zphil.LoadBearing.ClassicAppSpec
{
    /// <summary>
    ///     The spec `check` runs against the non-SDK-style ClassicApp fixture solution. Every anchor is a
    ///     pattern, which is the remedy a Framework codebase actually needs: a <c>typeof()</c> anchor on
    ///     <c>System.Data.SqlClient.SqlConnection</c> would need an assembly that does not exist on .NET,
    ///     while <c>arch.Namespace("System.Data.*")</c> needs no assembly load at all.
    /// </summary>
    /// <remarks>
    ///     A name pattern is not a substitute either: a <c>Selection</c> only matches types inside the
    ///     checked codebase, so <c>arch.Types.WithNameMatching("SqlConnection")</c> would go inert. The
    ///     namespace target is the one that reaches an external type.
    /// </remarks>
    public sealed class ClassicAppSpec : IArchitectureSpec
    {
        /// <inheritdoc />
        public void Define(Arch arch)
        {
            arch.Rule("naming/interfaces")
                .Enforce(arch.Types.OfKind(TypeKind.Interface).InNamespace("Classic.*").MustHavePrefix("I"))
                .Because("An I-prefixed interface is what the rest of this codebase reads as a contract.")
                .Fix("Rename the interface with an I prefix.");

            arch.Rule("data-access/no-inline-sql")
                .Enforce(arch.Namespace("Classic.*").MustNotReference(arch.Namespace("System.Data.*")))
                .Because("Inline ADO.NET in the billing code is how the connection settings and the " +
                         "retry policy drifted apart in the first place.")
                .Fix("Go through the repository layer; keep System.Data behind it.");

            arch.Rule("billing/no-direct-calculator")
                .Enforce(arch.Types.InNamespace("Classic.*")
                    .Except(arch.Types.WithNameMatching("BillingGateway"))
                    .MustNotReference(arch.Types.WithNameMatching("BillingCalculator")))
                .Because("The calculator's rounding is only correct when it is driven through the gateway, " +
                         "which supplies the invoice-level context.")
                .Fix("Call the gateway; let it own the calculator.");
        }
    }
}
