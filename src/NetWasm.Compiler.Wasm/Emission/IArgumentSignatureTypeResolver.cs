using NetWasm.Compiler.ControlFlow.Structured;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Wasm.Emission;

internal interface IArgumentSignatureTypeResolver
{
    CliTypeIdentity Resolve(StructuredMethodHeader header, int index);
}
