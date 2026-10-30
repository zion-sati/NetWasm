using System;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.ControlFlow;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Core.Types;
using NetWasm.Compiler.Metadata;

namespace NetWasm.Compiler.Analysis.Constants;

// x.GetType() needs no lookup when x can have one type only. A value of a
// non-nullable value type, boxed from a constant, local or argument, becomes
// typeof of that type and the box goes; a reference whose static type is a
// sealed class (string included) or a new object keeps its null check and
// names its type. The receiver is the instruction just before, in one block.
internal sealed class ExactObjectTypeRewriter(
    IControlFlowGraphBuilder graphs,
    ICalledMethodResolver calledMethods,
    IFieldRepository fields,
    ITypeDefinitionResolver typeDefinitions,
    INullableTypeResolver nullableTypes) : IMethodRewriteRule
{
    private static readonly CliTypeIdentity StringType =
        CliTypeIdentity.Primitive("string", CliValueKind.ManagedReference, isValueType: false);

    private readonly IControlFlowGraphBuilder _graphs = graphs ?? throw new ArgumentNullException(nameof(graphs));
    private readonly ICalledMethodResolver _calledMethods = calledMethods ?? throw new ArgumentNullException(nameof(calledMethods));
    private readonly IFieldRepository _fields = fields ?? throw new ArgumentNullException(nameof(fields));
    private readonly ITypeDefinitionResolver _typeDefinitions = typeDefinitions ?? throw new ArgumentNullException(nameof(typeDefinitions));
    private readonly INullableTypeResolver _nullableTypes = nullableTypes ?? throw new ArgumentNullException(nameof(nullableTypes));

    public CilMethodBody Rewrite(CilMethodBody body)
    {
        ArgumentNullException.ThrowIfNull(body);
        if (!body.Instructions.Any(static instruction => instruction is
            { Operation: CilOperation.GetObjectType, Operand: CilOperand.None }))
        {
            return body;
        }

        var rewritten = body.Instructions.ToDictionary(instruction => instruction.Offset);
        var changed = false;
        foreach (var block in _graphs.Build(body).Blocks)
        {
            var operations = block.Instructions.Where(instruction => instruction.Operation != CilOperation.Nop).ToArray();
            for (var index = 1; index < operations.Length; index++)
            {
                if (operations[index] is not { Operation: CilOperation.GetObjectType, Operand: CilOperand.None } getType)
                {
                    continue;
                }
                var receiver = operations[index - 1];
                if (receiver is { Operation: CilOperation.Box, Operand: CilOperand.TypeIdentity { Value: var boxed } } &&
                    index >= 2 && IsPureValue(operations[index - 2]) &&
                    Closed(boxed, body) is { IsValueType: true } valueType &&
                    _nullableTypes.Resolve(valueType) is null)
                {
                    var value = operations[index - 2];
                    rewritten[value.Offset] = value with { Operation = CilOperation.Nop, Operand = new CilOperand.None() };
                    rewritten[receiver.Offset] = receiver with
                    {
                        Operation = CilOperation.LoadTypeToken,
                        Operand = new CilOperand.TypeIdentity(valueType),
                    };
                    rewritten[getType.Offset] = getType with { Operation = CilOperation.MaterializeType };
                    changed = true;
                }
                else if (ExactReferenceType(receiver, body) is { } exact)
                {
                    rewritten[getType.Offset] = getType with { Operand = new CilOperand.ExactType(exact) };
                    changed = true;
                }
            }
        }

        return changed ? body with { Instructions = [.. body.Instructions.Select(instruction => rewritten[instruction.Offset])] } : body;
    }

    // Pushes one value without side effects, so it can go with the box.
    private static bool IsPureValue(CilInstruction instruction) => instruction.Operation is
        CilOperation.LoadInt32 or CilOperation.LoadInt64 or CilOperation.LoadFloat32 or CilOperation.LoadFloat64 or
        CilOperation.LoadLocal or CilOperation.LoadArgument;

    private CliTypeIdentity? ExactReferenceType(CilInstruction receiver, CilMethodBody body)
    {
        if (receiver.Operation == CilOperation.LoadString)
        {
            return StringType;
        }
        if (receiver.Operation == CilOperation.NewObject)
        {
            return _calledMethods.Resolve(receiver) is { } constructor && Closed(constructor.DeclaringType, body) is
            { IsValueType: false, Shape: not CliTypeShape.SzArray } created
                ? created
                : null;
        }
        var type = receiver switch
        {
            { Operation: CilOperation.LoadField or CilOperation.LoadStaticField, Operand: CilOperand.FieldInstance field } =>
                field.Value.FieldType,
            { Operation: CilOperation.LoadField or CilOperation.LoadStaticField, Operand: CilOperand.Entity field } =>
                _fields.GetField(field.Key).SignatureType,
            { Operation: CilOperation.LoadLocal, Operand: CilOperand.Index local }
                when local.Value < body.LocalSignatureTypes.Length => body.LocalSignatureTypes[local.Value],
            { Operation: CilOperation.Call or CilOperation.CallVirtual } =>
                _calledMethods.Resolve(receiver)?.Signature.ReturnSignatureType,
            _ => null,
        };
        return type is null || Closed(type, body) is not { } closed || !IsSealedClass(closed) ? null : closed;
    }

    private bool IsSealedClass(CliTypeIdentity type) =>
        type.Equals(StringType) ||
        type is { IsValueType: false, Shape: CliTypeShape.Named or CliTypeShape.GenericInstantiation } &&
        _typeDefinitions.ResolveTypeIdentity(type) is { IsSealed: true, IsInterface: false, IsValueType: false };

    private static CliTypeIdentity? Closed(CliTypeIdentity type, CilMethodBody body)
    {
        var closed = type.Substitute(
            body.MethodInstance?.DeclaringType.TypeArguments ?? ImmutableArray<CliTypeIdentity>.Empty,
            body.MethodInstance?.MethodArguments ?? ImmutableArray<CliTypeIdentity>.Empty);
        return closed.ContainsGenericParameters ? null : closed;
    }
}
