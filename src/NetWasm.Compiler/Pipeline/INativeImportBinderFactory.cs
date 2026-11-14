using System.Collections.Generic;
using NetWasm.Compiler.Core.NativeInterop;

namespace NetWasm.Compiler.Pipeline;

internal interface INativeImportBinderFactory
{
    // The binder for one compile: the libraries it links, or null when every import binds.
    INativeImportBinder Create(IEnumerable<string>? linkedLibraries);
}
