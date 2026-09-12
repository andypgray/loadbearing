using System.Globalization;
using System.Reflection;

namespace Zphil.LoadBearing.Tests.DocHygiene;

/// <summary>
///     Renders a shipped assembly's exported surface as the text its committed pin holds, and says what
///     moved between that pin and a fresh render.
/// </summary>
/// <remarks>
///     <para>
///         <b>What the pin carries, and what it deliberately leaves out.</b> Every exported type's full
///         name, and under each exported enum its constants with their numeric values. Members are
///         absent on purpose. The two shipping csprojs run ApiCompat over the published package at pack
///         time with strict mode on, which reports an added member as loudly as a removed one — so
///         pinning members here would move this file on roughly half of all commits to repeat what the
///         nupkg build already says, and a file that moves that often stops being read. At this grain
///         it moves on about one commit in six.
///     </para>
///     <para>
///         <b>So this file duplicates coverage rather than adding it, and that is the point.</b>
///         ApiCompat answers against the published bytes, which is the right authority — but only at
///         pack time, which in practice means one CI job and the release gate. Nothing runs
///         <c>dotnet pack</c> while writing code. This reds in the suite, in seconds, beside the edit.
///     </para>
///     <para>
///         <b>Enum constants carry their values, even though ApiCompat sees those too.</b> Measured
///         2026-09-12: inserting a member mid-list reports as <c>CP0011</c> — "Value of field
///         'Unchanged' in enum 'WriteOutcome' changed from '1' to '2'" — and unlike the additions it
///         reports without strict mode, being a break rather than a growth. So the values are not unique
///         coverage — they are the fast signal for the one surface change that breaks no compile
///         anywhere. A renumbering leaves every caller building green while the constants baked into
///         the IL of specs already compiled against an older contract come to mean something else, and
///         the pinned <c>AssemblyVersion</c> is what keeps those specs binding to meet it. That is the
///         change most likely to be shipped unnoticed, so it is worth seeing in seconds rather than at
///         pack time — and recording the values makes this file readable as the contract itself.
///     </para>
///     <para>
///         <b>Constants are ordered by value, not by name.</b> Name order would still catch a
///         renumbering, because each line carries its value — but reading the diff is the point, and in
///         value order an insertion shows as one added line followed by the run of members it pushed
///         along. <see cref="FieldInfo.GetRawConstantValue" /> is read rather than
///         <see cref="FieldInfo.GetValue" /> so nothing has to initialize the type, and the ordering key
///         is <see cref="decimal" /> because it is the one numeric type wide enough for every integral
///         underlying type a C# enum may have, <see cref="ulong" /> included.
///     </para>
///     <para>
///         <b><see cref="StringComparer.Ordinal" /> throughout.</b> Culture-aware collation differs
///         between runner images, and these strings are full of the characters it treats least
///         predictably — <c>`</c> on generic arity, <c>+</c> on nested types, <c>.</c> between namespace
///         parts. Ordinal is the only comparison that renders the same file on every machine. Names come
///         from <see cref="Type.FullName" /> for the same reason: <c>ToString()</c> and
///         <c>AssemblyQualifiedName</c> both carry text that moves for reasons unrelated to the surface.
///     </para>
/// </remarks>
internal static class PublicSurface
{
    private const string PinDirectory = "tests/Zphil.LoadBearing.Tests/DocHygiene";

    /// <summary>The indent marking a line as a constant belonging to the enum above it.</summary>
    private const string ConstantIndent = "    ";

    /// <summary>
    ///     The binding identity every shipped assembly carries. It is pinned rather than tracking the
    ///     package version, because a spec project's compiled reference names it and the default binder
    ///     refuses a host older than that reference.
    /// </summary>
    internal static readonly Version PinnedBindingIdentity = new(1, 0, 0, 0);

    /// <summary>The repo-relative path of the pin recording <paramref name="assembly" />'s surface.</summary>
    internal static string PinPath(Assembly assembly)
    {
        string name = assembly.GetName()
            .Name!;

        return $"{PinDirectory}/PublicSurface.{name}.txt";
    }

    /// <summary>
    ///     <paramref name="assembly" />'s exported surface as pin text: one line per exported type,
    ///     ordinal-sorted, each exported enum followed by its indented constants in value order. Ends on
    ///     exactly one newline, which is how the file is committed.
    /// </summary>
    internal static string Render(Assembly assembly)
    {
        return Render(assembly.GetExportedTypes());
    }

    /// <summary>
    ///     The same render over an explicit type set, which is how the rendering rules are exercised
    ///     against fixtures small enough to state an expected file for.
    /// </summary>
    internal static string Render(IReadOnlyCollection<Type> exported)
    {
        IOrderedEnumerable<Type> ordered = exported.OrderBy(FullNameOf, StringComparer.Ordinal);
        List<string> lines = new();

        foreach (Type type in ordered)
        {
            lines.Add(FullNameOf(type));
            if (!type.IsEnum) continue;

            lines.AddRange(Constants(type)
                .Select(static constant => $"{ConstantIndent}{constant.Name} = {constant.Value}"));
        }

        return string.Join("\n", lines) + "\n";
    }

    /// <summary>
    ///     What moved between the committed <paramref name="pinned" /> text and a fresh
    ///     <paramref name="rendered" /> one: one finding per line, <c>+</c> for what the render has and
    ///     the pin does not and <c>-</c> for the reverse, each constant qualified by the enum it sits
    ///     under so a finding reads on its own.
    /// </summary>
    internal static IReadOnlyList<string> Drift(string pinned, string rendered)
    {
        // Line endings are somebody else's gate. A checkout that rewrote the pin to CRLF would otherwise
        // report every line as both added and removed, which reads like the surface changed and is the
        // one message a reader must not be given wrongly.
        pinned = pinned.Replace("\r\n", "\n", StringComparison.Ordinal);

        string[] before = Qualified(pinned);
        string[] after = Qualified(rendered);
        HashSet<string> beforeSet = new(before, StringComparer.Ordinal);
        HashSet<string> afterSet = new(after, StringComparer.Ordinal);

        List<string> drift =
        [
            .. after.Where(line => !beforeSet.Contains(line))
                .Select(static line => $"+ {line}"),
            .. before.Where(line => !afterSet.Contains(line))
                .Select(static line => $"- {line}")
        ];

        // Set arithmetic cannot see a pure reordering, and for an enum the order is half the point of
        // pinning it at all — so fall back to the texts themselves when the line sets agree.
        if (drift.Count == 0 && !string.Equals(pinned, rendered, StringComparison.Ordinal))
            drift.Add("the pin holds exactly these lines, in a different order");

        return drift;
    }

    /// <summary>
    ///     <paramref name="enumType" />'s constants in value order, ties broken by name so equal-valued
    ///     aliases render the same way twice running.
    /// </summary>
    internal static IReadOnlyList<(string Name, string Value)> Constants(Type enumType)
    {
        FieldInfo[] fields =
            enumType.GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly);

        return fields
            .Select(static field => (field.Name, Raw: field.GetRawConstantValue()!))
            .OrderBy(static constant => Convert.ToDecimal(constant.Raw, CultureInfo.InvariantCulture))
            .ThenBy(static constant => constant.Name, StringComparer.Ordinal)
            .Select(static constant =>
                (constant.Name, Value: Convert.ToString(constant.Raw, CultureInfo.InvariantCulture)!))
            .ToArray();
    }

    /// <summary>Every exported enum in <paramref name="assembly" />, in the order the pin renders them.</summary>
    internal static IReadOnlyList<Type> ExportedEnums(Assembly assembly)
    {
        Type[] exported = assembly.GetExportedTypes();

        return exported.Where(static type => type.IsEnum)
            .OrderBy(FullNameOf, StringComparer.Ordinal)
            .ToArray();
    }

    // Pin text as standalone lines: a constant is re-attached to the enum above it, so a finding names
    // what changed without the reader having to go and look the line up in context.
    private static string[] Qualified(string text)
    {
        string[] lines = text.Split('\n');
        List<string> qualified = new();
        var owner = string.Empty;

        foreach (string line in lines)
        {
            if (line.Length == 0) continue;

            if (!line.StartsWith(ConstantIndent, StringComparison.Ordinal))
            {
                owner = line;
                qualified.Add(line);
                continue;
            }

            // A constant ahead of any type line means a hand-edited pin rather than a rendered one.
            // Report the line as it stands: there is no owner to name, and "+ : Mike = 2" names nothing.
            qualified.Add(owner.Length == 0 ? line.Trim() : $"{owner}: {line.Trim()}");
        }

        return qualified.ToArray();
    }

    private static string FullNameOf(Type type)
    {
        return type.FullName
               ?? throw new InvalidOperationException($"Exported type {type.Name} has no full name.");
    }
}
