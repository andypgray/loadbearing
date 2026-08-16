using System.Reflection;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Shouldly;
using Xunit;
using Zphil.LoadBearing.Cli.Mcp.Pipeline;

namespace Zphil.LoadBearing.Tests.Mcp;

/// <summary>
///     <see cref="ToolAttributeDiscovery.IsJsonBoundParameter" />: the one definition of which tool
///     parameters arrive in a <c>tools/call</c> argument object rather than from request context or DI.
///     Two surfaces read their answer from it — <c>UnknownParameterGuard</c>, which rejects a key matching
///     no parameter, and <c>CoercerCoverageTests</c>, which demands a coercer per bound type — so an
///     exclusion that stopped excluding would widen both at once.
/// </summary>
/// <remarks>
///     Only <see cref="CancellationToken" /> is live: every <c>arch_*</c> tool declares one, and services
///     arrive through primary constructors rather than method parameters. The rest of the list is
///     defensive, mirroring the SDK's own augmentation set, and this is the only place that says what it
///     is for — no real tool signature can demonstrate it.
/// </remarks>
public sealed class ToolAttributeDiscoveryTests
{
    [Theory]
    [InlineData(nameof(Probe.Path), true)]
    [InlineData(nameof(Probe.Flag), true)]
    [InlineData(nameof(Probe.Cancellation), false)]
    [InlineData(nameof(Probe.Arguments), false)]
    [InlineData(nameof(Probe.Services), false)]
    [InlineData(nameof(Probe.Server), false)]
    [InlineData(nameof(Probe.Request), false)]
    [InlineData(nameof(Probe.Progress), false)]
    [InlineData(nameof(Probe.Keyed), false)]
    public void IsJsonBoundParameter_SeparatesArgumentsFromContext(string methodName, bool jsonBound)
    {
        MethodInfo method = typeof(Probe).GetMethod(methodName)
                            ?? throw new InvalidOperationException($"No probe method '{methodName}'.");
        ParameterInfo parameter = method.GetParameters()
            .Single();

        ToolAttributeDiscovery.IsJsonBoundParameter(parameter)
            .ShouldBe(
                jsonBound,
                $"{methodName}'s {parameter.ParameterType.Name} parameter");
    }

    /// <summary>
    ///     One parameter per shape the predicate rules on, each alone in its method so the reflection above
    ///     can take it without naming it. Not a tool type: discovery reflects over the CLI assembly, and
    ///     this lives in the test assembly.
    /// </summary>
    public sealed class Probe
    {
        public void Path(string path)
        {
        }

        public void Flag(bool overview)
        {
        }

        public void Cancellation(CancellationToken cancellationToken)
        {
        }

        public void Arguments(AIFunctionArguments arguments)
        {
        }

        public void Services(IServiceProvider services)
        {
        }

        public void Server(McpServer server)
        {
        }

        public void Request(RequestContext<CallToolRequestParams> request)
        {
        }

        // The predicate matches the open generic, so the argument here is immaterial.
        public void Progress(IProgress<int> progress)
        {
        }

        public void Keyed([FromKeyedServices("solution")] string solution)
        {
        }
    }
}
