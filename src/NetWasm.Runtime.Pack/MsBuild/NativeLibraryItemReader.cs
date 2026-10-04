using System;
using System.Collections.Immutable;
using System.IO;
using Microsoft.Build.Framework;
using NetWasm.Runtime.Pack.Materialization;

namespace NetWasm.Runtime.Pack.MsBuild;

internal sealed class NativeLibraryItemReader : INativeLibraryItemReader
{
    public ImmutableArray<RuntimeNativeLibraryDescriptor> Read(ITaskItem[] items)
    {
        ArgumentNullException.ThrowIfNull(items);
        var result = ImmutableArray.CreateBuilder<RuntimeNativeLibraryDescriptor>(items.Length);
        foreach (var item in items)
        {
            if (item is null || string.IsNullOrWhiteSpace(item.ItemSpec))
                throw new InvalidOperationException("A NativeLibrary item has no archive identity.");
            var library = item.GetMetadata("NetWasmLibraryName");
            var target = item.GetMetadata("WasmTarget");
            var path = item.GetMetadata("FullPath");
            if (string.IsNullOrWhiteSpace(library) || library.IndexOfAny(['\0', '\r', '\n']) >= 0 ||
                target is not ("wasm32" or "wasm64") || string.IsNullOrWhiteSpace(path) ||
                !Path.IsPathFullyQualified(path) || path.IndexOfAny(['\0', '\r', '\n']) >= 0)
                throw new InvalidOperationException("A NativeLibrary item requires NetWasmLibraryName, WasmTarget and an unambiguous full archive path.");
            result.Add(new(library, target, Path.GetFullPath(path)));
        }
        return result.MoveToImmutable();
    }
}
