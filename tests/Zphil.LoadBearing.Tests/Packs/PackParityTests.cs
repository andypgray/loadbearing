using Shouldly;
using Xunit;
using Zphil.LoadBearing.Hosting;
using Zphil.LoadBearing.Rendering;
using Zphil.LoadBearing.Tests.Checking;

namespace Zphil.LoadBearing.Tests.Packs;

/// <summary>
///     The claim a rule pack lives or dies on: a pack-declared rule and the hand-written equivalent
///     reify to the same thing. <c>Selection</c> and <c>Constraint</c> are closed hierarchies over a
///     public vocabulary, so a pack has nothing private to build with and no way to produce a node an
///     inline spec could not — this is where that stops being an argument and becomes a measurement.
///     The rendered-block assertion covers every sentence, <c>Because</c>, glossary gate, and section
///     ordering in one string; the per-rule theory then names which field moved when it breaks.
/// </summary>
public class PackParityTests
{
    private static readonly ArchitectureModel Packed = ArchModelBuilder.Build(new PackedNineSpec());
    private static readonly ArchitectureModel Inline = ArchModelBuilder.Build(new InlineNineSpec());

    /// <summary>The nine pack rule IDs, in the order the pack declares them.</summary>
    public static TheoryData<string> PackRuleIds =>
    [
        "http/reuse-httpclient",
        "di/no-service-locator",
        "di/no-buildserviceprovider",
        "async/no-sync-over-async",
        "di/no-captive-dependencies",
        "naming/async-suffix",
        "exceptions/no-general-catch",
        "async/accept-cancellation",
        "persistence/no-mapping-attributes"
    ];

    [Fact]
    public void RootBlock_PackedAndInline_AreByteIdentical()
    {
        string packed = AgentContextRenderer.RootBlock(Packed, "NineSpec");
        string inline = AgentContextRenderer.RootBlock(Inline, "NineSpec");

        packed.ShouldBe(inline);
    }

    [Fact]
    public void RuleOrder_PackedAndInline_Agree()
    {
        Packed.Rules.Select(rule => rule.Id)
            .ShouldBe(Inline.Rules.Select(rule => rule.Id));
    }

    [Theory]
    [MemberData(nameof(PackRuleIds))]
    public void Rule_PackedAndInline_AgreeFieldByField(string id)
    {
        ArchRule packed = Packed.Rule(id);
        ArchRule inline = Inline.Rule(id);

        packed.Posture.ShouldBe(inline.Posture);
        packed.Sentence.ShouldBe(inline.Sentence);
        packed.Because.ShouldBe(inline.Because);
        packed.Fix.ShouldBe(inline.Fix);
        packed.BaselinePath.ShouldBe(inline.BaselinePath);
        packed.Migrate?.From.ShouldBe(inline.Migrate?.From);
    }

    [Theory]
    [MemberData(nameof(PackRuleIds))]
    public void MemberOperands_PackedAndInline_Agree(string id)
    {
        // The pack's typeof + nameof anchors against the inline spec's expression anchors: an expression
        // anchor reduces at mint to the resolved member's declaring type (constructed generics normalized
        // to their definition), so Task<object>.Result and typeof(Task<>) + "Result" are the same leaf.
        // This is the assertion that catches the conversion, since Location is diagnostics-only and does
        // not reach the model.
        Anchors(Packed.Rule(id))
            .ShouldBe(Anchors(Inline.Rule(id)));
    }

    private static IReadOnlyList<string> Anchors(ArchRule rule)
    {
        if (rule.Constraint is not { } constraint) return [];

        return constraint.MemberOperands
            .Select(member => $"{member.DeclaringType.FullName}.{member.Name} (method: {member.IsMethod})")
            .ToList();
    }
}
