using NetWasm.Compiler.ControlFlow.Structured;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Wasm.Emission;

internal interface IArgumentTypeResolver
{
    CliValueKind Resolve(StructuredMethodHeader header, int index);
}
