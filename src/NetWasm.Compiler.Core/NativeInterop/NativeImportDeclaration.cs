using System.Reflection;

namespace NetWasm.Compiler.Core.NativeInterop;

public sealed record NativeImportDeclaration(
    string LibraryName,
    string EntryPoint,
    MethodImportAttributes Attributes,
    bool IsVarArg,
    bool HasMarshalling,
    bool SuppressesGcTransition,
    bool HasCustomCallingConvention)
{
    // [WasmImportLinkage]: the library name is the Wasm import module, so the
    // host or another module supplies the function rather than a linked archive.
    public bool HasWasmImportLinkage { get; init; }
}
