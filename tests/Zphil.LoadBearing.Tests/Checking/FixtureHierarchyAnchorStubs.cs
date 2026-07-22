// Namespace-matched reflectable stub so typeof(MyApp.Web.WebRouteAttribute) yields FullName
// "MyApp.Web.WebRouteAttribute", which matches the real attribute in the extracted MyApp fixture model
// (the checker matches by full name, not CLR identity). The reflectable MyApp.Web.IHandler<T> anchor the
// negative-hierarchy fixture tests also use lives in Oracle/OracleStubs.cs and is reused. Do NOT reference
// the real MyApp assemblies for compilation.

namespace MyApp.Web;

/// <summary>Namespace-matched reflectable stub of the fixture's <c>[WebRoute]</c> attribute (checker anchor use only).</summary>
public sealed class WebRouteAttribute : Attribute;