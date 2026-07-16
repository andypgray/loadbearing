// Reflectable member-target types for MustNotUse specs (typeof + nameof). MemberUseVerbTests.Scene
// re-declares these — with their members — plus subject types that use them; the two copies must stay
// in lockstep, the same discipline as CheckerTargets / Sources.Hierarchy and the FullDisplay
// correspondence test.

namespace Zphil.LoadBearing.Tests.Checking.MemberTargets;

public sealed class Clock
{
    public int Ticks { get; set; }

    public void Advance()
    {
    }

    public void Advance(int by)
    {
    }
}

public interface IGauge
{
    void Read();
}

public sealed class Gauge : IGauge
{
    public void Read()
    {
    }
}