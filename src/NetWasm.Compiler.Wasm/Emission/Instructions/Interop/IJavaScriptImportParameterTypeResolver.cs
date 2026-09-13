using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Wasm.Emission.Instructions.Interop;

internal interface IJavaScriptImportParameterTypeResolver
{
    CliValueKind Resolve(CliTypeIdentity type, bool isCallback);
}
