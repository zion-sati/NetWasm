using System.Collections.Immutable;
using Microsoft.Build.Framework;

namespace NetWasm.Compiler.Tasks.MsBuild;

internal sealed class CompilerOriginalContractReferenceReader : ICompilerOriginalContractReferenceReader
{
    public ImmutableArray<string> Read(IEnumerable<ITaskItem> references)
    {
        ArgumentNullException.ThrowIfNull(references);
        return references.Select(static item => item.GetMetadata("NetWasmOriginalReference"))
            .Where(static path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.Ordinal)
            .ToImmutableArray();
    }
}
