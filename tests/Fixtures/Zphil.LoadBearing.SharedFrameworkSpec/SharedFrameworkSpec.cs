using Microsoft.AspNetCore.Mvc;

namespace Zphil.LoadBearing.SharedFrameworkSpec;

/// <summary>
///     A spec that cannot load, for one specific reason: its <c>typeof()</c> anchor names a type from a
///     .NET shared framework, which the host running the spec does not carry and which no build setting
///     can stage beside it.
/// </summary>
/// <remarks>
///     <para>
///         The anchor is confined to the <c>Define()</c> body on purpose. A spec class that itself derived
///         from an ASP.NET type would fail earlier, in <c>SpecDiscovery.FindSpecs</c>, and surface as the
///         <c>ReflectionTypeLoadException</c> arm — a different message, and the wrong one to guard here.
///     </para>
///     <para>
///         The rule is otherwise ordinary and would pass; nothing about it is reached, because
///         <c>Define()</c> throws on the first anchor.
///     </para>
/// </remarks>
public sealed class SharedFrameworkSpec : IArchitectureSpec
{
    /// <inheritdoc />
    public void Define(Arch arch)
    {
        arch.Rule("shared-framework/controllers-suffixed")
            .Enforce(arch.Types.DerivedFrom(typeof(ControllerBase)).MustHaveSuffix("Controller"))
            .Because("MVC discovery is convention-based, so a controller that is not named for one is invisible.")
            .Fix("Rename the type to end in Controller.");
    }
}
