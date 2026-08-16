#if NET
namespace MultiTfm.Core
{
    // Compiled under net10.0 only (NET is defined there and absent on netstandard2.0), so exactly one
    // framework declares it: the union unions, and a type only one framework declares must NOT change the
    // multi-framework note, whose subject is the types the frameworks share.
    public class ModernOnly
    {
    }
}
#endif
