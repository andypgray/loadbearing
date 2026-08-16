using Shouldly;
using Xunit;
using Zphil.LoadBearing.Hosting;
using Zphil.LoadBearing.Validation;

namespace Zphil.LoadBearing.Tests.Packs;

/// <summary>
///     How a pack composes with the spec that calls it: many spec classes build against one
///     <c>Arch</c>, rules land in the order the specs ran, and a duplicate ID is reported once — with
///     provenance the pack pays nothing for, since the caller-info the anchor factory already captures
///     points at whichever file authored the ID first.
/// </summary>
public class PackCompositionTests
{
    [Fact]
    public void PackRuleAndLocalRule_ComposeInSpecOrder()
    {
        // One Arch, two spec classes, no coordination between them.
        ArchitectureModel packFirst = ArchModelBuilder.Build(new PackCallSpec(), new LocalDistinctSpec());
        ArchitectureModel localFirst = ArchModelBuilder.Build(new LocalDistinctSpec(), new PackCallSpec());

        packFirst.Rules.Select(rule => rule.Id)
            .ShouldBe(["di/no-buildserviceprovider", "sample/local-only"]);
        localFirst.Rules.Select(rule => rule.Id)
            .ShouldBe(["sample/local-only", "di/no-buildserviceprovider"]);
    }

    [Fact]
    public void DuplicateId_PackCallAuthoredFirst_ReportsThePackFile()
    {
        SpecValidationError error = DuplicateIdError(new PackCallSpec(), new LocalCollidingSpec());

        // Free provenance: the pack's methods let arch.Rule capture its own caller info, so the pack
        // file names itself without forwarding anything. Asserted by file name, never by line — pinning
        // a line would make the pack source line-sensitive, and it is meant to stay safe to reformat.
        error.Location!.File.ShouldBe("DotNetGuidance.cs");
    }

    [Fact]
    public void DuplicateId_LocalRuleAuthoredFirst_ReportsTheSpecFile()
    {
        SpecValidationError error = DuplicateIdError(new LocalCollidingSpec(), new PackCallSpec());

        // The collision is reported at the *first authored* occurrence, so swapping the spec order swaps
        // which file is named. Both directions are pinned because only the pair shows the rule.
        error.Location!.File.ShouldBe("PackCollisionSpecs.cs");
    }

    [Fact]
    public void DuplicateId_AcrossSpecClasses_IsReportedOnce()
    {
        var exception = Should.Throw<SpecValidationException>(() => ArchModelBuilder.Build(new PackCallSpec(), new LocalCollidingSpec()));

        exception.Errors
            .Count(error => error.Code == SpecValidationErrorCode.DuplicateId)
            .ShouldBe(1);
    }

    private static SpecValidationError DuplicateIdError(params IArchitectureSpec[] specs)
    {
        var exception = Should.Throw<SpecValidationException>(() => ArchModelBuilder.Build(specs));

        return exception.Errors.Single(error => error.Code == SpecValidationErrorCode.DuplicateId);
    }
}
