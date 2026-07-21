using System.Text.Json.Serialization;

namespace Zphil.LoadBearing.Cli.Rendering;

// The wire shape of `check --sarif`: SARIF 2.1.0 (OASIS), the subset LoadBearing emits — one run, a
// driver carrying a reporting descriptor per rule, and one result per violation site. Pinned by the golden
// `Cli/Golden/violated-check.sarif`. Clustered in one file: these records are one cohesive DTO, not product
// types. Serialized with the shared LoadBearingJson.Options (camelCase names, indented, nulls omitted);
// dictionary keys ride verbatim (no DictionaryKeyPolicy), so the SRCROOT base-id and the fingerprint key
// emit exactly as written. The one property camelCase would corrupt — `$schema`, which it would strip the
// `$` from — carries an explicit name attribute; every other name is its positional property camelCased.

/// <summary>The root SARIF log — the whole document written by <c>--sarif</c>.</summary>
internal sealed record SarifLog(
    [property: JsonPropertyName("$schema")]
    string Schema,
    string Version,
    IReadOnlyList<SarifRun> Runs);

/// <summary>The single analysis run: its tool, invocation, URI base, and results.</summary>
internal sealed record SarifRun(
    SarifTool Tool,
    IReadOnlyList<SarifInvocation> Invocations,
    IReadOnlyDictionary<string, SarifArtifactLocationBase> OriginalUriBaseIds,
    IReadOnlyList<SarifResult> Results);

/// <summary>The analysis-tool wrapper.</summary>
internal sealed record SarifTool(SarifDriver Driver);

/// <summary>The tool driver: name, version, information URI, and the per-rule metadata catalog.</summary>
internal sealed record SarifDriver(
    string Name,
    string Version,
    string InformationUri,
    IReadOnlyList<SarifReportingDescriptor> Rules);

/// <summary>One rule's metadata (a SARIF reportingDescriptor). The null message slots are omitted.</summary>
internal sealed record SarifReportingDescriptor(
    string Id,
    SarifMessage? ShortDescription,
    SarifMessage? FullDescription,
    SarifMessage? Help,
    SarifReportingConfiguration DefaultConfiguration,
    SarifRuleProperties Properties);

/// <summary>A rule's default configuration — its severity <c>level</c>.</summary>
internal sealed record SarifReportingConfiguration(string Level);

/// <summary>A rule's LoadBearing-specific property bag — currently just its posture.</summary>
internal sealed record SarifRuleProperties(string Posture);

/// <summary>One result — a single violation site. <see cref="Suppressions" /> is null (omitted) on red.</summary>
internal sealed record SarifResult(
    string RuleId,
    string Level,
    SarifMessage Message,
    IReadOnlyList<SarifLocation> Locations,
    IReadOnlyDictionary<string, string> PartialFingerprints,
    string BaselineState,
    IReadOnlyList<SarifSuppression>? Suppressions);

/// <summary>A SARIF message string — serves both a result's <c>message</c> and a rule's descriptions.</summary>
internal sealed record SarifMessage(string Text);

/// <summary>One result location.</summary>
internal sealed record SarifLocation(SarifPhysicalLocation PhysicalLocation);

/// <summary>A physical location: the artifact and the region within it.</summary>
internal sealed record SarifPhysicalLocation(SarifArtifactLocation ArtifactLocation, SarifRegion Region);

/// <summary>An artifact reference — a solution-relative URI resolved against the <see cref="UriBaseId" /> base.</summary>
internal sealed record SarifArtifactLocation(string Uri, string UriBaseId);

/// <summary>The empty base descriptor (<c>{}</c>) the SRCROOT URI base id maps to.</summary>
internal sealed record SarifArtifactLocationBase;

/// <summary>A source region — just the 1-based start line.</summary>
internal sealed record SarifRegion(int StartLine);

/// <summary>A result suppression (kind <c>external</c>) carrying the baseline justification.</summary>
internal sealed record SarifSuppression(string Kind, string Justification);

/// <summary>The tool invocation: whether it ran successfully and any tool-execution notifications.</summary>
internal sealed record SarifInvocation(bool ExecutionSuccessful, IReadOnlyList<SarifNotification>? ToolExecutionNotifications);

/// <summary>One tool-execution notification (a workspace diagnostic), level <c>warning</c>.</summary>
internal sealed record SarifNotification(SarifMessage Message, string Level);