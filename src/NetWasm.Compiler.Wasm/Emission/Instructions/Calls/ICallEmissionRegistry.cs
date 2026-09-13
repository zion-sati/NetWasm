namespace NetWasm.Compiler.Wasm.Emission.Instructions.Calls;

internal interface ICallEmissionRegistry
{
    ICallEmitter Get(CallEmissionKind kind);
}
