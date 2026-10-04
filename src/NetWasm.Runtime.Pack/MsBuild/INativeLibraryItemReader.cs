using System.Collections.Immutable;
using Microsoft.Build.Framework;
using NetWasm.Runtime.Pack.Materialization;

namespace NetWasm.Runtime.Pack.MsBuild;

internal interface INativeLibraryItemReader
{
    ImmutableArray<RuntimeNativeLibraryDescriptor> Read(ITaskItem[] items);
}
