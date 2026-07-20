namespace MyApp.Web;

/// <summary>
///     Name-carrier stub for the sanctioned handler constructor so <c>arch.Type&lt;HandlerRegistry&gt;()</c>
///     compiles in the violated spec's DI rule. Correspondence to the real <c>MyApp.Web.HandlerRegistry</c>
///     is by full name; this DLL is not a MyApp solution member, so the stub never enters the checked universe.
/// </summary>
public sealed class HandlerRegistry;
