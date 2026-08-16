using System.Collections.Concurrent;
using Microsoft.CodeAnalysis;
using Zphil.LoadBearing.Cli.SpecLoading;

namespace Zphil.LoadBearing.Tests.TestSupport;

/// <summary>
///     A compile-time stand-in for the <c>Zphil.LoadBearing</c> contract at a chosen
///     <see cref="Version" /> — the one thing the suite cannot get from the real core, whose
///     <c>AssemblyVersion</c> is pinned to <c>1.0.0.0</c> on purpose. A spec compiled against it carries an
///     assembly ref to that identity, which is what makes the version-agnostic bind in
///     <see cref="SpecLoadContext.Load" /> (and the <c>SpecContractMismatch</c> failure it defers to)
///     testable end to end.
/// </summary>
/// <remarks>
///     <para>
///         Never loaded — only referenced. The image goes straight to a <see cref="MetadataReference" />,
///         so nothing on disk and nothing in any <c>AssemblyLoadContext</c> ever carries a second copy of
///         these type names; the source is a string constant, so <c>MSBuildWorkspace</c> sees a literal and
///         the dogfood universe (self-spec subjects, <c>arch_graph</c> inventories, the <c>AGENTS.md</c>
///         render) is untouched.
///     </para>
///     <para>
///         <b>Every member here must match the real declaration exactly.</b> The memberrefs a spec emits
///         against this stub are resolved at run time against the host's real contract, so a drifted
///         signature does not go unnoticed — it reds the <em>green</em> arms of
///         <c>SpecContractSkewE2ETests</c> with the runtime naming the exact member that failed to resolve.
///         That is the guard doing its job, not a gap in it. The caller-info optionals on
///         <see cref="Arch.Rule" /> are part of that: without them the call site binds a one-argument
///         <c>Rule(string)</c> the host does not declare.
///     </para>
///     <para>
///         <c>FutureVerb</c> is the member the host deliberately does not have, and it is a member on a
///         <em>present</em> type on purpose. An absent <em>type</em> fails earlier, at typeref resolution,
///         and lands on <c>ModelPipeline</c>'s <see cref="TypeLoadException" /> arm instead — the wrong arm
///         for this subject.
///     </para>
/// </remarks>
internal static class SkewedContract
{
    // Only the surface the skew specs touch. Bodies never run: this assembly is a reference, never a load.
    private const string Source = """
                                  #nullable enable
                                  using System;
                                  using System.Reflection;
                                  using System.Runtime.CompilerServices;
                                  using Zphil.LoadBearing.Fluent;

                                  [assembly: AssemblyVersion("{VERSION}")]

                                  namespace Zphil.LoadBearing.Fluent
                                  {
                                      public interface IEnforceRule
                                      {
                                          IEnforceRule Because(string because);
                                      }
                                  }

                                  namespace Zphil.LoadBearing
                                  {
                                      public abstract class Selection
                                      {
                                      }

                                      public abstract class Constraint
                                      {
                                      }

                                      public interface IArchitectureSpec
                                      {
                                          void Define(Arch arch);
                                      }

                                      public interface IRuleBuilder
                                      {
                                          IEnforceRule Enforce(Constraint constraint);
                                      }

                                      public sealed class Arch
                                      {
                                          public Selection Types => throw new NotSupportedException();

                                          public IRuleBuilder Rule(string id,
                                              [CallerFilePath] string? filePath = null, [CallerLineNumber] int lineNumber = 0)
                                          {
                                              throw new NotSupportedException();
                                          }

                                          public void FutureVerb(string note)
                                          {
                                              throw new NotSupportedException();
                                          }
                                      }

                                      public static class SelectionConstraints
                                      {
                                          public static Constraint MustHaveSuffix(this Selection subject, string suffix)
                                          {
                                              throw new NotSupportedException();
                                          }
                                      }
                                  }
                                  """;

    // The emitted image is a pure function of the version and a MetadataReference is immutable, so one
    // compile per distinct version serves the whole run rather than one per request.
    private static readonly ConcurrentDictionary<Version, MetadataReference> ByVersion = new();

    /// <summary>
    ///     The stand-in contract at <paramref name="version" />, as a reference to compile a spec against.
    /// </summary>
    internal static MetadataReference Reference(Version version)
    {
        return ByVersion.GetOrAdd(version, Emit);
    }

    private static MetadataReference Emit(Version version)
    {
        byte[] image = SpecAssemblyCompiler.EmitImage(
            Source.Replace("{VERSION}", version.ToString(), StringComparison.Ordinal),
            SpecLoadContext.ContractAssemblyName,
            SpecAssemblyCompiler.PlatformReferences);
        return MetadataReference.CreateFromImage(image);
    }
}
