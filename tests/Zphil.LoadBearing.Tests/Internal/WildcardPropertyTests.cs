using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Shouldly;
using Zphil.LoadBearing.Internal;

namespace Zphil.LoadBearing.Tests.Internal;

/// <summary>
///     <see cref="Wildcard.Match" /> stated over generated patterns rather than a table of them. It is a
///     hand-written iterative matcher that backtracks by letting the last <c>*</c> swallow one more character,
///     and it decides which rules a run evaluates, which projects are surveyed, and which namespaces and type
///     names a rule reaches — but nothing downstream can tell that it answered wrongly. The dangerous direction
///     is the quiet one: a pattern that matches too little leaves a rule enforced over nothing and the report
///     still says passed.
/// </summary>
/// <remarks>
///     <c>NamespacePatternTests</c> and <c>TypeNamePatternTests</c> reach this method through two tokenizers and
///     pin the pairs someone thought to write down, which is what makes them the spec for GRAMMAR §4.2 and §5.2.
///     What they cannot state is the matcher's own contract over inputs nobody enumerated, which is where a
///     backtracking bug lives. Dot-crossing is deliberately absent here: <see cref="Wildcard" /> has no notion of
///     a separator, so a property about dots would be asserting the tokenizer's behaviour at the wrong level.
/// </remarks>
public sealed class WildcardPropertyTests
{
    /// <summary>
    ///     Two letters, one of them also drawn in upper case, and a dot — plus <c>*</c> on the pattern side. A
    ///     wide alphabet makes matches vanishingly rare and leaves a property exercising only its false arm; four
    ///     characters make near-misses and dense wildcards the common case, which is the whole point of
    ///     generating them. <c>A</c> earns its place specifically: without a character that differs from another
    ///     only in case, no generated pair can tell an ordinal comparison from an insensitive one, and the
    ///     case-sensitivity claim below would be untested prose.
    /// </summary>
    private const string TextAlphabet = "aAb.";

    private const string PatternAlphabet = "aAb.*";

    /// <summary>
    ///     The matcher against the definition of <c>*</c> it implements. This is the property that would have
    ///     caught the backtracking wrong, and the other two state in a caller's terms what it states in the
    ///     algorithm's.
    /// </summary>
    [Property(MaxTest = 1000)]
    public Property Match_AnyPatternAndText_AgreesWithTheRecursiveDefinition()
    {
        Gen<(string Pattern, string Text)> cases = StringOf(PatternAlphabet, 0, 8)
            .SelectMany(_ => StringOf(TextAlphabet, 0, 8), (pattern, text) => (Pattern: pattern, Text: text));

        return Prop.ForAll(
            cases.ToArbitrary(),
            testCase =>
            {
                // Arrange
                bool expected = MatchesByDefinition(testCase.Pattern, testCase.Text);

                // Act
                bool matched = Wildcard.Match(testCase.Pattern, testCase.Text);

                // Assert
                matched.ShouldBe(
                    expected,
                    $"`{testCase.Pattern}` against \"{testCase.Text}\": the iterative matcher and the recursive "
                    + "definition of `*` must agree. Where they do not it is the backtracking that is wrong — "
                    + "the definition has nothing to get wrong.");
            });
    }

    /// <summary>
    ///     The contract every literal pattern relies on, and the one a case-insensitive comparison would break
    ///     silently: without a <c>*</c> there is nothing to match but the string itself.
    /// </summary>
    [Property]
    public Property Match_PatternCarryingNoWildcard_IsOrdinalEquality()
    {
        Gen<(string Pattern, string Text)> unrelated = StringOf(TextAlphabet, 0, 6)
            .SelectMany(_ => StringOf(TextAlphabet, 0, 6), (pattern, text) => (Pattern: pattern, Text: text));

        // Both near-miss shapes are unioned in rather than waited for. A free draw almost never produces an equal
        // pair, so without the first arm the property only ever exercises its false side. The second is the one
        // that carries the claim: a pair differing from another only in case is rarer still — measured, a
        // case-insensitive matcher survived this property at 100 cases until this arm existed — and it is
        // exactly what an ordinal comparison must reject and an insensitive one must not.
        Gen<(string Pattern, string Text)> identical = StringOf(TextAlphabet, 0, 6)
            .Select(pattern => (Pattern: pattern, Text: pattern));

        Gen<(string Pattern, string Text)> caseFlipped = StringOf(TextAlphabet, 1, 6)
            .Select(pattern => (Pattern: pattern, Text: pattern.ToUpperInvariant()));

        return Prop.ForAll(
            Gen.OneOf(unrelated, identical, caseFlipped).ToArbitrary(),
            testCase =>
            {
                // Act
                bool matched = Wildcard.Match(testCase.Pattern, testCase.Text);

                // Assert
                matched.ShouldBe(
                    string.Equals(testCase.Pattern, testCase.Text, StringComparison.Ordinal),
                    $"`{testCase.Pattern}` carries no `*`, so it matches \"{testCase.Text}\" exactly when the "
                    + "two are the same string — ordinally, because GRAMMAR pins matching as case-sensitive.");
            });
    }

    /// <summary>
    ///     The safety-critical shape, stated at the level the call sites use it: a trailing <c>*</c> reaches every
    ///     text carrying the literal prefix and no other. <c>MyApp.Legacy*</c> must reach
    ///     <c>MyApp.LegacyBilling</c> and must not reach <c>MyApp.Ledger</c>, and a <c>--rules</c> glob that
    ///     matches too little reports passed for a rule that never ran.
    /// </summary>
    [Property]
    public Property Match_LiteralPrefixWithTrailingWildcard_IsOrdinalStartsWith()
    {
        Gen<(string Prefix, string Text)> unrelated = StringOf(TextAlphabet, 0, 6)
            .SelectMany(_ => StringOf(TextAlphabet, 0, 8), (prefix, text) => (Prefix: prefix, Text: text));

        // As above: the text that genuinely extends the prefix is drawn deliberately, so every seed exercises
        // the arm where the pattern is supposed to match.
        Gen<(string Prefix, string Text)> extended = StringOf(TextAlphabet, 0, 6)
            .SelectMany(
                _ => StringOf(TextAlphabet, 0, 6),
                (prefix, suffix) => (Prefix: prefix, Text: prefix + suffix));

        return Prop.ForAll(
            Gen.OneOf(unrelated, extended).ToArbitrary(),
            testCase =>
            {
                // Act
                bool matched = Wildcard.Match(testCase.Prefix + "*", testCase.Text);

                // Assert
                matched.ShouldBe(
                    testCase.Text.StartsWith(testCase.Prefix, StringComparison.Ordinal),
                    $"`{testCase.Prefix}*` must match \"{testCase.Text}\" exactly when that text starts with "
                    + $"\"{testCase.Prefix}\". Matching too widely applies a rule to code it was never written "
                    + "for; matching too narrowly leaves the rule enforced over nothing, and says passed.");
            });
    }

    /// <summary>
    ///     <c>*</c> by its definition rather than by an algorithm: at each step it either matches nothing here and
    ///     the rest of the pattern must carry the text, or it swallows one character and is tried again. Correct
    ///     by inspection, which is the only property an oracle has to have.
    /// </summary>
    /// <remarks>
    ///     The table is for speed and nothing else — it answers what the bare recursion answers. Without it the
    ///     definition is exponential, and the generated patterns are its worst case rather than its best: a
    ///     quarter of the alphabet is <c>*</c>, so shapes like <c>*a*b*a*</c> are ordinary, and against text that
    ///     does not match one the recursion explores the whole tree before saying so. Measured at 1000 cases:
    ///     roughly 40 s bare, under a second memoized.
    /// </remarks>
    private static bool MatchesByDefinition(string pattern, string text)
    {
        var answered = new bool?[pattern.Length + 1, text.Length + 1];

        return Matches(0, 0);

        bool Matches(int patternIndex, int textIndex)
        {
            if (answered[patternIndex, textIndex] is { } remembered) return remembered;

            bool matched;
            if (patternIndex == pattern.Length)
                matched = textIndex == text.Length;
            else if (pattern[patternIndex] == '*')
                matched = Matches(patternIndex + 1, textIndex)
                          || (textIndex < text.Length && Matches(patternIndex, textIndex + 1));
            else
                matched = textIndex < text.Length
                          && pattern[patternIndex] == text[textIndex]
                          && Matches(patternIndex + 1, textIndex + 1);

            answered[patternIndex, textIndex] = matched;

            return matched;
        }
    }

    /// <summary>A string of <paramref name="alphabet" />'s characters, between the two lengths inclusive.</summary>
    private static Gen<string> StringOf(string alphabet, int minLength, int maxLength)
    {
        return Gen.Choose(minLength, maxLength)
            .SelectMany(length => Gen.Elements(alphabet.ToCharArray()).ListOf(length))
            .Select(characters => new string(characters.ToArray()));
    }
}
