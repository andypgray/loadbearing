// C# 7.3 (net48's default language version) — braced namespaces on purpose; see the csproj.

using Legacy.Product;

namespace Zphil.LoadBearing.LegacyBrokenSpec
{
    /// <summary>
    ///     A spec that cannot produce a model, so the failure arms of <c>ModelPipeline.LoadModel</c> can be
    ///     driven through the seam a user actually hits rather than pinned only against fabricated
    ///     exceptions. Its <c>Define()</c> anchors <see cref="LegacyHandler" />, whose implemented interface
    ///     lives in <c>System.Web</c> — a Framework-only assembly with no .NET counterpart — so the
    ///     <c>typeof()</c> throws <see cref="System.TypeLoadException" /> in the net10 host however this
    ///     project is built. That is the one failure no build setting can fix, which is why it is the
    ///     fixture rather than a missing package.
    /// </summary>
    /// <remarks>
    ///     The sibling <see cref="AuditedBillingGateway" /> carries the assembly's <em>other</em> failure
    ///     mode. Together they mean this single fixture drives two different catch arms depending on
    ///     whether the product DLL is staged beside it.
    /// </remarks>
    public sealed class LegacyBrokenSpec : IArchitectureSpec
    {
        /// <inheritdoc />
        public void Define(Arch arch)
        {
            arch.Rule("legacy/handlers-not-constructed")
                .Enforce(arch.Types.InNamespace("Legacy.*").MustNotConstruct(typeof(LegacyHandler)))
                .Because("The handler is resolved by the hosting pipeline, never built by a caller.");
        }
    }

    /// <summary>
    ///     An ordinary legacy subclass whose base type lives in the product assembly. Its only job is to put
    ///     a foreign type in this assembly's <em>signature</em> closure: <c>Assembly.GetTypes()</c> loads
    ///     every declared type's base and interfaces, so with the product DLL withheld this one fails to
    ///     load and discovery raises <see cref="System.Reflection.ReflectionTypeLoadException" /> before
    ///     <c>Define()</c> ever runs.
    /// </summary>
    /// <remarks>
    ///     The spec above keeps its own foreign reference inside a method body, where <c>GetTypes()</c> does
    ///     not look — that is what lets the same assembly fail two different ways.
    /// </remarks>
    public class AuditedBillingGateway : BillingGateway
    {
    }
}
