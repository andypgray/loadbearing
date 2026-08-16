using System.Security;
using System.Xml;
using System.Xml.Linq;

namespace Zphil.LoadBearing.Roslyn;

/// <summary>
///     Whether a project file is SDK-style, read off the <c>.csproj</c> XML itself — the discriminator that
///     lets <see cref="RestoreFailures" /> blame a missing <c>project.assets.json</c> without blaming the
///     legacy projects that never write one.
/// </summary>
/// <remarks>
///     <para>
///         <b>Why anything needs to know.</b> Absence of an assets file asserts nothing on its own: a
///         non-SDK-style .NET Framework project never writes one, and those are exactly the codebases this
///         product is built for. It asserts a great deal for an SDK-style project, which writes one on every
///         restore — so "no assets file" there means the restore never ran, and every package edge is missing
///         from the model exactly as a failed restore leaves it.
///     </para>
///     <para>
///         <b>The three shapes, and why all three.</b> An SDK-style project can declare its SDK as an
///         attribute on the root (<c>&lt;Project Sdk="Microsoft.NET.Sdk"&gt;</c> — what every template
///         writes), as a child element (<c>&lt;Sdk Name="…" /&gt;</c>), or as a pair of explicit imports
///         (<c>&lt;Import Sdk="…" Project="Sdk.props" /&gt;</c>, the form a project pinning an SDK version
///         between the props and targets halves has to use). All three are the same fact to MSBuild, so all
///         three are the same fact here.
///     </para>
///     <para>
///         <b>Local names throughout, and never the namespace.</b> The legacy shape declares the 2003 MSBuild
///         XML namespace and the SDK shape declares none, so the <c>xmlns</c> looks like the discriminator and
///         is not one: MSBuild accepts an SDK-style project that declares the 2003 namespace anyway, and the
///         attribute is what decides. Reading local names means both are read correctly rather than one being
///         inferred from the other.
///     </para>
///     <para>
///         <b>Where the read is deliberately narrower than "anywhere in the file".</b> The <c>Sdk</c> element
///         is looked for among the root's own children, where MSBuild defines it, rather than anywhere in the
///         document — a legacy project is free to declare a <em>property</em> called <c>Sdk</c>, and reading
///         that as an SDK declaration would blame a healthy legacy project for a file it was never going to
///         write. <c>Import</c> is looked for among all descendants, because an <c>ImportGroup</c> may
///         legitimately hold one.
///     </para>
///     <para>
///         <b>Every degradation answers <see langword="false" />.</b> An unreadable file, malformed XML, a
///         root element that is not <c>Project</c>, a missing file — each says "not SDK-style", the same
///         posture <see cref="RestoreFailures" /> takes for a file it could not read. A false negative leaves
///         a partial model undetected, which is the state that already existed; a false positive refuses a
///         healthy solution, which is the disease the structural gate was written to cure. Only one of those
///         is worth risking, and it is not the second.
///     </para>
/// </remarks>
internal static class SdkStyleProject
{
    /// <summary>
    ///     Whether the project file at <paramref name="projectFilePath" /> declares an MSBuild SDK — by an
    ///     <c>Sdk</c> attribute on the root, an <c>Sdk</c> element among the root's children, or an
    ///     <c>Import</c> carrying an <c>Sdk</c> attribute. False for a legacy project, and false for every
    ///     file this cannot read.
    /// </summary>
    /// <param name="projectFilePath">The absolute path to the project file.</param>
    internal static bool IsSdkStyle(string projectFilePath)
    {
        try
        {
            // Load, not Parse: the default reader settings prohibit DTD processing, so a project file
            // carrying a DOCTYPE raises XmlException and lands on the safe answer below rather than being
            // resolved.
            XElement? root = XDocument.Load(projectFilePath)
                .Root;

            if (root is null || root.Name.LocalName != "Project") return false;

            return HasSdkAttribute(root)
                   || root.Elements()
                       .Any(element => element.Name.LocalName == "Sdk")
                   || root.Descendants()
                       .Any(element => element.Name.LocalName == "Import" && HasSdkAttribute(element));
        }
        catch (Exception ex) when (ex is IOException
                                       or UnauthorizedAccessException
                                       or NotSupportedException
                                       or SecurityException
                                       or ArgumentException
                                       or XmlException)
        {
            return false;
        }
    }

    // Unprefixed attributes are in no namespace whatever the element's is, so the local name is the whole
    // name here — matched on it anyway, for the same reason the elements are.
    private static bool HasSdkAttribute(XElement element)
    {
        return element.Attributes()
            .Any(attribute => attribute.Name.LocalName == "Sdk");
    }
}
