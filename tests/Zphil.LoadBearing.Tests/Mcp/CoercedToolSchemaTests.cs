using System.ComponentModel;
using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.AI;
using ModelContextProtocol.Server;
using Shouldly;
using Xunit;
using Zphil.LoadBearing.Cli.Mcp.Pipeline;

namespace Zphil.LoadBearing.Tests.Mcp;

/// <summary>
///     The schema repair <c>CoercingToolRegistration.ReinjectErasedSchema</c> performs. Registering a
///     custom <see cref="System.Text.Json.Serialization.JsonConverter" /> for a type makes the STJ schema
///     exporter erase every parameter of that type — it can no longer infer the shape — so the
///     forgiving-input coercers buy their error messages at the cost of the advertised schema. The repair
///     reads the shape back off the CLR type; without it a client would be told a parameter has no type at
///     all, and an enum parameter would lose its allowed-values list entirely.
/// </summary>
/// <remarks>
///     <para>
///         Erasure has two spellings and both are pinned here: <c>{}</c> when the parameter has another
///         keyword to carry (a <c>[Description]</c>, a default value) and the bare <c>true</c> schema when
///         it has neither. Only <c>false</c>, the "no value is valid" schema, is left alone.
///     </para>
///     <para>
///         Driven through a probe tool created with the production
///         <see cref="CoercingToolRegistration.SchemaOptions" />, because the array and enum repairs serve
///         parameter shapes no <c>arch_*</c> tool has yet: every real parameter today is <c>string</c>,
///         <c>string?</c> or <c>bool</c>, and every one of them is described, so only the described
///         scalar-string branch is reachable through a real <c>tools/list</c>.
///     </para>
/// </remarks>
public sealed class CoercedToolSchemaTests
{
    // Both erasures, one claim: whatever keyword the exporter had left to erase, a string parameter reaches
    // the client as a plain "string".
    [Theory]
    [InlineData(nameof(Probe.WithScalarString), "required")]
    // The advertised shape is "string", not ["string","null"] — pinned because a union here would
    // change what every optional arch_* parameter advertises, and all of them are string?.
    [InlineData(nameof(Probe.WithNullableString), "optional")]
    // The `true` erasure, and the branch every live parameter goes through: a parameter with no description
    // and no default has neither keyword the exporter needs to emit an object at all.
    [InlineData(nameof(Probe.WithUndescribedString), "plain")]
    public void ErasedScalarString_IsRestoredToAPlainString(string methodName, string parameterName)
    {
        JsonObject property = ShouldHaveSchemaPropertyFor(methodName, parameterName);

        property["type"]!.GetValue<string>().ShouldBe("string");
    }

    [Theory]
    [InlineData(nameof(Probe.WithStringArray), "names")]
    // The exporter's other erasure. A parameter carrying a description or a default lands as `{}`; one with
    // neither lands as the bare `true` schema — "any value" — which is the same loss in a shape the
    // object-guarded repair used to fall straight through, silently and in the direction of "anything goes".
    [InlineData(nameof(Probe.WithUndescribedStringArray), "names")]
    public void ErasedStringArray_IsRestoredToAnArrayOfStrings(string methodName, string parameterName)
    {
        JsonObject property = ShouldHaveSchemaPropertyFor(methodName, parameterName);

        property["type"]!.GetValue<string>().ShouldBe("array");
        property["items"]!["type"]!.GetValue<string>().ShouldBe("string");
        // No enum constraint: any string is a legal element.
        property["items"]!.AsObject().ContainsKey("enum").ShouldBeFalse();
    }

    // The value list is the whole point: the converter hides the enum from the exporter, so without
    // this repair the allowed values would have to be duplicated into the description prose, where
    // no client can validate against them.
    [Theory]
    [InlineData(nameof(Probe.WithEnum), "severity")]
    // Nullable<T> is unwrapped before the type test, so an optional enum keeps its value list.
    [InlineData(nameof(Probe.WithNullableEnum), "severity")]
    // The costly half of the `true` erasure: no description means no prose to fall back on, so the
    // repaired enum list is the only place a client can learn the allowed values.
    [InlineData(nameof(Probe.WithUndescribedEnum), "severity")]
    public void ErasedEnum_IsRestoredWithTheAllowedValueList(string methodName, string parameterName)
    {
        JsonObject property = ShouldHaveSchemaPropertyFor(methodName, parameterName);

        property["type"]!.GetValue<string>().ShouldBe("string");
        EnumNamesOf(property).ShouldBe(["Hint", "Suggestion", "Warning", "Error"]);
    }

    [Fact]
    public void EnumArray_ErasedToEmpty_IsRestoredToAnArrayCarryingTheAllowedValueList()
    {
        JsonObject property = ShouldHaveSchemaPropertyFor(nameof(Probe.WithEnumArray), "severities");

        property["type"]!.GetValue<string>().ShouldBe("array");
        property["items"]!["type"]!.GetValue<string>().ShouldBe("string");
        EnumNamesOf(property["items"]!.AsObject()).ShouldBe(["Hint", "Suggestion", "Warning", "Error"]);
    }

    [Fact]
    public void ShapeTheExporterStillEmits_IsLeftAlone()
    {
        // bool has no custom converter, so the exporter emits its shape and every repair branch is
        // guarded on !ContainsKey. A repair that overwrote here would silently retype live parameters:
        // arch_graph's overview and allowWorkspaceDiagnostics are both bool.
        JsonObject property = ShouldHaveSchemaPropertyFor(nameof(Probe.WithBool), "flag");

        property["type"]!.GetValue<string>().ShouldBe("boolean");
    }

    [Fact]
    public void RepairedParameter_KeepsItsDescription()
    {
        // The repair rewrites the type node in place; losing the description would strip every
        // parameter's guidance from tools/list.
        JsonObject property = ShouldHaveSchemaPropertyFor(nameof(Probe.WithScalarString), "required");

        property["description"]!.GetValue<string>().ShouldBe("A described parameter.");
    }

    [Theory]
    [InlineData(nameof(Probe.WithUndescribedString), "plain")]
    [InlineData(nameof(Probe.WithUndescribedStringArray), "names")]
    [InlineData(nameof(Probe.WithUndescribedEnum), "severity")]
    public void ExporterWithoutTheHook_ErasesAnUndescribedParameterToTrue(string methodName, string parameterName)
    {
        // The premise the undescribed rows of the restoration theories above rest on, observed with the hook
        // off so it is an assertion rather than a claim: a converter-bound parameter with neither a
        // description nor a default has no keyword left to carry, so the exporter emits the bare `true`
        // schema rather than the `{}` the described parameters land as.
        JsonNode property = SchemaNodeFor(methodName, parameterName, repairErasures: false);

        property.GetValueKind().ShouldBe(JsonValueKind.True);
    }

    [Fact]
    public void UnrepairedType_ErasedToTrue_StaysTrue()
    {
        // `true` is only a repair target for the types the repair knows. A parameter no branch claims
        // is handed back byte-identical rather than widened into an equivalent-but-different `{}`.
        JsonNode property = SchemaNodeFor(nameof(Probe.WithUndescribedObject), "payload");

        property.GetValueKind().ShouldBe(JsonValueKind.True);
    }

    [Fact]
    public void FalseSchema_IsLeftAlone()
    {
        // `false` is not an erasure — it is the claim that no value is valid. Repairing it would widen
        // "nothing allowed" into "anything of this type allowed", the one direction this hook must never
        // move. Asserted against the helper directly because no exporter path reaches the hook with
        // `false`, so there is no probe parameter that could provoke it.
        CoercingToolRegistration.ErasureTarget(JsonValue.Create(false)).ShouldBeNull();
    }

    [Fact]
    public void EveryLiveArchToolParameter_AdvertisesAType()
    {
        // The live half of the repair, and the standing form of "no arch_* parameter may move": each one
        // reaches the client with a type only because this hook puts back what the coercers erased, and
        // both erasures — `{}` and `true` — read to a client as "any value at all".
        List<string> checkedParameters = [];

        foreach (McpServerTool tool in LiveArchTools())
        {
            JsonObject schema = JsonNode.Parse(tool.ProtocolTool.InputSchema.GetRawText())!.AsObject();
            JsonObject properties = schema["properties"]?.AsObject() ?? [];

            foreach ((string parameterName, JsonNode? property) in properties)
            {
                var where = $"{tool.ProtocolTool.Name}.{parameterName}";
                property.ShouldNotBeNull($"{where} advertises a null schema");
                property.ShouldBeOfType<JsonObject>($"{where} is not an object schema: {property.ToJsonString()}");
                property.AsObject()
                    .ContainsKey("type")
                    .ShouldBeTrue($"{where} advertises no type: {property.ToJsonString()}");
                checkedParameters.Add(where);
            }
        }

        // Otherwise a tools/list that advertised no parameters at all would read as a pass.
        checkedParameters.ShouldNotBeEmpty("the live arch_* tools advertise no parameters to check");
    }

    /// <summary>
    ///     The real <c>arch_*</c> tools, created the way the server creates them. The activation callback
    ///     never runs: <c>McpServerTool.Create</c> builds the schema from the method signature, and only a
    ///     <c>tools/call</c> would reach for an instance.
    /// </summary>
    private static IEnumerable<McpServerTool> LiveArchTools()
    {
        McpServerToolCreateOptions options = new()
        {
            SchemaCreateOptions = CoercingToolRegistration.SchemaOptions,
            SerializerOptions = ToolInputSerializerOptions.Instance
        };

        return ToolAttributeDiscovery.GetToolMethods()
            .Where(method => method.GetCustomAttribute<McpServerToolAttribute>() is not null)
            .Select(method => McpServerTool.Create(
                method,
                _ => throw new UnreachableException("A schema-only tool was asked for an instance."),
                options));
    }

    private static string[] EnumNamesOf(JsonObject property)
    {
        return property["enum"]!.AsArray().Select(node => node!.GetValue<string>()).ToArray();
    }

    /// <summary>
    ///     Creates <paramref name="methodName" /> as a tool exactly the way
    ///     <c>CoercingToolRegistration.WithCoercingTools</c> creates the real ones — same schema hook, same
    ///     serializer options — and returns the advertised schema node for <paramref name="parameterName" />.
    /// </summary>
    private static JsonObject ShouldHaveSchemaPropertyFor(string methodName, string parameterName)
    {
        JsonNode property = SchemaNodeFor(methodName, parameterName);
        property.ShouldBeOfType<JsonObject>($"'{parameterName}' is not an object schema: {property.ToJsonString()}");

        return property.AsObject();
    }

    /// <summary>
    ///     The advertised schema node for <paramref name="parameterName" />, whatever its JSON kind — the
    ///     erasure boundary needs to see a node the object-shaped assertions would reject. Pass
    ///     <paramref name="repairErasures" /> as <see langword="false" /> to swap the production hook for a
    ///     default-constructed one, which leaves the exporter's own output — the erasure — visible.
    /// </summary>
    private static JsonNode SchemaNodeFor(string methodName, string parameterName, bool repairErasures = true)
    {
        MethodInfo method = typeof(Probe).GetMethod(methodName)
                            ?? throw new InvalidOperationException($"No probe method '{methodName}'.");

        McpServerToolCreateOptions options = new()
        {
            SchemaCreateOptions = repairErasures ? CoercingToolRegistration.SchemaOptions : new AIJsonSchemaCreateOptions(),
            SerializerOptions = ToolInputSerializerOptions.Instance
        };

        var tool = McpServerTool.Create(method, _ => new Probe(), options);

        JsonObject schema = JsonNode.Parse(tool.ProtocolTool.InputSchema.GetRawText())!.AsObject();
        JsonNode? property = schema["properties"]?[parameterName];
        // The whole schema rides in the message: the first question a red here raises is what the
        // exporter actually emitted, and a bare node assertion cannot answer it.
        property.ShouldNotBeNull($"the schema advertises no '{parameterName}' property: {schema.ToJsonString()}");

        return property;
    }

    /// <summary>
    ///     Carries one parameter of every shape the repair handles, described and undescribed so both
    ///     erasures are reachable, plus a <c>bool</c> the exporter still describes on its own and an
    ///     <c>object</c> no branch claims. Not discoverable as a real tool: <c>ToolAttributeDiscovery</c>
    ///     reflects over the CLI assembly, and this lives in the test assembly.
    /// </summary>
    public sealed class Probe
    {
        public enum Severity
        {
            Hint = 0,
            Suggestion = 1,
            Warning = 2,
            Error = 3
        }

        public string WithScalarString([Description("A described parameter.")] string required)
        {
            return required;
        }

        public string? WithNullableString(string? optional = null)
        {
            return optional;
        }

        public string[] WithStringArray([Description("Some names.")] string[] names)
        {
            return names;
        }

        public Severity WithEnum([Description("How loud.")] Severity severity)
        {
            return severity;
        }

        public Severity[] WithEnumArray([Description("How loud, severally.")] Severity[] severities)
        {
            return severities;
        }

        public string WithUndescribedString(string plain)
        {
            return plain;
        }

        public string[] WithUndescribedStringArray(string[] names)
        {
            return names;
        }

        public Severity WithUndescribedEnum(Severity severity)
        {
            return severity;
        }

        public object WithUndescribedObject(object payload)
        {
            return payload;
        }

        public Severity? WithNullableEnum(Severity? severity = null)
        {
            return severity;
        }

        public bool WithBool(bool flag)
        {
            return flag;
        }
    }
}
