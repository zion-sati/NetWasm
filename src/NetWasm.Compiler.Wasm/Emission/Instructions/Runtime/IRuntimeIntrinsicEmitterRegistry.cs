using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Wasm.Emission.Instructions.Runtime;

internal interface IRuntimeIntrinsicEmitterRegistry
{
    IRuntimeIntrinsicEmitter Get(RuntimeIntrinsic intrinsic);
}
