using System;
using System.Collections.Generic;
using System.Linq;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Core.Types;

namespace NetWasm.Compiler.Analysis.Constants;

// Reads the constant that a short, effect-free producer sequence pushes:
// integer, null and string literals, and byte spans over read-only static data.
internal sealed class ConstantOperandReader(
    ICalledMethodResolver calledMethods,
    IFieldRepository fields,
    ITypeRepository types,
    IPreciseStaticConstructorClassifier constructors) : IConstantOperandReader
{
    private readonly ICalledMethodResolver _calledMethods = calledMethods ?? throw new ArgumentNullException(nameof(calledMethods));
    private readonly IFieldRepository _fields = fields ?? throw new ArgumentNullException(nameof(fields));
    private readonly ITypeRepository _types = types ?? throw new ArgumentNullException(nameof(types));
    private readonly IPreciseStaticConstructorClassifier _constructors = constructors ??
        throw new ArgumentNullException(nameof(constructors));

    public int MaximumLength => 3;

    public ScalarConstant? Read(IReadOnlyList<CilInstruction> producers)
    {
        ArgumentNullException.ThrowIfNull(producers);
        return producers switch
        {
            [{ Operation: CilOperation.LoadInt32, Operand: CilOperand.ConstantI4 value }] => new ScalarConstant.Int32(value.Value),
            [{ Operation: CilOperation.LoadNull }] => new ScalarConstant.Null(),
            [{ Operation: CilOperation.LoadString, Operand: CilOperand.UserString text }] => new ScalarConstant.String(text.Value),
            [{ Operation: CilOperation.CreateReadOnlySpanFromRva, Operand: CilOperand.ReadOnlySpanData span }]
                when span.ElementType.CanonicalName == "primitive:u1" => ReadBytes(span.Field.Definition, span.ElementCount),
            // Roslyn's UTF-8 literal: new ReadOnlySpan<byte>(&<PrivateImplementationDetails>.field, length).
            [
            { Operation: CilOperation.LoadStaticFieldAddress, Operand: CilOperand.Entity field },
            { Operation: CilOperation.LoadInt32, Operand: CilOperand.ConstantI4 length },
            { Operation: CilOperation.NewObject } construction,
            ] when IsBytePointerConstructor(_calledMethods.Resolve(construction)) => ReadBytes(_fields.GetField(field.Key), length.Value),
            _ => null,
        };
    }

    private static bool IsBytePointerConstructor(MethodInstanceModel? constructor) =>
        constructor is { Definition.Name: ".ctor", Definition.IsStatic: false } &&
        ReadOnlySpanDataResolver.TryGetReadOnlySpanElement(constructor.DeclaringType, out var element) &&
        element.CanonicalName == "primitive:u1" &&
        constructor.Signature.ParameterSignatureTypes is [{ Shape: CliTypeShape.UnmanagedPointer }, { StackKind: CliValueKind.I4 }];

    private ScalarConstant.Bytes? ReadBytes(FieldDefinitionModel definition, int length)
    {
        if (!definition.IsStatic || !definition.IsInitOnly || definition.InitialData.IsDefaultOrEmpty ||
            (uint)length > (uint)definition.InitialData.Length)
        {
            return null;
        }
        // Taking the field's address would run a precise static constructor.
        var owner = _types.GetTypeDefinition(definition.DeclaringType);
        if (_constructors.Classify(owner))
        {
            return null;
        }
        return new([.. definition.InitialData.Take(length)]);
    }
}
