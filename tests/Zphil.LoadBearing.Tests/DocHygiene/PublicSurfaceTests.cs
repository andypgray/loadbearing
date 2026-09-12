using Shouldly;
using Xunit;

namespace Zphil.LoadBearing.Tests.DocHygiene;

/// <summary>
///     The rendering and diffing rules behind the surface pins, exercised over fixture types declared
///     here rather than over a shipped assembly — so an expected file can be stated in full, and so the
///     rules stay pinned when the real surface moves for reasons of its own.
/// </summary>
/// <remarks>
///     The fixtures are private nested types: the renderer is handed a type set rather than asked to
///     sweep an assembly, so nothing here needs to be exported, and the test assembly's own surface
///     stays free of them. Nesting is not incidental either — it gives the nested-name case a real
///     <c>+</c>-separated name to spell.
/// </remarks>
public sealed class PublicSurfaceTests
{
    private const string Prefix = "Zphil.LoadBearing.Tests.DocHygiene.PublicSurfaceTests+";

    [Fact]
    public void Render_ListsTypesInOrdinalOrderAndEndsOnOneNewline()
    {
        // Arrange: handed in an order that is neither the ordinal one nor the reverse of it.
        Type[] types = [typeof(Outer.Inner), typeof(Generic<>), typeof(Outer)];

        // Act
        string rendered = PublicSurface.Render(types);

        // Assert
        rendered.ShouldBe($"{Prefix}Generic`1\n{Prefix}Outer\n{Prefix}Outer+Inner\n");
    }

    [Fact]
    public void Render_NamesTypesByFullNameRatherThanByToStringOrAssemblyQualifiedName()
    {
        // Arrange: an open generic is where the three spellings differ most — ToString appends the type
        // parameter list, AssemblyQualifiedName appends the assembly identity, and only FullName is the
        // name a consumer would recognise.
        Type[] types = [typeof(Generic<>)];

        // Act
        string rendered = PublicSurface.Render(types);

        // Assert
        rendered.ShouldBe($"{Prefix}Generic`1\n");
        rendered.ShouldNotContain("[T]");
        rendered.ShouldNotContain("Version=");
    }

    [Fact]
    public void Render_PutsEachEnumsConstantsUnderItInValueOrder()
    {
        // Arrange
        Type[] types = [typeof(Shuffled)];

        // Act
        string rendered = PublicSurface.Render(types);

        // Assert: declaration order, which is value order — not the alphabetical order of the names.
        rendered.ShouldBe($"{Prefix}Shuffled\n    Zulu = 0\n    Alpha = 1\n    Mike = 2\n");
    }

    [Fact]
    public void Render_RecordsTheValueOfEveryConstantSoAnInsertionCannotHide()
    {
        // Arrange: the failure the enum half of the pin exists for. A member inserted mid-list is one
        // added line to a name-only pin and a renumbering of everything after it in the compiled IL.
        string before = PublicSurface.Render([typeof(Shuffled)]);
        string after = PublicSurface.Render([typeof(Inserted)]);

        // Act: compare the two bodies, which differ only by the member inserted between Zulu and Alpha.
        string[] movedConstants = PublicSurface.Drift(before, after)
            .Where(static finding => finding.Contains(" = ", StringComparison.Ordinal))
            .ToArray();

        // Assert: the insertion and both of the members it pushed along, not the insertion alone.
        movedConstants.Length.ShouldBeGreaterThan(1);
        movedConstants.ShouldContain(finding => finding.EndsWith("Alpha = 2", StringComparison.Ordinal));
        movedConstants.ShouldContain(finding => finding.EndsWith("Mike = 3", StringComparison.Ordinal));
    }

    [Fact]
    public void Constants_BreakAValueTieByName()
    {
        // Arrange & Act: an alias shares its value with the member it aliases, so value order alone
        // leaves the pair unordered and the file would differ between runs.
        IReadOnlyList<(string Name, string Value)> constants = PublicSurface.Constants(typeof(Aliased));

        // Assert
        constants.Select(static constant => $"{constant.Name} = {constant.Value}")
            .ShouldBe(["Alias = 10", "Low = 10", "High = 30"]);
    }

    [Fact]
    public void Drift_NamesWhatWasAddedAndWhatWasRemoved()
    {
        // Arrange
        string pinned = PublicSurface.Render([typeof(Outer), typeof(Generic<>)]);
        string rendered = PublicSurface.Render([typeof(Outer), typeof(Outer.Inner)]);

        // Act
        IReadOnlyList<string> drift = PublicSurface.Drift(pinned, rendered);

        // Assert
        drift.ShouldBe([$"+ {Prefix}Outer+Inner", $"- {Prefix}Generic`1"]);
    }

    [Fact]
    public void Drift_QualifiesAConstantWithTheEnumItSitsUnder()
    {
        // Arrange: an indented line reads "Mike = 2" on its own, which names nothing in an assembly with
        // a dozen enums — so a finding has to carry the type the constant belongs to.
        string pinned = PublicSurface.Render([typeof(Shuffled)]);
        string rendered = PublicSurface.Render([typeof(Aliased)]);

        // Act
        IReadOnlyList<string> drift = PublicSurface.Drift(pinned, rendered);

        // Assert
        drift.ShouldContain($"- {Prefix}Shuffled: Mike = 2");
        drift.ShouldContain($"+ {Prefix}Aliased: Alias = 10");
    }

    [Fact]
    public void Drift_ReportsAReorderingThatMovesNoLines()
    {
        // Arrange: two constants sharing a value render two lines that differ only in position, so
        // swapping them is the one edit set arithmetic cannot see — and it is exactly what a regressed
        // tie-break would produce, alternating between runs while every line stayed the same.
        string pinned = PublicSurface.Render([typeof(Aliased)]);
        string reordered = pinned.Replace(
            "    Alias = 10\n    Low = 10\n",
            "    Low = 10\n    Alias = 10\n",
            StringComparison.Ordinal);

        // Act
        IReadOnlyList<string> drift = PublicSurface.Drift(pinned, reordered);

        // Assert
        drift.ShouldHaveSingleItem()
            .ShouldContain("different order");
    }

    [Fact]
    public void Drift_LeavesLineEndingsToTheGateThatOwnsThem()
    {
        // Arrange: a checkout that rewrote the pin to CRLF must not report every line as both added and
        // removed — that reads like the surface changed, which is the one thing a reader must not be
        // told wrongly. A separate arm holds the pins to LF and says so in its own words.
        string pinned = PublicSurface.Render([typeof(Shuffled)]);
        string asCrlf = pinned.Replace("\n", "\r\n", StringComparison.Ordinal);

        // Act
        IReadOnlyList<string> drift = PublicSurface.Drift(asCrlf, pinned);

        // Assert
        drift.ShouldBeEmpty();
    }

    [Fact]
    public void Drift_IsEmptyForAnUnchangedSurface()
    {
        // Arrange
        string pinned = PublicSurface.Render([typeof(Shuffled), typeof(Outer)]);

        // Act
        IReadOnlyList<string> drift = PublicSurface.Drift(pinned, pinned);

        // Assert
        drift.ShouldBeEmpty();
    }

    [Fact]
    public void PinPath_NamesTheFileBesideThisSuiteAfterTheAssembly()
    {
        // Act
        string path = PublicSurface.PinPath(typeof(Arch).Assembly);

        // Assert
        path.ShouldBe("tests/Zphil.LoadBearing.Tests/DocHygiene/PublicSurface.Zphil.LoadBearing.txt");
    }

    // Every fixture below exists to be reflected over rather than called, so no code names its
    // members and the unused-member analysis is right about the letter and wrong about the point.
    // ReSharper disable UnusedMember.Local
    /// <summary>A nested type, so the renderer has a <c>+</c>-separated name to spell.</summary>
    private sealed class Outer
    {
        /// <summary>The inner half of that name; as visible as the private type holding it.</summary>
        public sealed class Inner;
    }

    /// <summary>An open generic, whose three reflected spellings differ.</summary>
    /// <typeparam name="T">Unused; the arity is the point.</typeparam>
    private sealed class Generic<T>;

    /// <summary>Constants whose declaration order is not their alphabetical order.</summary>
    private enum Shuffled
    {
        /// <summary>Zero.</summary>
        Zulu,

        /// <summary>One.</summary>
        Alpha,

        /// <summary>Two.</summary>
        Mike
    }

    /// <summary><see cref="Shuffled" /> with a member inserted after its first.</summary>
    private enum Inserted
    {
        /// <summary>Zero.</summary>
        Zulu,

        /// <summary>One, and the reason the two below move.</summary>
        Bravo,

        /// <summary>Two, formerly one.</summary>
        Alpha,

        /// <summary>Three, formerly two.</summary>
        Mike
    }

    /// <summary>Explicit values, with two names sharing one of them.</summary>
    private enum Aliased
    {
        /// <summary>Ten.</summary>
        Low = 10,

        /// <summary>Ten as well, which is what forces the tie-break.</summary>
        Alias = 10,

        /// <summary>Thirty, after a gap.</summary>
        High = 30
    }
}
