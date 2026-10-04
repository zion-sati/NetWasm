using System.Collections.Immutable;
using NetWasm.Compiler.ComponentModel;

namespace NetWasm.Compiler.Tasks.ComponentModel;

internal interface IInternalRuntimeExportReader
{
    ImmutableArray<WasmInternalExport> Read(string metadata);
}
