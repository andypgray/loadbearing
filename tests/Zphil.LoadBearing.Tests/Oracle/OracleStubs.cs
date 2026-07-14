// Namespace-matched stub so typeof(MyApp.Web.IHandler<>) yields FullName "MyApp.Web.IHandler`1",
// which matches the real MyApp.Web.IHandler<T> in the extracted MyApp model (the checker matches
// by full name, not by CLR identity). Do NOT reference the real MyApp assemblies for compilation.

namespace MyApp.Web;

/// <summary>Namespace-matched stub of the fixture's handler marker interface (oracle use only).</summary>
public interface IHandler<T>;