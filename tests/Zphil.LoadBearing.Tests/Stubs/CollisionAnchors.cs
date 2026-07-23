// Colliding simple names in distinct Billing/Sales namespaces, backing the collision-widening pins
// (GRAMMAR §6) for the negative hierarchy/attribute anchor lists and the exception verbs: each pair
// renders widened — `Billing.IReceipt` / `Sales.IReceipt`, `[Billing.Audit]` / `[Sales.Audit]`,
// `Billing.DataException` / `Sales.DataException`. Mirrors the Order pair in Orders.cs.

namespace Zphil.LoadBearing.Tests.Stubs.Billing
{
    /// <summary>A receipt contract in the Billing namespace (MustNotImplement collision anchor).</summary>
    public interface IReceipt;

    /// <summary>A ledger base type in the Billing namespace (MustNotDeriveFrom collision anchor).</summary>
    public abstract class LedgerBase;

    /// <summary>An audit attribute in the Billing namespace (MustNotBeAttributedWith collision anchor).</summary>
    public sealed class AuditAttribute : Attribute;

    /// <summary>A data exception in the Billing namespace (MustNotCatch / MustOnlyThrow collision anchor).</summary>
    public sealed class DataException : Exception;
}

namespace Zphil.LoadBearing.Tests.Stubs.Sales
{
    /// <summary>A receipt contract in the Sales namespace (MustNotImplement collision anchor).</summary>
    public interface IReceipt;

    /// <summary>A ledger base type in the Sales namespace (MustNotDeriveFrom collision anchor).</summary>
    public abstract class LedgerBase;

    /// <summary>An audit attribute in the Sales namespace (MustNotBeAttributedWith collision anchor).</summary>
    public sealed class AuditAttribute : Attribute;

    /// <summary>A data exception in the Sales namespace (MustNotCatch / MustOnlyThrow collision anchor).</summary>
    public sealed class DataException : Exception;
}