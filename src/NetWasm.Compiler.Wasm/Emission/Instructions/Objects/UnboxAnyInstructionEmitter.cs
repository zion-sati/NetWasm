using NetWasm.Compiler.Wasm.Encoding;
using System.Collections.Immutable;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Core.Types;

namespace NetWasm.Compiler.Wasm.Emission.Instructions.Objects;

internal sealed class UnboxAnyInstructionEmitter(
    ICilTypeOperandResolver types,
    IUnboxEmitter boxing,
    ITypeTestEmitter typeTests,
    INullableTypeResolver nullableTypes,
    INullableUnboxAnyEmitter nullableUnboxes) : InstructionCommandProvider
{
    public override ImmutableArray<InstructionCommand> Commands =>
    [
        new(
            CilOperation.UnboxAny,
            InstructionFamily.AllocationBoxingTypes,
            EmitUnboxAny),
    ];

    private void EmitUnboxAny(InstructionEmissionRequest request, IWasmInstructionWriter code)
    {
        var type = types.Resolve(request.Instruction, request.Header.MethodInstance);
        if (!type.IsValueType)
        {
            typeTests.Test(request, code, false);
            return;
        }
        var underlyingType = nullableTypes.Resolve(type);
        if (underlyingType is not null)
        {
            nullableUnboxes.Emit(request, code, type, underlyingType);
            return;
        }
        boxing.Unbox(request, code, true);
    }
}
