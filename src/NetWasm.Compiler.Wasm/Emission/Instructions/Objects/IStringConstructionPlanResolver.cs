using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Wasm.Emission.Instructions.Objects;

internal interface IStringConstructionPlanResolver
{
    StringConstructionPlan Resolve(
        MethodSignatureModel signature,
        int instructionOffset);
}
