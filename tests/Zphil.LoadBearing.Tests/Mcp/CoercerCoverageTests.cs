using System.Reflection;
using System.Text.Json.Serialization;
using ModelContextProtocol.Server;
using Shouldly;
using Xunit;
using Zphil.LoadBearing.Cli.Mcp.Pipeline;

namespace Zphil.LoadBearing.Tests.Mcp;

/// <summary>
///     Ties the forgiving-input coercers to the surface they serve. <c>ToolInputSerializerOptions</c> is a
///     hand-kept per-CLR-type registration list, and a tool parameter whose type nothing claims does not
///     fail loudly: it falls through to the SDK default, where a mis-spelled argument comes back as a
///     byte-position deserializer error the CLI's error mapper reads as a bug rather than as user input —
///     the exact failure this pipeline exists to prevent. Both tests compute the live set from
///     <see cref="ToolAttributeDiscovery" /> rather than restating it, so the covered set tracks the tools
///     instead of drifting away from them silently.
/// </summary>
public sealed class CoercerCoverageTests
{
    /// <summary>
    ///     The factories registered <em>ahead</em> of the surface: shapes no <c>arch_*</c> parameter has yet,
    ///     kept deliberately rather than retired. Retiring them would also delete their schema-repair
    ///     branches, and the enum branch's loss is silent — the tool keeps working and the model simply
    ///     stops being told which values are allowed.
    /// </summary>
    private static readonly string[] DeclaredAheadOfTheSurface =
    [
        nameof(EnumArrayCoercerFactory),
        nameof(EnumValidationConverterFactory),
        nameof(StringArrayCoercerFactory)
    ];

    private static IList<JsonConverter> RegisteredConverters => ToolInputSerializerOptions.Instance.Converters;

    [Fact]
    public void EveryJsonBoundParameterType_HasARegisteredCoercer()
    {
        // The drift guard. A new parameter of an uncovered type reds here and forces the choice — write a
        // coercer, or say in the ledger below why the SDK default is good enough for it.
        List<string> uncovered = [];
        List<string> checkedParameters = [];

        foreach ((string where, Type type) in LiveJsonBoundParameters())
        {
            checkedParameters.Add(where);
            bool covered = RegisteredConverters.Any(converter => converter.CanConvert(type));
            if (!covered) uncovered.Add($"{where} ({type.Name})");
        }

        // Shouldly prints the offenders itself; the message says what to do about them.
        uncovered.ShouldBeEmpty(
            "each of these binds through the SDK default, where a mis-spelled argument comes back as a "
            + "byte-position error the CLI's error mapper reads as a bug — write a coercer for the type, "
            + "or record here why the default is good enough for it");
        // Otherwise a reflection slip that found no parameters at all would read as a pass.
        checkedParameters.ShouldNotBeEmpty("the live arch_* tools declare no JSON-bound parameters to check");
    }

    [Fact]
    public void RegisteredFactories_NoLiveParameterReaches_AreTheDeclaredAheadOfTheSurfaceSet()
    {
        // The other half of the tie: which registrations are speculative is a recorded decision, not an
        // accident. The first array- or enum-shaped parameter to land moves a name out of the ledger, and
        // an accidental de-registration moves one in.
        Type[] liveTypes = LiveJsonBoundParameters()
            .Select(parameter => parameter.Type)
            .Distinct()
            .ToArray();

        string[] unreached = RegisteredConverters
            .Where(converter => !liveTypes.Any(converter.CanConvert))
            .Select(converter => converter.GetType()
                .Name)
            .Order()
            .ToArray();

        unreached.ShouldBe(DeclaredAheadOfTheSurface);
    }

    /// <summary>
    ///     Every parameter the server binds from a <c>tools/call</c> argument object, as
    ///     <c>tool.parameter</c> plus the CLR type a converter would have to claim —
    ///     <see cref="Nullable{T}" /> unwrapped, because a converter registered for <c>T</c> serves
    ///     <c>T?</c> as well.
    /// </summary>
    private static IEnumerable<(string Where, Type Type)> LiveJsonBoundParameters()
    {
        foreach (MethodInfo method in ToolAttributeDiscovery.GetToolMethods())
        {
            // Attribute-gated, not just "public method on a tool type": without this, object's own
            // Equals(object) would count as a live parameter of type object.
            if (method.GetCustomAttribute<McpServerToolAttribute>()
                    ?.Name is not { } toolName) continue;

            IEnumerable<ParameterInfo> bound = method.GetParameters()
                .Where(ToolAttributeDiscovery.IsJsonBoundParameter);

            foreach (ParameterInfo parameter in bound)
            {
                Type type = Nullable.GetUnderlyingType(parameter.ParameterType) ?? parameter.ParameterType;
                yield return ($"{toolName}.{parameter.Name}", type);
            }
        }
    }
}
