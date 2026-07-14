namespace MyApp.Legacy.Billing;

/// <summary>
///     Name-carrier stub for the frozen scope's sanctioned interface. It exists only so
///     <c>typeof(IBillingFacade)</c> compiles in the violated spec; correspondence to the real MyApp
///     type is by full name (the established Stubs pattern). This DLL is not a MyApp solution member,
///     so the stub never enters the checked universe.
/// </summary>
public interface IBillingFacade;

/// <summary>Name-carrier stub for the facade implementation listed alongside the interface (GRAMMAR §7).</summary>
public sealed class BillingFacade : IBillingFacade;