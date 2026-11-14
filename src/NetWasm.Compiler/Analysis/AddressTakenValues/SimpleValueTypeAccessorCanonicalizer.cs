using System;
using System.Linq;
using NetWasm.Compiler.ComponentModel.Optimization;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Core.Types;
using NetWasm.Compiler.Metadata;

namespace NetWasm.Compiler.Analysis.AddressTakenValues;

internal sealed class SimpleValueTypeAccessorCanonicalizer(
    ICalleeMethodBodyReader methodBodies,
    ICalledMethodResolver calledMethods,
    IFieldRepository fields,
    AddressTakenValuePromotionPolicy policy,
    IMethodInitializationTriggerClassifier initializationTriggers) :
    ISimpleValueTypeAccessorCanonicalizer
{
    private readonly ICalleeMethodBodyReader _methodBodies = methodBodies ??
        throw new ArgumentNullException(nameof(methodBodies));
    private readonly ICalledMethodResolver _calledMethods = calledMethods ??
        throw new ArgumentNullException(nameof(calledMethods));
    private readonly IFieldRepository _fields = fields ??
        throw new ArgumentNullException(nameof(fields));
    private readonly IMethodInitializationTriggerClassifier _initializationTriggers = initializationTriggers ??
        throw new ArgumentNullException(nameof(initializationTriggers));
    private readonly AddressTakenValuePromotionPolicy _policy = policy switch
    {
        AddressTakenValuePromotionPolicy.Disabled or
        AddressTakenValuePromotionPolicy.Conservative or
        AddressTakenValuePromotionPolicy.References => policy,
        _ => throw new ArgumentOutOfRangeException(nameof(policy)),
    };

    public CilMethodBody Canonicalize(CilMethodBody body)
    {
        ArgumentNullException.ThrowIfNull(body);
        if (_policy == AddressTakenValuePromotionPolicy.Disabled)
        {
            return body;
        }

        var instructions = body.Instructions.ToArray();
        var changed = false;
        for (var index = 0; index < instructions.Length; index++)
        {
            var instruction = instructions[index];
            if (instruction.Operation != CilOperation.Call ||
                Resolve(instruction) is not { } replacement)
            {
                continue;
            }

            instructions[index] = instruction with
            {
                Operation = replacement.Operation,
                Operand = replacement.Field,
            };
            changed = true;
        }

        return changed ? body with { Instructions = [.. instructions] } : body;
    }

    private AccessorReplacement? Resolve(CilInstruction instruction)
    {
        var method = _calledMethods.Resolve(instruction);
        if (method is null ||
            method.Definition.IsStatic ||
            method.Definition.IsVirtual ||
            method.Definition.IsAbstract ||
            !method.Definition.HasManagedBody ||
            _initializationTriggers.Classify(method.Definition) ||
            !method.DeclaringType.IsValueType)
        {
            return null;
        }

        if (!_methodBodies.TryReadMethodBody(method, out var body) || !body.ExceptionRegions.IsEmpty)
        {
            return null;
        }

        var operations = body.Instructions
            .Where(candidate => candidate.Operation != CilOperation.Nop)
            .ToArray();
        return ResolveGetter(method, operations) ?? ResolveSetter(method, operations);
    }

    private AccessorReplacement? ResolveGetter(
        MethodInstanceModel method,
        CilInstruction[] operations)
    {
        if (!method.Signature.ParameterSignatureTypes.IsEmpty ||
            method.Signature.ReturnType == CliValueKind.Void ||
            operations is not
            [
            { Operation: CilOperation.LoadArgument, Operand: CilOperand.Index { Value: 0 } },
            { Operation: CilOperation.LoadField } field,
            { Operation: CilOperation.Return },
            ] ||
            !TryResolveField(method, field.Operand, out var fieldType) ||
            !method.Signature.ReturnSignatureType.Equals(fieldType))
        {
            return null;
        }

        return new(CilOperation.LoadField, field.Operand);
    }

    private AccessorReplacement? ResolveSetter(
        MethodInstanceModel method,
        CilInstruction[] operations)
    {
        if (method.Signature.ParameterSignatureTypes is not [var parameter] ||
            method.Signature.ReturnType != CliValueKind.Void ||
            operations is not
            [
            { Operation: CilOperation.LoadArgument, Operand: CilOperand.Index { Value: 0 } },
            { Operation: CilOperation.LoadArgument, Operand: CilOperand.Index { Value: 1 } },
            { Operation: CilOperation.StoreField } field,
            { Operation: CilOperation.Return },
            ] ||
            !TryResolveField(method, field.Operand, out var fieldType) ||
            !parameter.Equals(fieldType))
        {
            return null;
        }

        return new(CilOperation.StoreField, field.Operand);
    }

    private bool TryResolveField(
        MethodInstanceModel method,
        CilOperand operand,
        out CliTypeIdentity fieldType)
    {
        switch (operand)
        {
            case CilOperand.FieldInstance instance
                when !instance.Value.Definition.IsStatic &&
                     instance.Value.Definition.DeclaringType ==
                         method.Definition.DeclaringType &&
                     instance.Value.DeclaringType.Equals(method.DeclaringType):
                fieldType = instance.Value.FieldType;
                return true;
            case CilOperand.Entity entity
                when method.DeclaringType.Shape != CliTypeShape.GenericInstantiation:
                {
                    var field = _fields.GetField(entity.Key);
                    if (!field.IsStatic &&
                        field.DeclaringType == method.Definition.DeclaringType)
                    {
                        fieldType = field.SignatureType;
                        return true;
                    }

                    break;
                }
        }

        fieldType = null!;
        return false;
    }

    private sealed record AccessorReplacement(
        CilOperation Operation,
        CilOperand Field);
}
