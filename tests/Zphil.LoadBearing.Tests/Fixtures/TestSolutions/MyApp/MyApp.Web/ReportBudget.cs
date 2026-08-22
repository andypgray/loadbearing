namespace MyApp.Web;

// The static-mutability carrier for state/no-static-mutable. Three static fields, one per shape the
// verb distinguishes: RenderCount is static and writable — the red site; DefaultFormat is static
// readonly and MaxRows is const, and BOTH are green, because a const field satisfies "must be
// readonly". The rule's subject is .Fields.ThatAreStatic(), so ReportPublisher's equally writable
// INSTANCE _attempts stays outside it — which is what makes the single red a statement about
// staticness rather than about writability.
// Nothing else trips a rule: not a *Controller, *Service or *Scheduler name, no System.Data, no
// clock read, no Task method, no catch, no throw, no `new`, no Domain reference, no IHandler, and no
// property, so the get-only rule beside it does not reach this type.
public static class ReportBudget
{
    public const int MaxRows = 500;

    public static readonly string DefaultFormat = "csv";

    public static int RenderCount;
}
