namespace Zphil.LoadBearing.Tests.Checking;

/// <summary>Shared source strings for the fast-path checker tests (extracted via CompilationFactory).</summary>
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
    ///     A frozen legacy scope with a facade: <c>App.Client.User</c> references both the facade
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
}