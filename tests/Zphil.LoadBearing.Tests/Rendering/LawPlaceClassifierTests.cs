using Shouldly;
using Xunit;
using Zphil.LoadBearing.Rendering;
using Zphil.LoadBearing.Tests.Checking;

namespace Zphil.LoadBearing.Tests.Rendering;

/// <summary>
///     <see cref="LawPlaceClassifier" /> facts, one per arm of the triage: the drawable verbs, the
///     place-shaped nouns, the three selections that are not places, the position asymmetry a single type
///     has, the adjective rule, and the identity collapse a single-glob layer earns. Models are built
///     from inline specs and read back through the reified nodes — no workspace, no extraction, because
///     the law is a property of the spec alone.
/// </summary>
public sealed class LawPlaceClassifierTests
{
    [Fact]
    public void IsDrawableVerb_TheFourDirectionVerbsAndExpose_AreDrawable()
    {
        // Arrange — one rule per drawable verb, in one model.
        ArchitectureModel model = Build(arch =>
        {
            arch.Rule("r/not-reference")
                .Enforce(arch.Namespace("A.*").MustNotReference(arch.Namespace("B.*"))).Because("x");
            arch.Rule("r/not-referenced-by")
                .Enforce(arch.Namespace("A.*").MustNotBeReferencedBy(arch.Namespace("B.*"))).Because("x");
            arch.Rule("r/only-reference")
                .Enforce(arch.Namespace("A.*").MustOnlyReference(arch.Namespace("B.*"))).Because("x");
            arch.Rule("r/only-referenced-by")
                .Enforce(arch.Namespace("A.*").MustOnlyBeReferencedBy(arch.Namespace("B.*"))).Because("x");
            arch.Rule("r/not-expose")
                .Enforce(arch.Namespace("A.*").MustNotExpose(arch.Namespace("B.*"))).Because("x");
        });

        // Act + Assert
        model.Rules.ShouldAllBe(rule => LawPlaceClassifier.IsDrawableVerb(rule.Constraint));
    }

    [Fact]
    public void IsDrawableVerb_AShapeOrNamingVerb_IsNotDrawable()
    {
        // Arrange — a verb that constrains what a type IS rather than what it may reach has no direction,
        // and an arrow would misrepresent it.
        ArchitectureModel model = Build(arch =>
        {
            arch.Rule("r/prefix").Enforce(arch.Namespace("A.*").MustHavePrefix("I")).Because("x");
            arch.Rule("r/sealed").Enforce(arch.Namespace("A.*").MustBeSealed()).Because("x");
            arch.Rule("r/construct")
                .Enforce(arch.Namespace("A.*").MustNotConstruct(arch.Namespace("B.*"))).Because("x");
        });

        // Act + Assert
        model.Rules.ShouldAllBe(rule => !LawPlaceClassifier.IsDrawableVerb(rule.Constraint));
    }

    [Fact]
    public void SubjectPlace_ALayer_IsAPlaceUnderTheLayerName()
    {
        // Arrange
        ArchitectureModel model = Build(arch =>
        {
            Layer domain = arch.Layer("Domain", "MyApp.Domain.*", "MyApp.Shared.*");
            arch.Rule("r/one").Enforce(domain.MustNotReference(arch.Namespace("B.*"))).Because("x");
        });

        // Act
        LawPlace? place = LawPlaceClassifier.SubjectPlace(Subject(model), model.Layers);

        // Assert — a multi-glob layer is a set no single glob equals, so it keeps its own key and carries
        // both globs into the containment comparison.
        place.ShouldNotBeNull();
        place.Label.ShouldBe("Domain");
        place.Key.ShouldBe("layer:Domain");
        place.IsDeclaredLayer.ShouldBeTrue();
        place.Globs.ShouldBe(["MyApp.Domain.*", "MyApp.Shared.*"]);
    }

    [Fact]
    public void SubjectPlace_ASingleGlobLayerAndItsGlob_CollapseToOnePlace()
    {
        // Arrange — one rule names the layer, the next names the glob that defines it.
        ArchitectureModel model = Build(arch =>
        {
            Layer web = arch.Layer("Web", "MyApp.Web.*");
            arch.Rule("r/one").Enforce(web.MustNotReference(arch.Namespace("B.*"))).Because("x");
            arch.Rule("r/two")
                .Enforce(arch.Namespace("MyApp.Web.*").MustNotReference(arch.Namespace("C.*"))).Because("x");
        });

        // Act
        LawPlace? viaLayer = LawPlaceClassifier.SubjectPlace(Subject(model), model.Layers);
        LawPlace? viaGlob = LawPlaceClassifier.SubjectPlace(Subject(model, 1), model.Layers);

        // Assert — same key, so they dedupe to one node, and the layer's name is what it is called.
        viaLayer.ShouldNotBeNull();
        viaGlob.ShouldNotBeNull();
        viaGlob.Key.ShouldBe(viaLayer.Key);
        viaGlob.Key.ShouldBe("MyApp.Web.*");
        viaGlob.Label.ShouldBe("Web");
        viaGlob.IsDeclaredLayer.ShouldBeTrue();
    }

    [Fact]
    public void SubjectPlace_ANamespaceOutsideEveryLayer_IsAPlaceUnderItsGlob()
    {
        // Arrange
        ArchitectureModel model = Build(arch =>
            arch.Rule("r/one")
                .Enforce(arch.Namespace("Microsoft.Build.*").MustNotReference(arch.Namespace("B.*")))
                .Because("x"));

        // Act
        LawPlace? place = LawPlaceClassifier.SubjectPlace(Subject(model), model.Layers);

        // Assert — the subtree operator is noise in an identifier, so the ID slugs from the prefix.
        place.ShouldNotBeNull();
        place.Label.ShouldBe("Microsoft.Build.*");
        place.IdSource.ShouldBe("Microsoft.Build");
        place.IsDeclaredLayer.ShouldBeFalse();
    }

    [Fact]
    public void SubjectPlace_AProject_IsAPlaceWithNoGlobs()
    {
        // Arrange
        ArchitectureModel model = Build(arch =>
            arch.Rule("r/one")
                .Enforce(arch.Project("MyApp.Web").MustNotReference(arch.Namespace("B.*")))
                .Because("x"));

        // Act
        LawPlace? place = LawPlaceClassifier.SubjectPlace(Subject(model), model.Layers);

        // Assert — a project is not a namespace region, so it never nests and never parents.
        place.ShouldNotBeNull();
        place.Label.ShouldBe("MyApp.Web");
        place.Globs.ShouldBeEmpty();
    }

    [Fact]
    public void Place_ASingleType_IsAnOperandPlaceAndNotASubjectPlace()
    {
        // Arrange
        ArchitectureModel model = Build(arch =>
            arch.Rule("r/one")
                .Enforce(arch.Namespace("A.*").MustNotReference(typeof(Environment)))
                .Because("x"));

        // Act
        LawPlace? asOperand = LawPlaceClassifier.OperandPlace(Operand(model), model.Layers);
        LawPlace? asSubject = LawPlaceClassifier.SubjectPlace(Operand(model), model.Layers);

        // Assert — a target type is exactly what the arrow points at, and it carries its full name because
        // the fence has no sentence around it to say which `Environment` this is. A rule anchored ON one
        // type is a statement about that type's obligations, and is not a place the picture is built from.
        asOperand.ShouldNotBeNull();
        asOperand.Label.ShouldBe("System.Environment");
        asSubject.ShouldBeNull();
    }

    [Fact]
    public void SubjectPlace_TypesWithExactlyOneInNamespace_IsThatRegion()
    {
        // Arrange — the other adjectives narrow which types inside the region are governed, which is not a
        // question of where, so they never move the node.
        ArchitectureModel model = Build(arch =>
            arch.Rule("r/one")
                .Enforce(arch.Types.InNamespace("MyApp.Web.*").OfKind(TypeKind.Interface)
                    .Except(arch.Types.WithNameMatching("Legacy*"))
                    .MustNotReference(arch.Namespace("B.*")))
                .Because("x"));

        // Act
        LawPlace? place = LawPlaceClassifier.SubjectPlace(Subject(model), model.Layers);

        // Assert
        place.ShouldNotBeNull();
        place.Key.ShouldBe("MyApp.Web.*");
    }

    [Fact]
    public void SubjectPlace_BareTypesOrTwoRegions_IsNoPlace()
    {
        // Arrange — bare `arch.Types` is the whole solution, and two InNamespace adjectives are an
        // intersection of regions whose honest node is none.
        ArchitectureModel model = Build(arch =>
        {
            arch.Rule("r/bare").Enforce(arch.Types.MustNotReference(arch.Namespace("B.*"))).Because("x");
            arch.Rule("r/two")
                .Enforce(arch.Types.InNamespace("A.*").InNamespace("A.B.*").MustNotReference(arch.Namespace("B.*")))
                .Because("x");
        });

        // Act + Assert
        LawPlaceClassifier.SubjectPlace(Subject(model), model.Layers).ShouldBeNull();
        LawPlaceClassifier.SubjectPlace(Subject(model, 1), model.Layers).ShouldBeNull();
    }

    [Fact]
    public void SubjectPlace_AUnion_IsNoPlaceAndNeverReadsTheNoun()
    {
        // Arrange — UnionSelection.Noun throws by design; the guard runs before any question about the
        // noun, so this returning null rather than throwing IS the pin.
        ArchitectureModel model = Build(arch =>
            arch.Rule("r/one")
                .Enforce(arch.AnyOf(arch.Namespace("A.*"), arch.Namespace("B.*"))
                    .MustNotReference(arch.Namespace("C.*")))
                .Because("x"));

        // Act
        LawPlace? place = LawPlaceClassifier.SubjectPlace(Subject(model), model.Layers);

        // Assert
        place.ShouldBeNull();
    }

    [Fact]
    public void SubjectPlace_ARegistration_IsNoPlace()
    {
        // Arrange — a registration is a lifetime rather than a location.
        ArchitectureModel model = Build(arch =>
            arch.Rule("r/one")
                .Enforce(arch.Registered(Lifetime.Singleton).MustNotReference(arch.Namespace("B.*")))
                .Because("x"));

        // Act + Assert
        LawPlaceClassifier.SubjectPlace(Subject(model), model.Layers).ShouldBeNull();
    }

    private static ArchitectureModel Build(Action<Arch> define)
    {
        return ArchModelBuilder.Build(new InlineSpec(define));
    }

    private static Selection Subject(ArchitectureModel model, int rule = 0)
    {
        return model.Rules[rule].Constraint!.Subject;
    }

    private static Selection Operand(ArchitectureModel model, int rule = 0, int operand = 0)
    {
        return model.Rules[rule].Constraint!.Operands[operand];
    }
}