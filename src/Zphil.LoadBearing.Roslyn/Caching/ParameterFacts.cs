namespace Zphil.LoadBearing.Roslyn.Caching;

/// <summary>
///     The scalar facts of one declared method parameter (GRAMMAR §4.6, §5.6), extracted from a Roslyn
///     symbol but holding no Roslyn types — the parameter analog of <see cref="MemberFacts" /> and the
///     pure-data payload the merge turns into a <see cref="Zphil.LoadBearing.Codebase.ParameterNode" />.
///     Read in declaration order off a method's <c>IMethodSymbol.Parameters</c> (the extension-method
///     <c>this</c> parameter included, default-valued parameters counted); only methods carry parameters, so
///     properties, fields, and events hold an empty list.
/// </summary>
/// <remarks>
///     <see cref="TypeFullName" /> is the definition-level FQN in extraction format, normalized by the very
///     same helper that produces <see cref="MemberFacts.ReturnTypeFullName" /> — a constructed generic
///     reduces to its definition, so a <c>MustAcceptParameter</c> anchor matches at the definition level
///     (GRAMMAR §5.6). <c>ref</c>/<c>in</c>/<c>out</c> do not change the recorded type; a <c>params T[]</c>
///     records the array type; a <c>T?</c> records <c>System.Nullable&lt;T&gt;</c>'s definition form. Like the
///     rest of the fragment DTO this record carries no dependency on <c>Microsoft.CodeAnalysis</c> and
///     round-trips through System.Text.Json unchanged.
/// </remarks>
internal sealed record ParameterFacts(
    string Name,
    string TypeFullName);