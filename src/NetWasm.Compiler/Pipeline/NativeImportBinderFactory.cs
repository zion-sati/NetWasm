using System.Collections.Generic;
using NetWasm.Compiler.Core.NativeInterop;

namespace NetWasm.Compiler.Pipeline;

internal sealed class NativeImportBinderFactory : INativeImportBinderFactory
{
    public INativeImportBinder Create(IEnumerable<string>? linkedLibraries) => new NativeLibraryBinding(linkedLibraries);
}
