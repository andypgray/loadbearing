// Reflectable mirror types for the FullDisplay ⇄ extraction correspondence pin
// (TypeNameFullDisplayTests). Each shape declared here is re-declared byte-for-byte as source in
// that test and extracted through Roslyn; the two independent renderings must agree. Keep the two
// copies in lockstep — that lockstep IS the pin.

namespace Zphil.LoadBearing.Tests.Correspondence;

public class Simple;

public class Outer
{
    public class Inner;
}

public interface IBox<T>;

public class Pair<TFirst, TSecond>;

public class UsesSimple : IBox<Simple>;

public class UsesInt : IBox<int>;

public class UsesNested : IBox<Outer.Inner>;

public class UsesArray : IBox<Simple[]>;

public class UsesRank2Array : IBox<Simple[,]>;

public class UsesGenericInGeneric : IBox<Pair<Simple, int>>;