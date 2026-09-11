using System.Security.Cryptography;
using System.Text;

namespace Zphil.LoadBearing.Baselines;

/// <summary>
///     Seals baseline entries, holding one hash instance across all of them.
/// </summary>
/// <remarks>
///     A seal is per entry rather than per file, so composing or verifying a file of a thousand entries
///     asks for a thousand hashes. Creating a <see cref="SHA256" /> for each one costs more than taking
///     any of them; one instance, reused, keeps the per-entry cost to the hash itself. Not thread-safe,
///     which is why it is passed down one composition or one read rather than held anywhere: a hash
///     instance carries state between <c>ComputeHash</c> calls.
///     <see cref="BaselineFormat.ComputeSeal" /> is the single-entry way in, and creates its own — it is
///     a diagnostic, not this path.
/// </remarks>
internal sealed class BaselineSealer : IDisposable
{
    private readonly SHA256 _sha = SHA256.Create();

    public void Dispose()
    {
        _sha.Dispose();
    }

    /// <summary>
    ///     The seal for <paramref name="entry" /> in the section for <paramref name="ruleId" />, as
    ///     <see cref="BaselineFormat.ComputeSeal" /> defines it.
    /// </summary>
    internal string Seal(string ruleId, BaselineEntry entry)
    {
        string input = BaselineFormat.SealInput(ruleId, entry);
        byte[] hash = _sha.ComputeHash(Encoding.UTF8.GetBytes(input));
        return BaselineFormat.ToLowerHex(hash, BaselineFormat.SealBytes);
    }
}
