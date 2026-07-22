namespace MyApp.Domain;

/// <summary>
///     Name-carrier stub for the domain exception type so <c>arch.Type&lt;OrderRuleViolation&gt;()</c>
///     compiles in the violated spec's <c>MustOnlyThrow</c> rule. Correspondence to the real
///     <c>MyApp.Domain.OrderRuleViolation</c> is by full name (the established Stubs pattern); this DLL is
///     not a MyApp solution member, so the stub never enters the checked universe.
/// </summary>
public sealed class OrderRuleViolation;