using System.Collections.Immutable;
using Microsoft.Build.Framework;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Tasks.MsBuild;

internal sealed class CompilerReferenceOriginReader : ICompilerReferenceOriginReader
{
    public ImmutableArray<CompilationReferenceOrigin> Read(
        ITaskItem[] references, CompilationReferenceRole role)
    {
        ArgumentNullException.ThrowIfNull(references);
        return references.Select(item => new CompilationReferenceOrigin(
            item.ItemSpec,
            role,
            ReadMetadata(item, "NuGetPackageId"),
            ReadMetadata(item, "NuGetPackageVersion"),
            ReadMetadata(item, "PathInPackage"),
            ReadMetadata(item, "NetWasmOriginalReference"))).ToImmutableArray();
    }

    private static string? ReadMetadata(ITaskItem item, string name)
    {
        var value = item.GetMetadata(name);
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }
}
