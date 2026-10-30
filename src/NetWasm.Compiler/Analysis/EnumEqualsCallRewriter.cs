using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Analysis;

// `value.Equals(other)` on an enum compiles to box, constrained and a callvirt
// of Object.Equals(object), so every comparison allocates the boxed argument,
// as EnumEqualityComparer<T>.Equals does for each Dictionary key or tuple
// field. The argument is a value of the receiver's own enum type, so
// Enum.Equals reduces to comparing the two underlying values: the argument
// waits in a local while the receiver's value is loaded through its address.
// CoreCLR compares enum values the same way, with an intrinsic.
internal sealed class EnumEqualsCallRewriter(
    ITypeDefinitionResolver typeDefinitions,
    ICalledMethodResolver calledMethods,
    ISymbolFormatter symbols,
    bool enabled) : IMethodRewriteRule
{
    private readonly ITypeDefinitionResolver _typeDefinitions = typeDefinitions ??
        throw new ArgumentNullException(nameof(typeDefinitions));
    private readonly ICalledMethodResolver _calledMethods = calledMethods ??
        throw new ArgumentNullException(nameof(calledMethods));
    private readonly ISymbolFormatter _symbols = symbols ?? throw new ArgumentNullException(nameof(symbols));

    public CilMethodBody Rewrite(CilMethodBody body)
    {
        ArgumentNullException.ThrowIfNull(body);
        if (!enabled) return body;
        HashSet<int>? entries = null;
        var sites = new List<(int Box, CliTypeIdentity Type)>();
        for (var index = 0; index + 2 < body.Instructions.Length; index++)
        {
            if (body.Instructions[index] is not { Operation: CilOperation.Box, Operand: CilOperand.TypeIdentity { Value: var type } } ||
                body.Instructions[index + 1] is not
                {
                    Operation: CilOperation.Constrained,
                    Operand: CilOperand.TypeIdentity { Value: var constrained },
                } ||
                !constrained.Equals(type) ||
                body.Instructions[index + 2].Operation != CilOperation.CallVirtual ||
                !IsEnum(type) ||
                !IsObjectEquals(body.Instructions[index + 2]))
            {
                continue;
            }
            entries ??= InstructionEntries.Of(body);
            if (entries.Contains(body.Instructions[index + 1].Offset) || entries.Contains(body.Instructions[index + 2].Offset))
            {
                continue;
            }
            sites.Add((index, type));
        }
        return sites.Count == 0 ? body : Splice(body, sites);
    }

    private bool IsEnum(CliTypeIdentity type) =>
        type.IsValueType && !type.ContainsGenericParameters &&
        type.Shape is CliTypeShape.Named or CliTypeShape.GenericInstantiation &&
        _typeDefinitions.ResolveTypeIdentity(type).IsEnum;

    private bool IsObjectEquals(CilInstruction call) =>
        _calledMethods.Resolve(call) is { Definition: { IsStatic: false, Name: "Equals" } definition, Signature.ParameterTypes.Length: 1 } &&
        _symbols.Format(definition.DeclaringType) is "System.Object" or "System.ValueType" or "System.Enum";

    // The box stores the argument in a new local, the prefix loads the
    // receiver's value, and the call compares it with the local.
    private static CilMethodBody Splice(CilMethodBody body, List<(int Box, CliTypeIdentity Type)> sites)
    {
        var locals = body.Locals.ToBuilder();
        var localTypes = body.LocalSignatureTypes.ToBuilder();
        var replaced = new Dictionary<int, ImmutableArray<(CilOperation Operation, CilOperand Operand)>>();
        foreach (var (box, type) in sites)
        {
            var argument = localTypes.Count;
            locals.Add(type.StackKind);
            localTypes.Add(type);
            replaced[box] = [(CilOperation.StoreLocal, new CilOperand.Index(argument))];
            replaced[box + 1] = [(CilOperation.LoadObject, new CilOperand.TypeIdentity(type))];
            replaced[box + 2] =
            [
                (CilOperation.LoadLocal, new CilOperand.Index(argument)),
                (CilOperation.CompareEqual, new CilOperand.None()),
            ];
        }

        var starts = new Dictionary<int, int>();
        var count = 0;
        for (var index = 0; index < body.Instructions.Length; index++)
        {
            starts.Add(body.Instructions[index].Offset, count);
            count += replaced.TryGetValue(index, out var replacement) ? replacement.Length : 1;
        }
        starts.Add(body.Instructions[^1].NextOffset, count);

        var rewritten = ImmutableArray.CreateBuilder<CilInstruction>(count);
        for (var index = 0; index < body.Instructions.Length; index++)
        {
            var instruction = body.Instructions[index];
            if (replaced.TryGetValue(index, out var replacement))
            {
                foreach (var (operation, operand) in replacement)
                {
                    Add(operation, operand, instruction.SourceOffset);
                }
            }
            else
            {
                Add(instruction.Operation, RenumberedInstructions.Operand(instruction.Operand, starts), instruction.SourceOffset);
            }
        }

        return body with
        {
            Instructions = rewritten.MoveToImmutable(),
            LocalSignatureTypes = localTypes.ToImmutable(),
            Locals = locals.ToImmutable(),
            ExceptionRegions = [.. body.ExceptionRegions.Select(region => RenumberedInstructions.Region(region, starts))],
        };

        void Add(CilOperation operation, CilOperand operand, int sourceOffset) =>
            rewritten.Add(new CilInstruction(rewritten.Count, rewritten.Count + 1, operation, operand)
            {
                OriginalOffset = sourceOffset,
            });
    }
}
