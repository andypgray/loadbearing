using System.Reflection;
using System.Text.Json;
using ModelContextProtocol.Server;
using Shouldly;
using Xunit;
using Zphil.LoadBearing.Cli.Mcp.Pipeline;

namespace Zphil.LoadBearing.Tests.Mcp;

/// <summary>
///     Tests for <see cref="UnknownParameterGuard" />: a JSON argument key matching no declared parameter
///     of an <c>arch_*</c> tool is surfaced as an actionable, self-correcting error instead of being
///     silently dropped by the SDK's <c>UnmappedMemberHandling = Skip</c>.
/// </summary>
public sealed class UnknownParameterGuardTests
{
    // Value is never inspected by the guard (only keys are), so a single shared dummy suffices.
    private static readonly JsonElement DummyValue = JsonDocument.Parse("null").RootElement;

    [Fact]
    public void Validate_UnknownKeyOnRealTool_NamesBadKeyToolAndValidList()
    {
        // Act — "rule" is the classic typo of arch_explain's "ruleId" parameter.
        string? message = UnknownParameterGuard.Validate(
            "arch_explain",
            new Dictionary<string, JsonElement> { ["rule"] = DummyValue });

        // Assert — names the bad key (quoted), the tool, and the real parameter list.
        message.ShouldNotBeNull();
        message.ShouldContain("\"rule\"");
        message.ShouldContain("arch_explain");
        message.ShouldContain("ruleId");
    }

    [Fact]
    public void Validate_EveryDeclaredParameter_ReturnsNull()
    {
        // Arrange — independently reflect every tool's JSON parameter names. Services arrive via primary
        // constructors, so the only context-bound method parameter is CancellationToken; IsJsonBound
        // encodes exactly that, independently of the guard's own predicate. A newly introduced
        // context-bound parameter type will (correctly) trip this test.
        List<string> failures = [];

        foreach (MethodInfo method in ToolAttributeDiscovery.GetToolMethods())
        {
            if (method.GetCustomAttribute<McpServerToolAttribute>()?.Name is not { } toolName) continue;

            var arguments = method.GetParameters()
                .Where(IsJsonBound)
                .ToDictionary(p => p.Name!, _ => DummyValue);

            string? message = UnknownParameterGuard.Validate(toolName, arguments);
            if (message is not null) failures.Add($"{toolName}: {message}");
        }

        // Assert — every real parameter name is accepted; any failure is schema drift.
        failures.ShouldBeEmpty();
    }

    [Fact]
    public void Validate_CaseInsensitiveKey_ReturnsNull()
    {
        // Act — a casing slip binds anyway under Web defaults, so it must not be flagged.
        string? message = UnknownParameterGuard.Validate(
            "arch_explain",
            new Dictionary<string, JsonElement> { ["RuleId"] = DummyValue });

        // Assert
        message.ShouldBeNull();
    }

    [Fact]
    public void Validate_KnownKeyOnCheck_ReturnsNull()
    {
        // Act — arch_check's real optional parameter.
        string? message = UnknownParameterGuard.Validate(
            "arch_check",
            new Dictionary<string, JsonElement> { ["diffBase"] = DummyValue });

        // Assert
        message.ShouldBeNull();
    }

    [Fact]
    public void Validate_UnknownToolName_ReturnsNull()
    {
        // Act — unknown-tool dispatch is the SDK's concern; the guard never blocks it.
        string? message = UnknownParameterGuard.Validate(
            "no_such_tool",
            new Dictionary<string, JsonElement> { ["whatever"] = DummyValue });

        // Assert
        message.ShouldBeNull();
    }

    [Fact]
    public void Validate_NullArguments_ReturnsNull()
    {
        UnknownParameterGuard.Validate("arch_check", null).ShouldBeNull();
    }

    [Fact]
    public void Validate_EmptyArguments_ReturnsNull()
    {
        UnknownParameterGuard.Validate("arch_check", new Dictionary<string, JsonElement>()).ShouldBeNull();
    }

    [Theory]
    [InlineData("file")]
    [InlineData("filePath")]
    [InlineData("directory")]
    public void Validate_HallucinatedKeyOnContext_ReturnsError(string hallucinatedKey)
    {
        // Act — the keys a model reaches for instead of arch_context's real "path" parameter.
        string? message = UnknownParameterGuard.Validate(
            "arch_context",
            new Dictionary<string, JsonElement> { [hallucinatedKey] = DummyValue });

        // Assert
        message.ShouldNotBeNull();
        message.ShouldContain($"\"{hallucinatedKey}\"");
        message.ShouldContain("arch_context");
        message.ShouldContain("path");
    }

    // Independent oracle for "is this a JSON-bound parameter": the only context-bound method-parameter
    // type in this server is CancellationToken. Deliberately NOT calling the guard's own predicate.
    private static bool IsJsonBound(ParameterInfo p)
    {
        return p.Name is not null && p.ParameterType != typeof(CancellationToken);
    }
}