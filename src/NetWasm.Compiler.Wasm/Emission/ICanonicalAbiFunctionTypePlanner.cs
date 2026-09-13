using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Wasm.Emission;

internal interface ICanonicalAbiFunctionTypePlanner
{
    CanonicalAbiFunctionLayout Plan(
        CanonicalAbiFunction function,
        CanonicalAbiDirection direction);
}
