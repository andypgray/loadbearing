namespace Zphil.LoadBearing;

/// <summary>
///     A unit of architecture specification. Spec classes implement this; they are discovered by
///     reflection (<see cref="Discovery.SpecDiscovery" />) and each is invoked exactly once per
///     model build with a fresh <see cref="Arch" /> (GRAMMAR §3.2, fresh-instance contract).
/// </summary>
public interface IArchitectureSpec
{
    /// <summary>
    ///     Registers layers, rules, and scopes on <paramref name="arch" />. The method mutates
    ///     <paramref name="arch" />; it never evaluates anything (rules are data, GRAMMAR §2).
    /// </summary>
    void Define(Arch arch);
}