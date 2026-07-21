using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using RoslynTypeKind = Microsoft.CodeAnalysis.TypeKind;

namespace Zphil.LoadBearing.Roslyn;

/// <summary>
///     Recognizes container-registration calls (GRAMMAR §4.7) inside one compilation's syntax and reports
///     each as a <see cref="RecognizedRegistration" /> (lifetime + service symbol + optional implementation
///     symbol). The recognition gate is <b>symbol-first</b> — never name-only: the invoked method must
///     resolve (its reduced form for an extension call) to a method whose containing namespace is
///     <c>Microsoft.Extensions.DependencyInjection</c> (or its <c>.Extensions</c> sub-namespace, the
///     <c>TryAdd*</c> family's home), whose name is in the recognized-call table below, and whose first
///     parameter is <c>IServiceCollection</c>. A look-alike extension in a user namespace is not recognized;
///     an in-solution wrapper whose body calls the real thing <em>is</em> seen, because the extractor walks
///     the wrapper's body like any other tree.
/// </summary>
/// <remarks>
///     <para>
///         The recognized-call table (each arm is a documented fence; everything else — <c>Configure</c>/
///         <c>AddOptions</c>, keyed-service overloads, raw <c>ServiceDescriptor</c>/<c>TryAddEnumerable</c>,
///         assembly-scanning registrars, framework defaults — is the §4.7 honesty boundary and yields
///         nothing):
///     </para>
///     <list type="bullet">
///         <item>
///             <c>Add{Singleton,Scoped,Transient}</c> and their <c>TryAdd*</c> twins — lifetime by name; two
///             type-args → (service, impl); one type-arg → (T, T) iff the call is receiver-only, else
///             (T, null) (a factory/instance registration names no implementation type); the <c>typeof</c>
///             overloads mirror the same split by <c>Type</c>-parameter count.
///         </item>
///         <item>
///             <c>AddHostedService&lt;T&gt;</c> — Singleton; service = the synthesized
///             <c>Microsoft.Extensions.Hosting.IHostedService</c> (implementation-only fallback, T in the
///             service slot, when it does not resolve); impl = T.
///         </item>
///         <item>
///             <c>AddDbContext&lt;T&gt;</c> / <c>AddDbContextPool&lt;T&gt;</c> — Scoped by default; an
///             explicit <em>literal</em> <c>ServiceLifetime.X</c> context-lifetime argument is honored; a
///             non-literal lifetime argument yields nothing (never guess); (service, impl) = (T, T).
///         </item>
///         <item>
///             <c>AddHttpClient&lt;TClient&gt;</c> / <c>AddHttpClient&lt;TClient, TImpl&gt;</c> — Transient;
///             (TClient, TClient) / (TClient, TImpl); the named-only string form registers no user type and
///             yields nothing.
///         </item>
///     </list>
///     <para>
///         Open-generic <c>typeof</c> registrations record definition-level like every other fact (§4.1) —
///         the extractor takes each reported symbol's <c>OriginalDefinition</c> FQN. The recognizer resolves
///         the three framework symbols it needs (<c>IServiceCollection</c>, <c>IHostedService</c>,
///         <c>ServiceLifetime</c>) once at construction; <see cref="IsActive" /> is false when
///         <c>IServiceCollection</c> is not referenced, so a compilation without MEDI recognizes nothing.
///     </para>
/// </remarks>
internal sealed class RegistrationRecognizer
{
    private const string DiNamespace = "Microsoft.Extensions.DependencyInjection";
    private const string DiExtensionsNamespace = "Microsoft.Extensions.DependencyInjection.Extensions";

    private readonly INamedTypeSymbol? _hostedService;
    private readonly INamedTypeSymbol? _serviceCollection;
    private readonly INamedTypeSymbol? _serviceLifetime;
    private readonly INamedTypeSymbol? _systemType;

    public RegistrationRecognizer(Compilation compilation)
    {
        _serviceCollection = compilation.GetTypeByMetadataName($"{DiNamespace}.IServiceCollection");
        _serviceLifetime = compilation.GetTypeByMetadataName($"{DiNamespace}.ServiceLifetime");
        _hostedService = compilation.GetTypeByMetadataName("Microsoft.Extensions.Hosting.IHostedService");
        _systemType = compilation.GetTypeByMetadataName("System.Type");
    }

    /// <summary>
    ///     False when <c>Microsoft.Extensions.DependencyInjection.IServiceCollection</c> is not referenced by
    ///     the compilation — the gate can never pass, so the extractor skips the registration walk entirely.
    /// </summary>
    public bool IsActive => _serviceCollection is not null;

    /// <summary>
    ///     Reports the registration(s) an invocation makes, or nothing when it is not a recognized call. A
    ///     single invocation reports at most one registration today; the enumerable shape leaves room for
    ///     future one-call-many-registrations forms without a signature change.
    /// </summary>
    public IEnumerable<RecognizedRegistration> Recognize(InvocationExpressionSyntax invocation, SemanticModel model)
    {
        SymbolInfo info = model.GetSymbolInfo(invocation);
        if ((info.Symbol ?? info.CandidateSymbols.FirstOrDefault()) is not IMethodSymbol method) return [];

        // The unreduced signature carries the IServiceCollection `this` parameter and the true namespace/name
        // (an extension call resolves to the reduced method, whose first parameter is the first value arg).
        IMethodSymbol signature = method.ReducedFrom ?? method;
        if (!PassesGate(signature)) return [];

        return signature.Name switch
        {
            "AddSingleton" or "AddScoped" or "AddTransient"
                or "TryAddSingleton" or "TryAddScoped" or "TryAddTransient"
                => RecognizeAddFamily(signature.Name, method, invocation, model),
            "AddHostedService" => RecognizeHostedService(method),
            "AddDbContext" or "AddDbContextPool" => RecognizeDbContext(method, invocation, model),
            "AddHttpClient" => RecognizeHttpClient(method),
            _ => []
        };
    }

    // Symbol-first gate: MEDI (or its .Extensions sub-namespace) + first parameter IServiceCollection. The
    // name is checked by the caller's switch, so a MEDI-namespace method with the wrong name (Configure,
    // AddKeyedSingleton, TryAddEnumerable) passes here yet falls through to the no-arm default — the §4.7
    // honesty boundary. A user-namespace look-alike fails the namespace check; a wrong first parameter (an
    // extension on some other type) fails the last check.
    private bool PassesGate(IMethodSymbol signature)
    {
        if (signature.Parameters.Length == 0) return false;

        string ns = signature.ContainingNamespace.ToDisplayString();
        if (ns is not (DiNamespace or DiExtensionsNamespace)) return false;

        return SymbolEqualityComparer.Default.Equals(signature.Parameters[0].Type.OriginalDefinition, _serviceCollection);
    }

    private IEnumerable<RecognizedRegistration> RecognizeAddFamily(
        string name, IMethodSymbol method, InvocationExpressionSyntax invocation, SemanticModel model)
    {
        Lifetime lifetime = LifetimeFromName(name);
        var typeArguments = method.TypeArguments;

        // Two type-args → (service, impl); e.g. AddSingleton<IFoo, Foo>().
        if (typeArguments.Length == 2)
        {
            if (AsNamed(typeArguments[0]) is { } service)
                return [new RecognizedRegistration(lifetime, service, AsNamed(typeArguments[1]))];
            return [];
        }

        // One type-arg → self iff receiver-only (AddSingleton<Foo>()), else impl null (factory/instance form).
        if (typeArguments.Length == 1)
        {
            if (AsNamed(typeArguments[0]) is not { } service) return [];
            return method.Parameters.Length == 0
                ? [new RecognizedRegistration(lifetime, service, service)]
                : [new RecognizedRegistration(lifetime, service, null)];
        }

        // No type-args → the typeof overloads, mirroring the split by Type-parameter count.
        return RecognizeTypeofAddFamily(lifetime, method, invocation, model);
    }

    private IEnumerable<RecognizedRegistration> RecognizeTypeofAddFamily(
        Lifetime lifetime, IMethodSymbol method, InvocationExpressionSyntax invocation, SemanticModel model)
    {
        var parameters = method.Parameters;
        var arguments = invocation.ArgumentList.Arguments;
        if (parameters.Length == 0 || !IsSystemType(parameters[0].Type) || arguments.Count == 0) return [];

        if (TypeFromTypeofArgument(arguments[0], model) is not { } service) return [];

        // Add(typeof(IFoo), typeof(Foo)) → (service, impl); the impl slot may be a non-typeof Type (skipped).
        if (parameters.Length >= 2 && IsSystemType(parameters[1].Type))
        {
            INamedTypeSymbol? impl = arguments.Count >= 2 ? TypeFromTypeofArgument(arguments[1], model) : null;
            return [new RecognizedRegistration(lifetime, service, impl)];
        }

        // Add(typeof(Foo)) → (Foo, Foo); Add(typeof(Foo), factory/instance) → (Foo, null).
        return parameters.Length == 1
            ? [new RecognizedRegistration(lifetime, service, service)]
            : [new RecognizedRegistration(lifetime, service, null)];
    }

    private IEnumerable<RecognizedRegistration> RecognizeHostedService(IMethodSymbol method)
    {
        if (method.TypeArguments.Length != 1 || AsNamed(method.TypeArguments[0]) is not { } impl) return [];

        // Service is the synthesized IHostedService (no syntactic mention); implementation-only fallback (T
        // in the service slot, impl null) when the framework type is not resolvable in this compilation.
        return _hostedService is not null
            ? [new RecognizedRegistration(Lifetime.Singleton, _hostedService, impl)]
            : [new RecognizedRegistration(Lifetime.Singleton, impl, null)];
    }

    private static IEnumerable<RecognizedRegistration> RecognizeHttpClient(IMethodSymbol method)
    {
        var typeArguments = method.TypeArguments;

        // AddHttpClient<TClient, TImpl>() → (TClient, TImpl); AddHttpClient<TClient>() → (TClient, TClient);
        // the named-only string form (no type-args) registers no user type → nothing.
        if (typeArguments.Length == 2)
            return AsNamed(typeArguments[0]) is { } client
                ? [new RecognizedRegistration(Lifetime.Transient, client, AsNamed(typeArguments[1]))]
                : [];

        if (typeArguments.Length == 1)
            return AsNamed(typeArguments[0]) is { } client
                ? [new RecognizedRegistration(Lifetime.Transient, client, client)]
                : [];

        return [];
    }

    private IEnumerable<RecognizedRegistration> RecognizeDbContext(
        IMethodSymbol method, InvocationExpressionSyntax invocation, SemanticModel model)
    {
        var typeArguments = method.TypeArguments;
        INamedTypeSymbol? service;
        INamedTypeSymbol? impl;
        switch (typeArguments.Length)
        {
            case 1:
                service = AsNamed(typeArguments[0]);
                impl = service;
                break;
            case 2:
                service = AsNamed(typeArguments[0]);
                impl = AsNamed(typeArguments[1]);
                break;
            default:
                return [];
        }

        if (service is null) return [];

        (bool present, bool literal, Lifetime value) = ContextLifetime(method, invocation, model);
        if (present && !literal) return []; // a non-literal lifetime argument → never guess
        Lifetime lifetime = present ? value : Lifetime.Scoped;
        return [new RecognizedRegistration(lifetime, service, impl)];
    }

    // The context lifetime of an AddDbContext/AddDbContextPool call: the argument bound to the first
    // ServiceLifetime parameter. Absent (default value used) → Scoped is applied by the caller; a literal
    // ServiceLifetime.X enum member → honored; anything else → present-but-not-literal → the caller skips.
    private (bool Present, bool Literal, Lifetime Value) ContextLifetime(
        IMethodSymbol method, InvocationExpressionSyntax invocation, SemanticModel model)
    {
        if (_serviceLifetime is null) return (false, false, default);

        var parameters = method.Parameters;
        int lifetimeParameter = -1;
        for (var i = 0; i < parameters.Length; i++)
            if (SymbolEqualityComparer.Default.Equals(parameters[i].Type.OriginalDefinition, _serviceLifetime))
            {
                lifetimeParameter = i;
                break;
            }

        if (lifetimeParameter < 0) return (false, false, default);

        ExpressionSyntax? argument = ArgumentForParameter(invocation.ArgumentList.Arguments, parameters, lifetimeParameter);
        if (argument is null) return (false, false, default); // default value → absent

        if (model.GetSymbolInfo(argument).Symbol is IFieldSymbol { ContainingType.TypeKind: RoslynTypeKind.Enum } field
            && SymbolEqualityComparer.Default.Equals(field.ContainingType.OriginalDefinition, _serviceLifetime))
            return (true, true, MapLifetime(field.Name));

        return (true, false, default);
    }

    // The argument expression bound to the reduced method's parameter at parameterIndex — matched by
    // name-colon first, else by positional index among the un-named arguments (C# requires positional
    // arguments precede named ones, so the k-th positional argument binds the k-th parameter).
    private static ExpressionSyntax? ArgumentForParameter(
        SeparatedSyntaxList<ArgumentSyntax> arguments, ImmutableArray<IParameterSymbol> parameters, int parameterIndex)
    {
        string parameterName = parameters[parameterIndex].Name;
        foreach (ArgumentSyntax argument in arguments)
            if (argument.NameColon?.Name.Identifier.Text == parameterName)
                return argument.Expression;

        var positional = 0;
        foreach (ArgumentSyntax argument in arguments)
        {
            if (argument.NameColon is not null) continue;
            if (positional == parameterIndex) return argument.Expression;
            positional++;
        }

        return null;
    }

    private static Lifetime LifetimeFromName(string name)
    {
        if (name.EndsWith("Singleton", StringComparison.Ordinal)) return Lifetime.Singleton;
        if (name.EndsWith("Scoped", StringComparison.Ordinal)) return Lifetime.Scoped;
        return Lifetime.Transient; // the only remaining recognized suffix
    }

    private static Lifetime MapLifetime(string enumMemberName)
    {
        return enumMemberName switch
        {
            "Singleton" => Lifetime.Singleton,
            "Transient" => Lifetime.Transient,
            _ => Lifetime.Scoped // "Scoped", and a defensive default for the closed ServiceLifetime enum
        };
    }

    private static INamedTypeSymbol? AsNamed(ITypeSymbol type)
    {
        return type as INamedTypeSymbol;
    }

    private bool IsSystemType(ITypeSymbol type)
    {
        return SymbolEqualityComparer.Default.Equals(type, _systemType);
    }

    private static INamedTypeSymbol? TypeFromTypeofArgument(ArgumentSyntax argument, SemanticModel model)
    {
        return argument.Expression is TypeOfExpressionSyntax typeOf
            ? model.GetSymbolInfo(typeOf.Type).Symbol as INamedTypeSymbol
            : null;
    }
}

/// <summary>
///     One recognized registration reported by <see cref="RegistrationRecognizer" />: the
///     <see cref="Lifetime" />, the service type symbol, and the implementation type symbol (null when the
///     registration names no distinct implementation — a factory/instance form, or the
///     <c>AddHostedService</c> unresolvable-service fallback where <see cref="Service" /> holds the
///     implementation). The extractor takes each symbol's <c>OriginalDefinition</c> FQN (definition-level,
///     §4.1) and records the fact string-side.
/// </summary>
internal readonly record struct RecognizedRegistration(
    Lifetime Lifetime,
    INamedTypeSymbol Service,
    INamedTypeSymbol? Implementation);