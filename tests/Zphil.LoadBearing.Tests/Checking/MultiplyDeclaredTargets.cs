// Reflectable twins of the two types the multiply-declared checker bed compiles into three projects at
// once, for the rows there that name a target with typeof(...). The lockstep is on the NAMES alone —
// nothing reflects on a member here, and a typeof operand resolves against the model by fully-qualified
// name — so these stay empty while the bed's own copies carry the members its member rows reach. Keeping
// the two spellings of `Shared.Widget` and `Shared.WidgetPart` in step IS the pin, the same discipline
// CheckerTargets holds for the hierarchy anchors.
// The namespace is the bed's, not the folder's: a typeof twin has to wear the name the extracted node wears.
// ReSharper disable once CheckNamespace

namespace Shared;

public class Widget;

public class WidgetPart;
