using Zphil.LoadBearing.Codebase;
using Zphil.LoadBearing.Tests.Extraction;

namespace Zphil.LoadBearing.Tests.Checking;

/// <summary>
///     Shared source strings for the fast-path checker tests, each beside the one
///     <see cref="CodebaseModel" /> extracted from it.
/// </summary>
/// <remarks>
///     Two jobs, and the second is why the catch scenes moved here. A scene many rows check is compiled
///     once rather than once per row — extraction is the expensive half of a fast-path test. And a scene
///     several <em>classes</em> check is held once rather than copied into each, because those copies are
///     the claim: sibling verbs asserting different verdicts over the same input only mean that while the
///     input is literally the same, and a copy edited in one class desynchronises the others silently.
/// </remarks>
internal static class Sources
{
    /// <summary>
    ///     A layered app: <c>App.Domain</c> types reference <c>App.Web</c> (and one BCL type), plus a
    ///     same-layer edge (Service → Model) and two extra violators (Apple, Zebra → Controller) for
    ///     the ordering pin.
    /// </summary>
    public const string Layered = """
                                  namespace App.Web { public class Controller {} public class Helper {} }
                                  namespace App.Domain
                                  {
                                      using System.Text;
                                      public class Service
                                      {
                                          public App.Web.Controller C;
                                          public App.Web.Helper H;
                                          public App.Domain.Model M;
                                          public StringBuilder Log;
                                      }
                                      public class Model {}
                                      public class Apple { public App.Web.Controller C; }
                                      public class Zebra { public App.Web.Controller C; }
                                  }
                                  """;

    /// <summary>
    ///     A quarantined legacy scope with a facade: <c>App.Client.User</c> references both the facade
    ///     (<c>IFacade</c>, sanctioned) and an interior type (<c>Internal</c>, not sanctioned).
    /// </summary>
    public const string Containment = """
                                      namespace App.Legacy { public interface IFacade {} public class Impl {} public class Internal {} }
                                      namespace App.Client
                                      {
                                          public class User
                                          {
                                              public App.Legacy.Internal Direct;
                                              public App.Legacy.IFacade Via;
                                          }
                                      }
                                      """;

    /// <summary>
    ///     A quarantined legacy scope whose sanctioned surface is named without loading it: a region
    ///     (<c>App.Legacy.Contracts.*</c>) and a consumer that lives outside the scope entirely
    ///     (<c>Sanctioned</c>). <c>Gateway</c> reaches the interior as a facade must; <c>Sanctioned</c> and
    ///     <c>Other</c> reach it identically from outside and differ only in their names; <c>Caller</c>
    ///     reaches the facade region.
    /// </summary>
    public const string ContainmentRegion = """
                                            namespace App.Legacy { public class Internal {} }
                                            namespace App.Legacy.Contracts { public class Gateway { public App.Legacy.Internal Inner; } }
                                            namespace App.Client
                                            {
                                                public class Sanctioned { public App.Legacy.Internal Direct; }
                                                public class Other { public App.Legacy.Internal Direct; }
                                                public class Caller { public App.Legacy.Contracts.Gateway Via; }
                                            }
                                            """;

    /// <summary>
    ///     The hierarchy fixture: the reflectable <c>Targets</c> types (mirrored from
    ///     <see cref="Targets" />) plus subjects exercising every hierarchy verb — one implementer,
    ///     one deriver, one attributed type, two <c>IHandler</c> constructions, and their negatives.
    /// </summary>
    public const string Hierarchy = """
                                    using System;
                                    namespace Zphil.LoadBearing.Tests.Checking.Targets;
                                    public interface IThing {}
                                    public interface IHandler<T> {}
                                    public class ThingBase {}
                                    public sealed class MarkAttribute : Attribute {}
                                    public class Order {}
                                    public class Widget : IThing {}
                                    public class Gizmo {}
                                    public class SubType : ThingBase {}
                                    public class FreeType {}
                                    public class OrderHandler : IHandler<Order> {}
                                    public class TextHandler : IHandler<string> {}
                                    [Mark] public class Tagged {}
                                    public class Plain {}
                                    """;

    /// <summary>
    ///     Transitivity / type-argument-substitution / declared-only-attribute fixture for the negative
    ///     hierarchy verbs (GRAMMAR §5.2–§5.3): a transitive interface implementer (via a base class), a
    ///     substitution handler (a class extending a generic base that implements <c>IHandler&lt;T&gt;</c>),
    ///     and an attributed base with an un-attributed derived type. Anchors are the reflectable
    ///     <see cref="Targets" /> types, re-declared here in lockstep (a separate compilation) — the same
    ///     discipline as <see cref="Hierarchy" />.
    /// </summary>
    public const string HierarchyTransitive = """
                                              using System;
                                              namespace Zphil.LoadBearing.Tests.Checking.Targets;
                                              public interface IThing {}
                                              public interface IHandler<T> {}
                                              public class Order {}
                                              public sealed class MarkAttribute : Attribute {}
                                              public class Widget : IThing {}
                                              public class WidgetChild : Widget {}
                                              public class HandlerBase<T> : IHandler<T> {}
                                              public class SubstHandler : HandlerBase<Order> {}
                                              [Mark] public class AttrBase {}
                                              public class AttrDerived : AttrBase {}
                                              """;

    /// <summary>
    ///     The external-anchor fixture (GRAMMAR §5.2): subjects whose base chain leaves the compilation —
    ///     one deriving from a BCL type directly, one reaching it through an intermediate <em>external</em>
    ///     base (<c>ArgumentException : SystemException : Exception</c>) — plus a non-deriver. The shallow
    ///     hierarchy external types carry is a fact about the <em>subject</em> position: a declared type's
    ///     base chain is walked through metadata, so an external <em>anchor</em> is matchable in both anchor
    ///     forms. Its own compilation because the <see cref="Hierarchy" /> pins assert exact selected sets.
    /// </summary>
    public const string ExternalBaseHierarchy = """
                                                namespace Zphil.LoadBearing.Tests.Checking.Targets;
                                                public class DirectDeriver : System.Exception {}
                                                public class IndirectDeriver : System.ArgumentException {}
                                                public class Unrelated {}
                                                """;

    /// <summary>
    ///     The generic-attribute fixture for string attribute anchors (GRAMMAR §5.2): one generic attribute
    ///     applied at two constructions, plus a non-generic one and a bare type. A definition-name anchor
    ///     must reach both constructions while a constructed spelling reaches neither. Its own compilation
    ///     because the <see cref="Hierarchy" /> pins assert exact selected sets — a new attributed type
    ///     there would move them.
    /// </summary>
    public const string GenericAttributes = """
                                            using System;
                                            namespace Zphil.LoadBearing.Tests.Checking.Targets;
                                            public sealed class MarkAttribute<T> : Attribute {}
                                            public sealed class PlainAttribute : Attribute {}
                                            [Mark<int>] public class TaggedInt {}
                                            [Mark<string>] public class TaggedText {}
                                            [Plain] public class TaggedPlain {}
                                            public class Untagged {}
                                            """;

    /// <summary>
    ///     The exact-FQN matching scene the three catch verbs share: <c>Broad</c> catches
    ///     <c>System.Exception</c> while <c>Narrow</c> catches <c>System.IO.IOException</c>, so a ban on
    ///     <c>typeof(Exception)</c> reds the first and never the second — the narrow catch is the good state
    ///     each verb rewards, and Broad's presence proves the ban is live rather than vacuously empty.
    /// </summary>
    public const string CatchBroadVsNarrow = """
                                             namespace App
                                             {
                                                 public class Broad
                                                 {
                                                     public void Run() { try { } catch (System.Exception) { } }
                                                 }
                                                 public class Narrow
                                                 {
                                                     public void Run() { try { } catch (System.IO.IOException) { } }
                                                 }
                                             }
                                             """;

    /// <summary>
    ///     The hierarchy-adjective operand scene the three catch verbs share: <c>Worker</c> catches its own
    ///     <c>N.AppError</c> — solution-declared and derived from <c>Exception</c>, so an
    ///     <c>arch.Types.DerivedFrom</c> operand matches it — and an external
    ///     <c>System.InvalidOperationException</c>, which such an operand never matches, external types
    ///     carrying a shallow hierarchy.
    /// </summary>
    public const string CatchDerivedFrom = """
                                           namespace N
                                           {
                                               public class AppError : System.Exception {}
                                               public class Worker
                                               {
                                                   public void Run()
                                                   {
                                                       try { } catch (N.AppError) { }
                                                       try { } catch (System.InvalidOperationException) { }
                                                   }
                                               }
                                           }
                                           """;

    /// <summary>
    ///     The type-pair ratchet scene the three catch verbs share: one <c>Handler</c> catching both
    ///     <c>Errors.AErr</c> and <c>Errors.BErr</c>, so a baseline blessing the first edge leaves the
    ///     second a distinct identity and red (GRAMMAR §4.3).
    /// </summary>
    public const string CatchRatchet = """
                                       namespace Errors { public class AErr : System.Exception {} public class BErr : System.Exception {} }
                                       namespace App
                                       {
                                           public class Handler
                                           {
                                               public void Run()
                                               {
                                                   try { } catch (Errors.AErr) { }
                                                   try { } catch (Errors.BErr) { }
                                               }
                                           }
                                       }
                                       """;

    /// <summary>
    ///     A controller opening the data layer directly — one forbidden edge
    ///     (<c>OldController -&gt; App.Data.Db</c>).
    /// </summary>
    public const string OneController = """
                                        namespace App.Web { public class OldController { public App.Data.Db Load() => new App.Data.Db(); } }
                                        namespace App.Data { public class Db {} }
                                        """;

    /// <summary>
    ///     The same forbidden edge reached from two distinct lines. Sites are deduped per
    ///     <c>file:line</c>, so this is two sites under one identity — the shape the measure counts.
    /// </summary>
    public const string TwoSiteController = """
                                            namespace App.Web
                                            {
                                                public class OldController
                                                {
                                                    public App.Data.Db A() => new App.Data.Db();
                                                    public App.Data.Db B() => new App.Data.Db();
                                                }
                                            }
                                            namespace App.Data { public class Db {} }
                                            """;

    /// <summary>
    ///     The Migrate rule the controller scenes are checked against: Web controllers must not
    ///     reference the data layer. It omits <c>.Baseline</c>, so its conventional path is
    ///     <c>arch/baselines/data/x.json</c> (GRAMMAR §4.4).
    /// </summary>
    public static void NoDataAccess(Arch arch)
    {
        arch.Rule("data/x")
            .Migrate(
                "Controllers open the data layer directly (legacy Active Record style).",
                arch.Namespace("App.Web.*").WithSuffix("Controller").MustNotReference(arch.Namespace("App.Data.*")))
            .Because("Repository pattern for testability.");
    }

    /// <summary>The one extracted model of <see cref="Layered" />.</summary>
    public static readonly CodebaseModel LayeredModel = CompilationFactory.Extract(Layered);

    /// <summary>The one extracted model of <see cref="Containment" />.</summary>
    public static readonly CodebaseModel ContainmentModel = CompilationFactory.Extract(Containment);

    /// <summary>The one extracted model of <see cref="ContainmentRegion" />.</summary>
    public static readonly CodebaseModel ContainmentRegionModel = CompilationFactory.Extract(ContainmentRegion);

    /// <summary>The one extracted model of <see cref="Hierarchy" />.</summary>
    public static readonly CodebaseModel HierarchyModel = CompilationFactory.Extract(Hierarchy);

    /// <summary>The one extracted model of <see cref="CatchBroadVsNarrow" />.</summary>
    public static readonly CodebaseModel CatchBroadVsNarrowModel = CompilationFactory.Extract(CatchBroadVsNarrow);

    /// <summary>The one extracted model of <see cref="CatchDerivedFrom" />.</summary>
    public static readonly CodebaseModel CatchDerivedFromModel = CompilationFactory.Extract(CatchDerivedFrom);

    /// <summary>The one extracted model of <see cref="CatchRatchet" />.</summary>
    public static readonly CodebaseModel CatchRatchetModel = CompilationFactory.Extract(CatchRatchet);

    /// <summary>The one extracted model of <see cref="OneController" />.</summary>
    public static readonly CodebaseModel OneControllerModel = CompilationFactory.Extract(OneController);

    /// <summary>The one extracted model of <see cref="TwoSiteController" />.</summary>
    public static readonly CodebaseModel TwoSiteControllerModel = CompilationFactory.Extract(TwoSiteController);
}
