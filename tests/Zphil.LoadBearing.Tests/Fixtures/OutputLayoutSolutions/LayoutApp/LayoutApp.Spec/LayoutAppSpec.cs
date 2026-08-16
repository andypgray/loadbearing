using Zphil.LoadBearing;

namespace LayoutApp.Spec
{
    // One rule that holds over LayoutApp.Core, so a resolved spec exits 0 and names itself in the report.
    // The rule ID is what the test asserts on: a path coming back proves nothing, a rendered verdict proves
    // the DLL the layout hid was found, loaded and run. A shape rule rather than a reference rule, because a
    // reference rule's target selection would match no types in this tiny universe and the rule would render
    // `warn ... inert` instead of `pass`.
    public sealed class LayoutAppSpec : IArchitectureSpec
    {
        public void Define(Arch arch)
        {
            arch.Rule("layout/naming-interfaces")
                .Enforce(arch.Types.OfKind(TypeKind.Interface).InNamespace("LayoutApp.Core.*").MustHavePrefix("I"))
                .Because("House naming convention; agents grep by I-prefix.");
        }
    }
}
