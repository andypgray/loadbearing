// C# 7.3 (net48's default language version) — braced namespaces on purpose; see the csproj.

using System.Web;

namespace Legacy.Product
{
    /// <summary>
    ///     The envelope boundary, as a real type rather than a description: an ordinary net48 handler whose
    ///     implemented interface lives in <c>System.Web</c>, a Framework-only assembly with no .NET
    ///     counterpart. Loading it in the net10 host throws <see cref="System.TypeLoadException" />, so a
    ///     spec must reach types like this one through a namespace pattern rather than <c>typeof()</c>.
    /// </summary>
    public class LegacyHandler : IHttpHandler
    {
        public bool IsReusable => true;

        public void ProcessRequest(HttpContext context)
        {
        }
    }
}
