using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Wasm.Emission;

internal interface ICilTypeOperandResolver
{
    CliTypeIdentity Resolve(CilInstruction instruction, MethodInstanceModel? methodInstance);
}
