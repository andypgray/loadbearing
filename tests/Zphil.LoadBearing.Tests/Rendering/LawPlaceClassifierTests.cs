using Shouldly;
using Xunit;
using Zphil.LoadBearing.Hosting;
using Zphil.LoadBearing.Rendering;
using Zphil.LoadBearing.Tests.Checking;

namespace Zphil.LoadBearing.Tests.Rendering;

/// <summary>
///     <see cref="LawPlaceClassifier" /> facts, one per arm of the triage: the drawable verbs, the
///     place-shaped nouns, the three selections that are not places, the position asymmetry a single type
///     has, the adjective rule, the identity collapses a single-glob and a project-defined layer earn, and
///     what a layer's definition says about where it is drawn. Models are built from inline specs and read
///     back through the reified nodes — no workspace, no extraction, because the law is a property of the
///     spec alone.
/// </summary>
public sealed class LawPlaceClassifierTests
{
    [Fact]
    public void IsDrawableVerb_TheLeafForm_IsDrawableSoItsSubjectStaysAPlace()
    {
        // Arrange — the verb draws nothing, having no operand to point at, and is drawable all the same:
        // declining it here would drop the subject's place with it, and a leaf is a node of the graph
        // whether or not an arrow leaves it.
        ArchitectureModel model = Checker.Model(arch =>
            arch.Rule("r/leaf")
                .Enforce(arch.Namespace("A.*").MustOnlyReferenceItself())
                .Because("x"));

        // Act + Assert
        LawPlaceClassifier.IsDrawableVerb(model.Rules.Single().Constraint)
            .ShouldBeTrue();
    }

    [Fact]
    public void IsDrawableVerb_TheInboundLeafForm_IsDrawableForTheSameReasonTheOutboundOneIs()
    {
        // Arrange — a hermetic set is a node of the graph whether or not an arrow reaches it, so the
        // inbound leaf classifies like its outbound twin: it registers its subject's place and draws
        // nothing.
        ArchitectureModel model = Checker.Model(arch =>
            arch.Rule("r/hermetic")
                .Enforce(arch.Namespace("A.*").MustOnlyBeReferencedByItself())
                .Because("x"));

        // Act
        LawPlaceClassifier.DrawableVerb verb = LawPlaceClassifier.Classify(model.Rules.Single().Constraint)
            .ShouldNotBeNull();

        // Assert
        verb.Inbound.ShouldBeTrue();
        verb.Only.ShouldBeTrue();
    }

    [Fact]
    public void IsDrawableVerb_TheCrossCellBan_IsNotDrawable()
    {
        // Arrange — the far end is a different place for every cell, so one arrow could not say the law.
        ArchitectureModel model = Checker.Model(arch =>
            arch.Rule("r/leaves")
                .Enforce(arch.Each(arch.Layer("A", "A.*"), arch.Layer("B", "B.*")).MustNotReferenceEachOther())
                .Because("x"));

        // Act + Assert
        LawPlaceClassifier.IsDrawableVerb(model.Rules.Single().Constraint)
            .ShouldBeFalse();
    }

    [Fact]
    public void IsDrawableVerb_TheCircularReferencesVerb_IsNotDrawable()
    {
        // Arrange — the law forbids a property of a path, not an edge, so no arrow can say "no circle".
        ArchitectureModel model = Checker.Model(arch =>
            arch.Rule("r/no-circles")
                .Enforce(arch.Each(arch.Layer("A", "A.*"), arch.Layer("B", "B.*")).MustNotHaveCircularReferences())
                .Because("x"));

        // Act + Assert
        LawPlaceClassifier.IsDrawableVerb(model.Rules.Single().Constraint)
            .ShouldBeFalse();
    }

    [Fact]
    public void SubjectPlace_AFamily_IsNotAPlace()
    {
        // Arrange — a family is several places at once, so it is none: its rules join the compact list
        // under the fence, the totality rule that keeps a law visible when the drawing cannot hold it.
        ArchitectureModel model = Checker.Model(arch =>
            arch.Rule("r/leaves")
                .Enforce(arch.Each(arch.Layer("A", "A.*"), arch.Layer("B", "B.*"))
                    .MustNotReference(arch.Namespace("C.*")))
                .Because("x"));

        // Act + Assert
        LawPlaceClassifier.SubjectPlace(model.Rules.Single().Constraint!.Subject, model.Layers)
            .ShouldBeNull();
    }

    [Fact]
    public void IsDrawableVerb_TheFourDirectionVerbsAndExpose_AreDrawable()
    {
        // Arrange — one rule per drawable verb, in one model.
        ArchitectureModel model = Checker.Model(arch =>
        {
            arch.Rule("r/not-reference")
                .Enforce(arch.Namespace("A.*").MustNotReference(arch.Namespace("B.*")))
                .Because("x");
            arch.Rule("r/not-referenced-by")
                .Enforce(arch.Namespace("A.*").MustNotBeReferencedBy(arch.Namespace("B.*")))
                .Because("x");
            arch.Rule("r/only-reference")
                .Enforce(arch.Namespace("A.*").MustOnlyReference(arch.Namespace("B.*")))
                .Because("x");
            arch.Rule("r/only-referenced-by")
                .Enforce(arch.Namespace("A.*").MustOnlyBeReferencedBy(arch.Namespace("B.*")))
                .Because("x");
            arch.Rule("r/not-expose")
                .Enforce(arch.Namespace("A.*").MustNotExpose(arch.Namespace("B.*")))
                .Because("x");
        });

        // Act + Assert
        model.Rules.ShouldAllBe(rule => LawPlaceClassifier.IsDrawableVerb(rule.Constraint));
    }

    [Fact]
    public void IsDrawableVerb_AShapeOrNamingVerb_IsNotDrawable()
    {
        // Arrange — a verb that constrains what a type IS rather than what it may reach has no direction,
        // and an arrow would misrepresent it.
        ArchitectureModel model = Checker.Model(arch =>
        {
            arch.Rule("r/prefix")
                .Enforce(arch.Namespace("A.*").MustHavePrefix("I"))
                .Because("x");
            arch.Rule("r/sealed")
                .Enforce(arch.Namespace("A.*").MustBeSealed())
                .Because("x");
            arch.Rule("r/construct")
                .Enforce(arch.Namespace("A.*").MustNotConstruct(arch.Namespace("B.*")))
                .Because("x");
        });

        // Act + Assert
        model.Rules.ShouldAllBe(rule => !LawPlaceClassifier.IsDrawableVerb(rule.Constraint));
    }

    [Fact]
    public void SubjectPlace_ALayer_IsAPlaceUnderTheLayerName()
    {
        // Arrange
        ArchitectureModel model = Checker.Model(arch =>
        {
            Layer domain = arch.Layer("Domain", "MyApp.Domain.*", "MyApp.Shared.*");
            arch.Rule("r/one")
                .Enforce(domain.MustNotReference(arch.Namespace("B.*")))
                .Because("x");
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
        ArchitectureModel model = Checker.Model(arch =>
        {
            Layer web = arch.Layer("Web", "MyApp.Web.*");
            arch.Rule("r/one")
                .Enforce(web.MustNotReference(arch.Namespace("B.*")))
                .Because("x");
            arch.Rule("r/two")
                .Enforce(arch.Namespace("MyApp.Web.*").MustNotReference(arch.Namespace("C.*")))
                .Because("x");
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
        ArchitectureModel model = Checker.Model(arch =>
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
        ArchitectureModel model = Checker.Model(arch =>
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
        ArchitectureModel model = Checker.Model(arch =>
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
        ArchitectureModel model = Checker.Model(arch =>
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
        ArchitectureModel model = Checker.Model(arch =>
        {
            arch.Rule("r/bare")
                .Enforce(arch.Types.MustNotReference(arch.Namespace("B.*")))
                .Because("x");
            arch.Rule("r/two")
                .Enforce(arch.Types.InNamespace("A.*").InNamespace("A.B.*").MustNotReference(arch.Namespace("B.*")))
                .Because("x");
        });

        // Act + Assert
        LawPlaceClassifier.SubjectPlace(Subject(model), model.Layers)
            .ShouldBeNull();
        LawPlaceClassifier.SubjectPlace(Subject(model, 1), model.Layers)
            .ShouldBeNull();
    }

    [Fact]
    public void SubjectPlace_AUnion_IsNoPlaceAndNeverReadsTheNoun()
    {
        // Arrange — UnionSelection.Noun throws by design; the guard runs before any question about the
        // noun, so this returning null rather than throwing IS the pin.
        ArchitectureModel model = Checker.Model(arch =>
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
        ArchitectureModel model = Checker.Model(arch =>
            arch.Rule("r/one")
                .Enforce(arch.Registered(Lifetime.Singleton).MustNotReference(arch.Namespace("B.*")))
                .Because("x"));

        // Act + Assert
        LawPlaceClassifier.SubjectPlace(Subject(model), model.Layers)
            .ShouldBeNull();
    }

    [Fact]
    public void SubjectPlace_AProjectDefinedLayerAndItsProject_CollapseToOnePlace()
    {
        // Arrange — one rule names the layer, the next names the project that defines it.
        ArchitectureModel model = Checker.Model(arch =>
        {
            Layer core = arch.Layer("Core", arch.Project("MyApp.Core"));
            arch.Rule("r/one")
                .Enforce(core.MustNotReference(arch.Namespace("B.*")))
                .Because("x");
            arch.Rule("r/two")
                .Enforce(arch.Project("MyApp.Core").MustNotReference(arch.Namespace("C.*")))
                .Because("x");
        });

        // Act
        LawPlace? viaLayer = LawPlaceClassifier.SubjectPlace(Subject(model), model.Layers);
        LawPlace? viaProject = LawPlaceClassifier.SubjectPlace(Subject(model, 1), model.Layers);

        // Assert — the glob collapse's project twin: same key, so they dedupe to one node, and the layer's
        // name is what it is called.
        viaLayer.ShouldNotBeNull();
        viaProject.ShouldNotBeNull();
        viaProject.Key.ShouldBe(viaLayer.Key);
        viaProject.Key.ShouldBe("project:MyApp.Core");
        viaProject.Label.ShouldBe("Core");
        viaProject.IsDeclaredLayer.ShouldBeTrue();
        viaProject.Globs.ShouldBeEmpty();
    }

    [Fact]
    public void SubjectPlace_ARefinementDefinedLayer_IsItsOwnPlaceInsideTheLayerItRefines()
    {
        // Arrange — the inner layer's definition says where it sits, which is the only thing that can say
        // so: nesting elsewhere is glob implication, and a project place has no globs to imply anything.
        ArchitectureModel model = Checker.Model(arch =>
        {
            Layer core = arch.Layer("Core", arch.Project("MyApp.Core"));
            Layer model2 = arch.Layer("Model", core.InNamespace("MyApp.Core.Model.*"));
            arch.Rule("r/one")
                .Enforce(model2.MustNotReference(arch.Namespace("B.*")))
                .Because("x");
        });

        // Act
        LawPlace? place = LawPlaceClassifier.SubjectPlace(Subject(model), model.Layers);

        // Assert
        place.ShouldNotBeNull();
        place.Key.ShouldBe("layer:Model");
        place.Label.ShouldBe("Model");
        place.Parent.ShouldNotBeNull()
            .Key.ShouldBe("project:MyApp.Core");
    }

    [Fact]
    public void SubjectPlace_AUnionDefinedLayer_ParentsEachOperandsPlace()
    {
        // Arrange — a union of places holds no region of its own; the box is what its operands sit in.
        ArchitectureModel model = Checker.Model(arch =>
        {
            Layer shipping = arch.Layer("Shipping", arch.AnyOf(arch.Project("MyApp.Core"), arch.Namespace("MyApp.Web.*")));
            arch.Rule("r/one")
                .Enforce(shipping.MustNotReference(arch.Namespace("B.*")))
                .Because("x");
        });

        // Act
        LawPlace? place = LawPlaceClassifier.SubjectPlace(Subject(model), model.Layers);

        // Assert
        place.ShouldNotBeNull();
        place.Key.ShouldBe("layer:Shipping");
        place.StructuralChildren.Select(child => child.Key)
            .ShouldBe(["project:MyApp.Core", "MyApp.Web.*"]);
    }

    [Fact]
    public void SubjectPlace_ALayerDefinedAsSomethingUnplaceable_IsNoPlace()
    {
        // Arrange — a registration is a lifetime rather than a location, and a union carrying an operand
        // the classifier declines is a box missing part of itself. Neither is a place, so the rules
        // anchored on them join the compact list under the fence.
        ArchitectureModel model = Checker.Model(arch =>
        {
            arch.Rule("r/registered")
                .Enforce(arch.Layer("Wiring", arch.Registered(Lifetime.Singleton)).MustNotReference(arch.Namespace("B.*")))
                .Because("x");
            arch.Rule("r/partial")
                .Enforce(arch.Layer("Mixed", arch.AnyOf(arch.Project("MyApp.Core"), arch.Types))
                    .MustNotReference(arch.Namespace("B.*")))
                .Because("x");
        });

        // Act + Assert
        LawPlaceClassifier.SubjectPlace(Subject(model), model.Layers)
            .ShouldBeNull();
        LawPlaceClassifier.SubjectPlace(Subject(model, 1), model.Layers)
            .ShouldBeNull();
    }

    private static Selection Subject(ArchitectureModel model, int rule = 0)
    {
        return model.Rules[rule].Constraint!.Subject!;
    }

    private static Selection Operand(ArchitectureModel model, int rule = 0, int operand = 0)
    {
        return model.Rules[rule].Constraint!.Operands[operand];
    }
}
