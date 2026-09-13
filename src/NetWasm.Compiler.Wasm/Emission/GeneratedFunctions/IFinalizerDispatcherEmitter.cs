using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission.Planning;

namespace NetWasm.Compiler.Wasm.Emission.GeneratedFunctions;

internal interface IFinalizerDispatcherEmitter
{
    byte[] Emit(
        TypeDescriptorLayout[] finalizableTypes,
        IFunctionIndexResolver functionIndices);
}
