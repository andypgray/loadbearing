namespace Zphil.LoadBearing.Codebase;

/// <summary>
///     A container-registration fact (GRAMMAR §4.7): a service type registered with a <see cref="Lifetime" />,
///     optionally naming a distinct implementation type, read from a source-visible registration call
///     (<c>AddSingleton</c> / <c>AddScoped</c> / <c>AddTransient</c> and their <c>TryAdd*</c> twins,
///     <c>AddHostedService</c>, <c>AddDbContext</c>, <c>AddHttpClient&lt;TClient&gt;</c>). <see cref="Sites" />
///     lists the distinct <c>file:line</c> positions of the recognized registration calls, deduped by
///     (file, line).
/// </summary>
/// <remarks>
///     <para>
///         The service and implementation are carried as <b>fully-qualified name strings</b>, deliberately
///         <em>not</em> as <see cref="TypeNode" /> instances and never denormalized onto the type model:
///         registration is many-to-many, so <c>arch.Registered(lifetime)</c> membership (the union of service
///         and implementation FQNs at that lifetime) is resolved at evaluation against these facts. The FQNs
///         are definition-level in the same form as <see cref="TypeNode.FullName" /> (open generics carry
///         their declared type-parameter names), so a registration FQN compares equal to a declared node's
///         <see cref="TypeNode.FullName" />.
///     </para>
///     <para>
///         <see cref="ImplementationFullName" /> is null for a registration that names no implementation type
///         — a factory (<c>AddSingleton&lt;T&gt;(sp =&gt; …)</c>) or instance form. For
///         <c>AddHostedService&lt;T&gt;</c> the service is the synthesized <c>IHostedService</c> and the
///         implementation is <c>T</c>; when <c>IHostedService</c> is not resolvable in the compilation the
///         fact degrades to implementation-only, carrying <c>T</c> in <see cref="ServiceFullName" /> with a
///         null <see cref="ImplementationFullName" />. Registrations the source does not spell with a
///         recognized call are invisible (the honesty boundary of §4.7): <c>Configure</c>/<c>AddOptions</c>,
///         keyed-service overloads, raw <c>ServiceDescriptor</c>/<c>TryAddEnumerable</c>, assembly scanning,
///         reflection, framework defaults, and wrapper extensions compiled into packages.
///     </para>
/// </remarks>
public sealed class ServiceRegistration
{
    internal ServiceRegistration(
        Lifetime lifetime, string serviceFullName, string? implementationFullName, IReadOnlyList<SourceLocation> sites)
    {
        Lifetime = lifetime;
        ServiceFullName = serviceFullName;
        ImplementationFullName = implementationFullName;
        Sites = sites;
    }

    /// <summary>The lifetime the registration was made with.</summary>
    public Lifetime Lifetime { get; }

    /// <summary>The service type's fully-qualified name (definition-level, the <see cref="TypeNode.FullName" /> form).</summary>
    public string ServiceFullName { get; }

    /// <summary>
    ///     The implementation type's fully-qualified name, or null when the registration names no distinct
    ///     implementation type (a factory or instance form, or the <c>AddHostedService</c> unresolvable-service
    ///     fallback).
    /// </summary>
    public string? ImplementationFullName { get; }

    /// <summary>The distinct registration-call sites, ordered by (file, line).</summary>
    public IReadOnlyList<SourceLocation> Sites { get; }
}