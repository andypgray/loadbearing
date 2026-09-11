namespace Zphil.LoadBearing.Codebase;

/// <summary>
///     One dependency-injection registration the source spells out: a service type registered with a
///     <see cref="Lifetime" />, and where the call names one, the implementation type behind it. Read them
///     from <see cref="CodebaseModel.ServiceRegistrations" />. The calls recognized are
///     <c>AddSingleton</c>, <c>AddScoped</c> and <c>AddTransient</c> with their <c>TryAdd</c> forms,
///     <c>AddHostedService</c>, <c>AddDbContext</c>, <c>AddDbContextPool</c> and <c>AddHttpClient</c>.
/// </summary>
/// <remarks>
///     <para>
///         A call is recognized by the method it resolves to and never by its name alone: it must come from
///         <c>Microsoft.Extensions.DependencyInjection</c> and take an <c>IServiceCollection</c> first. So a
///         look-alike of your own is not counted, while a helper of your own whose body calls the real thing
///         is. Everything registered another way is simply absent — assembly scanning, keyed overloads, a
///         raw <c>ServiceDescriptor</c>, <c>Configure</c> and <c>AddOptions</c>, reflection, framework
///         defaults, and registration helpers compiled into a package, which leave no source to read.
///     </para>
///     <para>
///         The two type names are fully qualified in the same form <see cref="TypeNode.FullName" /> takes,
///         so either compares equal to a declared type's name. An open generic is recorded as its
///         definition, so <c>AddSingleton(typeof(IRepo&lt;&gt;), typeof(Repo&lt;&gt;))</c> records
///         <c>IRepo&lt;T&gt;</c> and <c>Repo&lt;T&gt;</c>.
///     </para>
/// </remarks>
// Service and implementation are carried as fully-qualified name strings, never as TypeNode instances and
// never denormalized onto the type model: registration is many-to-many, so arch.Registered membership is
// resolved at evaluation against these facts (GRAMMAR §4.7).
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

    /// <summary>
    ///     Gets the lifetime the registration was made with. <c>AddSingleton</c>, <c>AddScoped</c> and
    ///     <c>AddTransient</c> each name their own; <c>AddHostedService</c> registers as a singleton,
    ///     <c>AddHttpClient</c> as transient, and <c>AddDbContext</c> as scoped unless the call passes a
    ///     <c>ServiceLifetime</c> of its own. Such an argument is honoured only when it is a literal: where it
    ///     is computed, the call records no fact at all rather than a guess.
    /// </summary>
    public Lifetime Lifetime { get; }

    /// <summary>
    ///     Gets the service type's fully-qualified name. For <c>AddHostedService&lt;T&gt;</c> the service is
    ///     <c>Microsoft.Extensions.Hosting.IHostedService</c> even though nothing in the source spells it; where
    ///     the compilation cannot resolve that interface, <c>T</c> is recorded here instead and
    ///     <see cref="ImplementationFullName" /> is null.
    /// </summary>
    public string ServiceFullName { get; }

    /// <summary>
    ///     Gets the implementation type's fully-qualified name, or null where the call names no distinct
    ///     implementation — a factory such as <c>AddSingleton&lt;T&gt;(sp =&gt; ...)</c>, an instance
    ///     registration, or the <c>AddHostedService</c> case above.
    /// </summary>
    public string? ImplementationFullName { get; }

    /// <summary>
    ///     Gets each distinct registration call, ordered by file then line. Two calls recording the same fact on
    ///     one line count as one site.
    /// </summary>
    public IReadOnlyList<SourceLocation> Sites { get; }
}
