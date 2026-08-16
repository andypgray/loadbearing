using Microsoft.CodeAnalysis;

namespace Zphil.LoadBearing.Roslyn;

/// <summary>
///     Extraction's symbol-tolerance policy, held in one place: the symbol a binding resolved to, or — when
///     it resolved to none — the compiler's first candidate.
/// </summary>
/// <remarks>
///     The policy is a product decision, not a convenience. LoadBearing's subject is a messy legacy codebase
///     that may not fully compile against the references at hand, so a name the compiler could only guess at
///     still contributes its edge rather than vanishing from the model. Falling back to
///     <see cref="SymbolInfo.CandidateSymbols" /> is how that guess is honoured, and the downstream gates
///     (<see cref="TypeKindMapper" /> on the type channels, the recognizer's own symbol-first gate on the
///     registration channel) are what stop a wrong guess from minting something false. Every site that binds
///     a name for extraction goes through here, so the tolerance cannot be granted on one channel and
///     silently withheld on another.
/// </remarks>
internal static class SymbolInfoExtensions
{
    /// <summary>The bound symbol, else the first candidate, else <see langword="null" />.</summary>
    internal static ISymbol? BestCandidate(this SymbolInfo info)
    {
        return info.Symbol ?? info.CandidateSymbols.FirstOrDefault();
    }
}
