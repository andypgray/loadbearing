using Shouldly;
using Xunit;
using Zphil.LoadBearing.Codebase;

namespace Zphil.LoadBearing.Tests.Extraction;

/// <summary>
///     Extraction is total over symbols the compiler never resolved. A workspace that loads only partially
///     — a project whose references are missing, a package that was never restored — hands Roslyn error
///     symbols to the external-mint path through base types, interfaces, and attribute classes, and every
///     fact minted from one has to have an answer. Two of the three already did (TypeKind falls back to
///     Class, the baseline key to an <c>unresolved:</c> form); accessibility did not, so a survey over an
///     unbuilt tree died with an invariant violation naming a symbol nobody wrote.
/// </summary>
/// <remarks>
///     The negative control lives in <see cref="Roslyn.AccessibilityMapperTests" />: it pins that these
///     symbols really do report <c>TypeKind.Error</c> and <c>Accessibility.NotApplicable</c>, so a Roslyn
///     change that started resolving them would surface there rather than making these tests pass vacuously.
///     Compiled against the core library only, so <c>Ghost.*</c> is unresolvable by construction — no
///     MSBuild, no workspace, no timing.
/// </remarks>
public sealed class UnresolvedSymbolExtractionTests
{
    [Fact]
    public void Extract_TypeDerivingFromUnresolvedBase_MintsTheExternalInsteadOfThrowing()
    {
        // The F-12 path: PopulateHierarchy resolves the base type, which is an error symbol.
        CodebaseModel model = CompilationFactory.Extract("namespace App;\npublic class Widget : Ghost.Base { }\n");

        TypeNode external = model.Types.Single(t => t.FullName == "Ghost.Base");

        // Public is the deliberate fallback: an unresolved external is never a rule subject, its accessibility
        // is informational, and a type reached across an assembly boundary is visibly-public surface.
        external.Accessibility.ShouldBe(Accessibility.Public);
        external.IsExternal.ShouldBeTrue();
        model.Types.Single(t => t.FullName == "App.Widget")
            .BaseType!.FullName()
            .ShouldBe("Ghost.Base");
    }

    [Fact]
    public void Extract_TypeImplementingUnresolvedInterface_MintsTheExternalInsteadOfThrowing()
    {
        // The likeliest field trigger: an interface from a package whose assembly never resolved. A declared
        // interface takes the first base-list slot so the unresolved name can only be an interface — an
        // unresolved name alone in that list is ambiguous, and Roslyn reads it as the base type (which is the
        // case above).
        CodebaseModel model = CompilationFactory.Extract(
            "namespace App;\npublic interface IReal { }\npublic class Cache : IReal, Ghost.ICache { }\n");

        model.Types.Single(t => t.FullName == "Ghost.ICache")
            .Accessibility.ShouldBe(Accessibility.Public);
        model.Types.Single(t => t.FullName == "App.Cache")
            .Interfaces.Select(i => i.FullName())
            .ShouldContain("Ghost.ICache");
    }

    [Fact]
    public void Extract_TypeCarryingUnresolvedAttribute_MintsTheExternalInsteadOfThrowing()
    {
        // The third ungated feeder: an attribute class that did not resolve. The error symbol's own name
        // depends on which of the two candidate spellings Roslyn kept, so the assertion keys on the prefix.
        CodebaseModel model = CompilationFactory.Extract("namespace App;\n[Ghost.Marker]\npublic class Tagged { }\n");

        TypeNode external = model.Types.Single(t => t.FullName.StartsWith("Ghost.Marker", StringComparison.Ordinal));
        external.Accessibility.ShouldBe(Accessibility.Public);
        external.IsExternal.ShouldBeTrue();
    }

    [Fact]
    public void Extract_UnresolvedExternal_KeepsAStableBaselineKeyAndTheClassFallback()
    {
        // The two facts that were already tolerant, pinned beside the third so the trio stays symmetric.
        // TypeKind.Error has no Core mapping, so the node falls back to Class. The baseline key needs no
        // fallback at all here: Roslyn mints an error symbol its own "!:" documentation-comment ID, which is
        // stable and deterministic — the extractor's unresolved:{fqn} form is for symbols with no ID at all.
        CodebaseModel model = CompilationFactory.Extract("namespace App;\npublic class Widget : Ghost.Base { }\n");

        TypeNode external = model.Types.Single(t => t.FullName == "Ghost.Base");
        external.SymbolId.ShouldBe("!:Ghost.Base");
        external.Kind.ShouldBe(TypeKind.Class);
    }
}
