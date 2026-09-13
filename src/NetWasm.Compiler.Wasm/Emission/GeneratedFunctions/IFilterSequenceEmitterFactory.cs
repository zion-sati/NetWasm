using NetWasm.Compiler.Wasm.Emission.Planning;

namespace NetWasm.Compiler.Wasm.Emission.GeneratedFunctions;

internal interface IFilterSequenceEmitterFactory
{
    IFilterSequenceEmitter Create(
        InstructionModuleTarget target,
        IFunctionIndexResolver functionIndices);
}
