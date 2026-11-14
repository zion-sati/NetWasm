using System;
using System.Collections.Generic;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Core.Types;

namespace NetWasm.Compiler.Analysis;

// A load without effects: an argument, a local, a constant (through unchecked conversions, which cannot throw), null,
// a string, or a static field of a type whose static constructor does not run at a precise point.
internal sealed class EffectFreeLoadFinder(
    IFieldRepository fields,
    ITypeRepository types,
    IPreciseStaticConstructorClassifier constructors) : IEffectFreeLoadFinder
{
    private readonly IFieldRepository _fields = fields ?? throw new ArgumentNullException(nameof(fields));
    private readonly ITypeRepository _types = types ?? throw new ArgumentNullException(nameof(types));
    private readonly IPreciseStaticConstructorClassifier _constructors = constructors ??
        throw new ArgumentNullException(nameof(constructors));

    public int FindStart(IReadOnlyList<CilInstruction> operations, int end)
    {
        ArgumentNullException.ThrowIfNull(operations);
        var index = end - 1;
        while (index >= 0 && operations[index].Operation is CilOperation.ConvertInt32 or CilOperation.ConvertInt32Unsigned or
            CilOperation.ConvertInt64 or CilOperation.ConvertInt64Unsigned or CilOperation.ConvertNativeInt or
            CilOperation.ConvertNativeUInt or CilOperation.ConvertFloat32 or CilOperation.ConvertFloat64 or
            CilOperation.ConvertFloatUnsigned)
        {
            index--;
        }
        return index >= 0 && IsEffectFree(operations[index]) ? index : -1;
    }

    private bool IsEffectFree(CilInstruction instruction) => instruction switch
    {
        { Operation: CilOperation.LoadArgument or CilOperation.LoadLocal or CilOperation.LoadInt32 or CilOperation.LoadInt64 or
            CilOperation.LoadFloat32 or CilOperation.LoadFloat64 or CilOperation.LoadNull or CilOperation.LoadString } => true,
        { Operation: CilOperation.LoadStaticField, Operand: CilOperand.Entity field } => InitializationIsOptional(field.Key),
        _ => false,
    };

    private bool InitializationIsOptional(EntityKey field) =>
        !_constructors.Classify(_types.GetTypeDefinition(_fields.GetField(field).DeclaringType));
}
