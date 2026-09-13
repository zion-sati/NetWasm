namespace NetWasm.Compiler.Wasm.Emission.Methods;

internal interface IManagedMethodBodyEmitterFactory
{
    IManagedMethodBodyEmitter Create();
}
