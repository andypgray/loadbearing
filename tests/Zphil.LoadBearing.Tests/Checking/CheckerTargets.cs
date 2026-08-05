// Reflectable target types for checker specs that use typeof(...). Sources.HierarchySource
// re-declares these plus subject types; the two copies must stay in lockstep — that IS the pin, the
// same discipline as the FullDisplay correspondence test.
// The sub-namespace keeps the targets apart from the tests that select them, and the arity of IHandler<>
// is what the specs bind to — neither follows from the folder or from a use of T.
// ReSharper disable once CheckNamespace

namespace Zphil.LoadBearing.Tests.Checking.Targets;

public interface IThing;

// ReSharper disable once UnusedTypeParameter
public interface IHandler<T>;

public class ThingBase;

public sealed class MarkAttribute : Attribute;

public class Order;
