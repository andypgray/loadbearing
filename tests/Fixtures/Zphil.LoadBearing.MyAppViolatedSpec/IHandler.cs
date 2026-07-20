namespace MyApp.Web;

/// <summary>
///     Name-carrier stub for the handler contract so <c>typeof(IHandler&lt;&gt;)</c> compiles in the
///     violated spec's DI rule. Correspondence to the real <c>MyApp.Web.IHandler`1</c> is by full name;
///     the open definition matches any construction (GRAMMAR §5.2).
/// </summary>
/// <typeparam name="T">The handled message type.</typeparam>
public interface IHandler<T>;
