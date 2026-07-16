using System.Text;
using Zphil.LoadBearing.Codebase;

namespace Zphil.LoadBearing.Tests.Extraction;

/// <summary>
///     Renders every fact of a <see cref="CodebaseModel" /> to a single deterministic string, for
///     string-equality pins. It is deliberately <em>total</em> — projects, every scalar type fact,
///     declaration sites, file paths, hierarchy (base type, interfaces, attributes), the three construction
///     lists, every edge with its sites, and every member-use edge with its member facts and sites — so that
///     if a fact is not rendered here it is not pinned. The model is already fully ordered (types by
///     FullName, edges by source/target, member edges by source/member SymbolId, projects by name), so a
///     straight walk is stable. Used by the fragment JSON round-trip test to assert that
///     serialize→deserialize→merge equals a direct merge.
/// </summary>
internal static class ModelDump
{
    public static string Render(CodebaseModel model)
    {
        var builder = new StringBuilder();

        builder.AppendLine("== PROJECTS ==");
        foreach (ProjectNode project in model.Projects)
            builder.Append(project.Name).Append(" -> [").Append(string.Join(", ", project.ProjectReferences)).AppendLine("]");

        builder.AppendLine("== TYPES ==");
        foreach (TypeNode type in model.Types)
            RenderType(builder, type);

        builder.AppendLine("== EDGES ==");
        foreach (ReferenceEdge edge in model.Edges)
            builder.Append(edge.Source.FullName).Append(" -> ").Append(edge.Target.FullName)
                .Append(" @ [").Append(RenderSites(edge.Sites)).AppendLine("]");

        builder.AppendLine("== MEMBER EDGES ==");
        foreach (MemberEdge edge in model.MemberEdges)
            builder.Append(edge.Source.FullName).Append(" -> ").Append(edge.Member.SymbolId)
                .Append(" (").Append(edge.Member.Kind).Append(' ').Append(edge.Member.ContainingType.FullName)
                .Append('.').Append(edge.Member.Name).Append(") @ [").Append(RenderSites(edge.Sites)).AppendLine("]");

        return builder.ToString();
    }

    private static void RenderType(StringBuilder builder, TypeNode type)
    {
        builder.Append("TYPE ").AppendLine(type.FullName);
        builder.Append("  symbolId=").AppendLine(type.SymbolId);
        builder.Append("  name=").Append(type.Name).Append(" ns=").AppendLine(type.Namespace);
        builder.Append("  kind=").Append(type.Kind).Append(" acc=").Append(type.Accessibility)
            .Append(" sealed=").Append(type.IsSealed).Append(" static=").Append(type.IsStatic)
            .Append(" abstract=").Append(type.IsAbstract).Append(" record=").Append(type.IsRecord).AppendLine();
        builder.Append("  project=").Append(type.ProjectName).Append(" external=").Append(type.IsExternal).AppendLine();
        builder.Append("  declSites=[").Append(RenderSites(type.DeclarationSites)).AppendLine("]");
        builder.Append("  filePaths=[").Append(string.Join(", ", type.FilePaths)).AppendLine("]");
        builder.Append("  baseType=").AppendLine(FullNameOf(type.BaseType));
        builder.Append("  interfaces=[").Append(string.Join(", ", type.Interfaces.Select(FullNameOf))).AppendLine("]");
        builder.Append("  attributes=[").Append(string.Join(", ", type.Attributes.Select(FullNameOf))).AppendLine("]");
        builder.Append("  allInterfaces=[").Append(string.Join(", ", type.AllInterfaces.Select(RenderConstruction))).AppendLine("]");
        builder.Append("  baseChain=[").Append(string.Join(", ", type.BaseTypeChain.Select(RenderConstruction))).AppendLine("]");
        builder.Append("  attrConstructions=[").Append(string.Join(", ", type.AttributeConstructions.Select(RenderConstruction))).AppendLine("]");
    }

    private static string RenderSites(IReadOnlyList<SourceLocation> sites)
    {
        return string.Join(", ", sites.Select(s => s.ToString()));
    }

    // Renders both halves of a construction: the definition node's FullName (the reference-equality target)
    // and the constructed display name — so a closed generic's substituted arguments are pinned too.
    private static string RenderConstruction(TypeConstruction construction)
    {
        return $"{construction.Definition.FullName}::{construction.FullName}";
    }

    private static string FullNameOf(ITypeInfo? info)
    {
        return info is TypeNode node ? node.FullName : "<null>";
    }
}