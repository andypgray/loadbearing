using System.Reflection;
using ArchUnitNET.Domain;
using ArchUnitNET.Domain.Dependencies;
using ArchUnitNET.Fluent;
using ArchUnitNET.Loader;
using Shouldly;
using Assembly = System.Reflection.Assembly;

namespace Zphil.LoadBearing.Tests.Oracle;

/// <summary>
///     The ArchUnitNET half of the differential-testing oracle: loads the three compiled MyApp
///     fixture DLLs once (their absolute paths baked into this assembly's metadata by the build) and
///     builds a single <see cref="ArchUnitNET.Domain.Architecture" /> from their IL (Mono.Cecil). This
///     is the independent substrate LoadBearing's Roslyn-source checker is cross-checked against
///     (Phase 10 Deliverable 1).
/// </summary>
/// <remarks>
///     Reused across the case-table rows as an <c>IClassFixture</c>, so the (non-trivial) assembly
///     load and model build happen exactly once. The reference universe is deliberately the three
///     MyApp assemblies' <em>declared</em> types (<see cref="Architecture" />'s
///     <c>Types</c>, which excludes the referenced BCL/System stubs) — the ArchUnitNET analog of
///     LoadBearing's <c>!IsExternal</c> universe (GRAMMAR §4.1), so <c>MustOnly*</c>-style rules share
///     the same reference universe on both substrates.
/// </remarks>
public sealed class OracleArchitecture
{
    private readonly HashSet<string> _declaredNames;

    public OracleArchitecture()
    {
        string domainPath = BakedPath("MyAppDomainPath");
        string webPath = BakedPath("MyAppWebPath");
        string billingPath = BakedPath("MyAppBillingPath");

        Domain = Assembly.LoadFrom(domainPath);
        Web = Assembly.LoadFrom(webPath);
        Billing = Assembly.LoadFrom(billingPath);

        Architecture = new ArchLoader().LoadAssemblies(Domain, Web, Billing).Build();

        // Architecture.Types is declared-only (the referenced/System stubs live in ReferencedTypes),
        // so with only the three MyApp assemblies loaded this is exactly the solution-declared universe.
        MyAppTypes = Architecture.Types.ToList();
        _declaredNames = MyAppTypes.Select(t => t.FullName).ToHashSet(StringComparer.Ordinal);
    }

    /// <summary>The architecture built from the three MyApp DLLs' IL.</summary>
    public Architecture Architecture { get; }

    /// <summary>The loaded MyApp.Domain assembly (for <c>ResideInAssembly</c> universe scoping).</summary>
    public Assembly Domain { get; }

    /// <summary>The loaded MyApp.Web assembly.</summary>
    public Assembly Web { get; }

    /// <summary>The loaded MyApp.Legacy.Billing assembly.</summary>
    public Assembly Billing { get; }

    /// <summary>The MyApp-declared types (the <c>!IsExternal</c> reference universe).</summary>
    public IReadOnlyList<IType> MyAppTypes { get; }

    /// <summary>
    ///     A fresh MyApp-declared-types universe filter (an <see cref="IObjectProvider{T}" /> ranging over
    ///     only the three MyApp assemblies' declared types). A new instance per call — the fluent object
    ///     carries rule-build state, so it is never shared between rules.
    /// </summary>
    public IObjectProvider<IType> DeclaredTypes()
    {
        return ArchRuleDefinition.Types().That().ResideInAssembly(Domain, Web, Billing);
    }

    /// <summary>
    ///     The frozen scope's interior: MyApp.Legacy.Billing types minus the two facade types
    ///     (<c>BillingFacade</c>, <c>IBillingFacade</c>) — the ArchUnitNET analog of LoadBearing's
    ///     <c>frozen.Except(facadeImpl).Except(facadeIface)</c>. Concretely
    ///     <c>
    ///         { BillingCalculator,
    ///         RoundingMode }
    ///     </c>
    ///     .
    /// </summary>
    public IType[] FrozenInterior()
    {
        return MyAppTypes
            .Where(t => t.FullName.StartsWith("MyApp.Legacy.Billing.", StringComparison.Ordinal))
            .Where(t => t.Name != "BillingFacade" && t.Name != "IBillingFacade")
            .ToArray();
    }

    /// <summary>The MyApp handler marker interface as resolved in the architecture (real assembly identity).</summary>
    public Interface HandlerInterface()
    {
        return Architecture.Interfaces.Single(i => i.Name.StartsWith("IHandler", StringComparison.Ordinal));
    }

    /// <summary>
    ///     The MyApp-declared types whose IL calls <c>System.DateTime</c>'s clock getters
    ///     (<c>get_Now()</c> / <c>get_UtcNow()</c>) — the ArchUnitNET (Mono.Cecil) analog of LoadBearing's
    ///     <c>MustNotUse(DateTime.Now, DateTime.UtcNow)</c> (GRAMMAR §4.5). A C# property read compiles to a
    ///     getter call, so the member-use ban surfaces as a <see cref="MethodCallDependency" /> in the IL
    ///     substrate. ArchUnitNET's fluent surface has no member-call predicate, so the architecture's
    ///     dependency model is queried directly — same substrate, verdict-level, type granularity.
    /// </summary>
    public IReadOnlySet<string> TypesReadingAmbientClock()
    {
        return MyAppTypes
            .Where(type => type.Dependencies.OfType<MethodCallDependency>().Any(IsClockGetterCall))
            .Select(type => type.FullName)
            .ToHashSet(StringComparer.Ordinal);
    }

    // A call to System.DateTime.get_Now() / get_UtcNow(). ArchUnitNET's MethodMember.Name is parens-inclusive;
    // pinned to the 0.13.3 form (the package is exact-pinned).
    private static bool IsClockGetterCall(MethodCallDependency dependency)
    {
        return dependency.TargetMember.DeclaringType.FullName == "System.DateTime"
               && dependency.TargetMember.Name is "get_Now()" or "get_UtcNow()";
    }

    /// <summary>
    ///     Evaluates an ArchUnitNET rule and returns the distinct FullNames of the MyApp-declared types
    ///     that FAIL it. Verdict-level, type granularity: the empty-subject null sentinel
    ///     (<c>EvaluatedObject == null</c>) is skipped via <c>OfType&lt;IType&gt;</c>, and the result is
    ///     restricted to the declared universe so referenced/external stubs never leak into the compare.
    /// </summary>
    public IReadOnlySet<string> FailingTypeNames(IArchRule rule)
    {
        return rule.Evaluate(Architecture)
            .Where(result => !result.Passed)
            .Select(result => result.EvaluatedObject)
            .OfType<IType>()
            .Select(type => type.FullName)
            .Where(_declaredNames.Contains)
            .ToHashSet(StringComparer.Ordinal);
    }

    private static string BakedPath(string key)
    {
        string? path = typeof(OracleArchitecture).Assembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .SingleOrDefault(attribute => attribute.Key == key)?.Value;

        path.ShouldNotBeNullOrEmpty();
        File.Exists(path).ShouldBeTrue($"MyApp fixture DLL for '{key}' should exist at the baked path '{path}'.");
        return path!;
    }
}