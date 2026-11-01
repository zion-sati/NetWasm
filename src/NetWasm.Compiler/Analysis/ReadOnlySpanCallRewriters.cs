using System;
using System.Collections.Generic;
using System.Linq;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Metadata;

namespace NetWasm.Compiler.Analysis;

internal sealed class CreateSpanCallRewriter(
    ICalledMethodResolver calledMethods,
    IReadOnlySpanDataResolver spanData) : IMethodRewriteRule
{
    public CilMethodBody Rewrite(CilMethodBody body)
    {
        ArgumentNullException.ThrowIfNull(body);
        var instructions = body.Instructions.ToArray();
        var entryOffsets = GetEntryOffsets(body);
        var changed = false;

        for (var index = 1; index < instructions.Length; index++)
        {
            var call = instructions[index];
            var token = instructions[index - 1];
            if (call.Operation != CilOperation.Call ||
                token.Operation != CilOperation.LoadFieldToken ||
                token.NextOffset != call.Offset ||
                entryOffsets.Contains(call.Offset) ||
                token.Operand is not CilOperand.FieldInstance fieldOperand)
            {
                continue;
            }

            var target = calledMethods.Resolve(call);
            var resolved = spanData.Resolve(target, fieldOperand.Value);
            if (resolved is null)
            {
                continue;
            }

            instructions[index - 1] = token with
            {
                Operation = CilOperation.Nop,
                Operand = new CilOperand.None(),
            };
            instructions[index] = call with
            {
                Operation = CilOperation.CreateReadOnlySpanFromRva,
                Operand = resolved,
            };
            changed = true;
        }

        return changed ? body with { Instructions = [.. instructions] } : body;
    }

    internal static HashSet<int> GetEntryOffsets(CilMethodBody body)
    {
        var offsets = new HashSet<int>();
        foreach (var instruction in body.Instructions)
        {
            if (instruction.Operand is CilOperand.BranchTarget branch)
            {
                offsets.Add(branch.Offset);
            }
        }
        foreach (var region in body.ExceptionRegions)
        {
            offsets.Add(region.TryOffset);
            offsets.Add(checked(region.TryOffset + region.TryLength));
            offsets.Add(region.HandlerOffset);
            offsets.Add(checked(region.HandlerOffset + region.HandlerLength));
            if (region.FilterOffset is int filterOffset)
            {
                offsets.Add(filterOffset);
            }
        }
        return offsets;
    }
}

// Roslyn creates a ReadOnlySpan<byte> over constant data as
// new ReadOnlySpan<byte>(&<PrivateImplementationDetails>.field, length) rather
// than through RuntimeHelpers.CreateSpan. Over the whole field it is the same
// view, so later passes read its length and elements as constants. Taking the
// address of a static field runs its type's static constructor, so the field's
// type must have none.
internal sealed class RvaPointerSpanRewriter(
    ICalledMethodResolver calledMethods,
    IFieldRepository fields,
    ITypeIdentityResolver typeIdentities,
    ITypeDefinitionResolver typeDefinitions,
    IMethodRepository methods) : IMethodRewriteRule
{
    public CilMethodBody Rewrite(CilMethodBody body)
    {
        ArgumentNullException.ThrowIfNull(body);
        var instructions = body.Instructions.ToArray();
        var entryOffsets = CreateSpanCallRewriter.GetEntryOffsets(body);
        var changed = false;

        for (var index = 2; index < instructions.Length; index++)
        {
            if (instructions[index - 2] is not { Operation: CilOperation.LoadStaticFieldAddress } address ||
                instructions[index - 1] is not
                {
                    Operation: CilOperation.LoadInt32,
                    Operand: CilOperand.ConstantI4 length,
                } count ||
                instructions[index] is not { Operation: CilOperation.NewObject } construction ||
                entryOffsets.Contains(count.Offset) ||
                entryOffsets.Contains(construction.Offset) ||
                Field(address.Operand) is not { } field ||
                HasStaticConstructor(field.DeclaringType) ||
                View(calledMethods.Resolve(construction), field, length.Value) is not { } view)
            {
                continue;
            }

            instructions[index - 2] = address with { Operation = CilOperation.Nop, Operand = new CilOperand.None() };
            instructions[index - 1] = count with { Operation = CilOperation.Nop, Operand = new CilOperand.None() };
            instructions[index] = construction with
            {
                Operation = CilOperation.CreateReadOnlySpanFromRva,
                Operand = view,
            };
            changed = true;
        }

        return changed ? body with { Instructions = [.. instructions] } : body;
    }

    // The pointer constructor over the whole of a read-only initialized field.
    private static CilOperand.ReadOnlySpanData? View(MethodInstanceModel? constructor, FieldInstanceModel field, int length)
    {
        if (constructor is not { Definition: { Name: ".ctor", IsStatic: false } } ||
            !ReadOnlySpanDataResolver.TryGetReadOnlySpanElement(constructor.DeclaringType, out var elementType) ||
            elementType.ContainsGenericParameters ||
            constructor.Signature.ParameterSignatureTypes is not
                [{ Shape: CliTypeShape.UnmanagedPointer }, { StackKind: CliValueKind.I4 }] ||
            !ReadOnlySpanDataResolver.TryGetPrimitiveSize(elementType, out var elementSize) ||
            !field.Definition.IsStatic ||
            !field.Definition.IsInitOnly ||
            field.Definition.InitialData.IsDefaultOrEmpty ||
            length <= 0 ||
            (long)length * elementSize != field.Definition.InitialData.Length)
        {
            return null;
        }

        return new(field, constructor.DeclaringType, elementType, length);
    }

    private FieldInstanceModel? Field(CilOperand operand) => operand switch
    {
        CilOperand.FieldInstance instance => instance.Value,
        CilOperand.Entity { Key: var key } when fields.GetField(key) is var definition =>
            new(definition, typeIdentities.GetTypeIdentity(definition.DeclaringType), definition.SignatureType),
        _ => null,
    };

    private bool HasStaticConstructor(CliTypeIdentity type) =>
        typeDefinitions.ResolveTypeIdentity(type).Methods.Any(method => methods.GetMethod(method).Name == ".cctor");
}
