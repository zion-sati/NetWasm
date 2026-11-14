using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace NetWasm.Compiler.Tests;

// Compares each NetWasm.App library's public API with upstream's reference assemblies, one row per difference.
// Types and members are identified by their documentation comment IDs, and a type is matched wherever upstream
// declares it. A type the library forwards (to CoreLib) is left to CoreLib's own audit.
internal static class FrameworkSurfaceAudit
{
    public const string Header = "assembly\tkind\tapi\tupstream";

    // Kinds, for a library L:
    //   not-upstream        a public type no upstream reference assembly declares;
    //   upstream-elsewhere  a public type upstream declares in another assembly (the last column);
    //   extra-member        a member of one of L's types that upstream's type lacks;
    //   missing-member      a member of upstream's type that L's type lacks;
    //   missing-type        a type upstream's L declares that L neither declares nor forwards.
    public static IReadOnlyList<string> Compute(
        IReadOnlyList<MetadataReference> libraries,
        IReadOnlyList<MetadataReference> dependencies,
        IReadOnlyList<MetadataReference> upstream)
    {
        var ours = CSharpCompilation.Create("NetWasm.App", references: [.. libraries, .. dependencies]);
        var theirs = CSharpCompilation.Create("Upstream", references: upstream);
        var upstreamTypes = new Dictionary<string, (string Assembly, HashSet<string> Members)>(StringComparer.Ordinal);
        foreach (var assembly in upstream.Select(theirs.GetAssemblyOrModuleSymbol).OfType<IAssemblySymbol>())
        {
            foreach (var type in PublicTypes(assembly.GlobalNamespace))
            {
                upstreamTypes.TryAdd(Id(type), (assembly.Name, Members(type)));
            }
        }

        var rows = new List<string>();
        foreach (var library in libraries.Select(ours.GetAssemblyOrModuleSymbol).OfType<IAssemblySymbol>())
        {
            var declared = new HashSet<string>(StringComparer.Ordinal);
            foreach (var type in PublicTypes(library.GlobalNamespace))
            {
                var id = Id(type);
                declared.Add(id);
                if (!upstreamTypes.TryGetValue(id, out var match))
                {
                    rows.Add(Row(library, "not-upstream", id));
                    continue;
                }
                if (match.Assembly != library.Name)
                {
                    rows.Add(Row(library, "upstream-elsewhere", id, match.Assembly));
                }
                var members = Members(type);
                rows.AddRange(members.Except(match.Members).Select(member => Row(library, "extra-member", member)));
                rows.AddRange(match.Members.Except(members).Select(member => Row(library, "missing-member", member)));
            }
            var forwarded = library.GetForwardedTypes().Select(Id).ToHashSet(StringComparer.Ordinal);
            rows.AddRange(upstreamTypes
                .Where(pair => pair.Value.Assembly == library.Name && !declared.Contains(pair.Key) && !forwarded.Contains(pair.Key))
                .Select(pair => Row(library, "missing-type", pair.Key)));
        }
        rows.Sort(StringComparer.Ordinal);
        return [Header, .. rows];
    }

    private static string Row(IAssemblySymbol library, string kind, string api, string upstream = "") =>
        $"{library.Name}\t{kind}\t{api}\t{upstream}";

    private static string Id(ISymbol symbol) =>
        symbol.GetDocumentationCommentId() ?? throw new InvalidOperationException($"{symbol} has no documentation ID.");

    // Public and protected API: what a consumer outside the assembly can name or override.
    private static bool Visible(ISymbol symbol) =>
        symbol.DeclaredAccessibility is Accessibility.Public or Accessibility.Protected or Accessibility.ProtectedOrInternal;

    private static IEnumerable<INamedTypeSymbol> PublicTypes(INamespaceOrTypeSymbol container)
    {
        foreach (var member in container.GetMembers())
        {
            if (member is INamespaceSymbol nested)
            {
                foreach (var type in PublicTypes(nested))
                {
                    yield return type;
                }
            }
            else if (member is INamedTypeSymbol type && Visible(type))
            {
                yield return type;
                foreach (var inner in PublicTypes(type))
                {
                    yield return inner;
                }
            }
        }
    }

    private static HashSet<string> Members(INamedTypeSymbol type) =>
        [.. type.GetMembers().Where(member => member is not INamedTypeSymbol && Visible(member)).Select(Id)];
}
