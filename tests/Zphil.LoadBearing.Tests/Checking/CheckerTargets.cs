// Reflectable target types for checker specs that use typeof(...). Sources.HierarchySource
// re-declares these plus subject types; the two copies must stay in lockstep — that IS the pin, the
// same discipline as the FullDisplay correspondence test.

namespace Zphil.LoadBearing.Tests.Checking.Targets;

public interface IThing;

public interface IHandler<T>;

public class ThingBase;

public sealed class MarkAttribute : Attribute;

public class Order;