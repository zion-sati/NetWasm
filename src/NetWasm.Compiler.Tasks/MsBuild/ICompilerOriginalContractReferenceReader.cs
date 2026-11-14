using System.Collections.Immutable;
using Microsoft.Build.Framework;

namespace NetWasm.Compiler.Tasks.MsBuild;

internal interface ICompilerOriginalContractReferenceReader
{
    ImmutableArray<string> Read(IEnumerable<ITaskItem> references);
}
