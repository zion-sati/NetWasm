using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission.Methods;

namespace NetWasm.Compiler.Wasm.Emission.Instructions.Interop;

internal interface IStackLocalResolver
{
    int Resolve(MethodEmissionContext context, int slot, CliValueKind type);
}
