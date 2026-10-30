using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Core.Types;
using NetWasm.Compiler.Metadata;

namespace NetWasm.Compiler.Analysis.Allocations;

// `((ISpanFormattable)value).TryFormat(...)` on a type parameter compiles to
// box, castclass and callvirt. Once the parameter is a value type that declares
// the method the call reaches, the box only carries a copy of the value to that
// method: a local holds the copy instead, and the call becomes the constrained
// call it would be on the value, which binds to the method directly. .NET's JIT
// removes such boxes when it devirtualizes the call.
internal sealed class BoxedReceiverCallRewriter(
    ITypeDefinitionResolver typeDefinitions,
    IMethodRepository methods,
    ICalledMethodResolver calledMethods,
    INullableTypeResolver nullableTypes,
    ITypeRelationshipClassifier relationships,
    IConstrainedCallTargetResolver targets,
    IStackEffectResolver effects) : IMethodRewriteRule
{
    private readonly ITypeDefinitionResolver _typeDefinitions = typeDefinitions ?? throw new ArgumentNullException(nameof(typeDefinitions));
    private readonly IMethodRepository _methods = methods ?? throw new ArgumentNullException(nameof(methods));
    private readonly ICalledMethodResolver _calledMethods = calledMethods ?? throw new ArgumentNullException(nameof(calledMethods));
    private readonly INullableTypeResolver _nullableTypes = nullableTypes ?? throw new ArgumentNullException(nameof(nullableTypes));
    private readonly ITypeRelationshipClassifier _relationships = relationships ?? throw new ArgumentNullException(nameof(relationships));
    private readonly IConstrainedCallTargetResolver _targets = targets ?? throw new ArgumentNullException(nameof(targets));
    private readonly IStackEffectResolver _effects = effects ?? throw new ArgumentNullException(nameof(effects));

    public CilMethodBody Rewrite(CilMethodBody body)
    {
        ArgumentNullException.ThrowIfNull(body);
        var entries = InstructionEntries.Of(body);
        var sites = new List<Site>();
        for (var index = 0; index < body.Instructions.Length; index++)
        {
            if (body.Instructions[index] is { Operation: CilOperation.Box, Operand: CilOperand.TypeIdentity { Value: var type } } &&
                type.IsValueType && !type.ContainsGenericParameters && _nullableTypes.Resolve(type) is null &&
                type.Shape is CliTypeShape.Primitive or CliTypeShape.Named or CliTypeShape.GenericInstantiation &&
                FindReceiverCall(body.Instructions, entries, index, type) is { } site)
            {
                sites.Add(site);
            }
        }
        return sites.Count == 0 ? body : Splice(body, sites);
    }

    // The call that takes the box as its receiver, when nothing else in the
    // box's block reads it and the value type declares the method it reaches.
    private Site? FindReceiverCall(ImmutableArray<CilInstruction> instructions, HashSet<int> entries, int box, CliTypeIdentity type)
    {
        int? cast = null;
        // Values pushed above the box.
        var above = 0;
        for (var index = box + 1; index < instructions.Length; index++)
        {
            var instruction = instructions[index];
            if (entries.Contains(instruction.Offset))
            {
                return null;
            }
            if (above == 0 && cast is null && instruction is { Operation: CilOperation.CastClass, Operand: CilOperand.TypeIdentity { Value: var target } } &&
                !target.ContainsGenericParameters &&
                _relationships.Classify(type, _nullableTypes.Resolve(target) ?? target).IsAssignmentCompatible)
            {
                // The cast cannot fail.
                cast = index;
                continue;
            }
            if (_effects.Resolve(instruction) is not var (pops, pushes) || pops > above + 1)
            {
                return null;
            }
            if (pops == above + 1)
            {
                return instruction.Operation == CilOperation.CallVirtual &&
                    instructions[index - 1].Operation != CilOperation.Constrained && ReachesOwnMethod(type, instruction)
                    ? new Site(box, cast, index, type)
                    : null;
            }
            above += pushes - pops;
        }
        return null;
    }

    private bool ReachesOwnMethod(CliTypeIdentity type, CilInstruction call)
    {
        var declaration = call.Operand switch
        {
            CilOperand.Entity entity => _methods.GetMethod(entity.Key),
            CilOperand.MethodInstance instance => instance.Value.Definition,
            _ => null,
        };
        if (declaration is not { IsStatic: false, IsVirtual: true })
        {
            return false;
        }
        var concreteType = _typeDefinitions.ResolveTypeIdentity(type);
        if (concreteType.IsEnum)
        {
            // An enum declares no methods: System.Enum implements them on the box.
            return false;
        }
        var implementation = _targets.Resolve(new ConstrainedCallTargetRequest(
            type,
            concreteType,
            declaration,
            call.Operand is CilOperand.MethodInstance instanceCall ? instanceCall.Value.Signature : declaration.Signature,
            type.Shape == CliTypeShape.GenericInstantiation ? type.TypeArguments : [],
            call.Operand is CilOperand.MethodInstance methodCall ? methodCall.Value.MethodArguments : []));
        // An inherited or default interface method takes the object itself.
        return !implementation.IsStatic && implementation.DeclaringType == concreteType.Key;
    }

    // The box becomes a store to a new local and that local's address, the cast
    // goes, and the call gains the constrained prefix the receiver now needs.
    private static CilMethodBody Splice(CilMethodBody body, List<Site> sites)
    {
        var localTypes = body.LocalSignatureTypes.ToBuilder();
        var boxes = new Dictionary<int, (CliTypeIdentity Type, int Local)>();
        var casts = new HashSet<int>();
        var calls = new Dictionary<int, CliTypeIdentity>();
        foreach (var site in sites)
        {
            boxes.Add(site.Box, (site.Type, localTypes.Count));
            localTypes.Add(site.Type);
            if (site.Cast is { } cast)
            {
                casts.Add(cast);
            }
            calls.Add(site.Call, site.Type);
        }

        var starts = new Dictionary<int, int>();
        var count = 0;
        for (var index = 0; index < body.Instructions.Length; index++)
        {
            starts.Add(body.Instructions[index].Offset, count);
            count += boxes.ContainsKey(index) || calls.ContainsKey(index) ? 2 : 1;
        }
        starts.Add(body.Instructions[^1].NextOffset, count);

        var rewritten = ImmutableArray.CreateBuilder<CilInstruction>(count);
        for (var index = 0; index < body.Instructions.Length; index++)
        {
            var instruction = body.Instructions[index];
            if (boxes.TryGetValue(index, out var box))
            {
                Add(CilOperation.StoreLocal, new CilOperand.Index(box.Local), instruction.SourceOffset);
                Add(CilOperation.LoadLocalAddress, new CilOperand.Index(box.Local), instruction.SourceOffset);
            }
            else if (casts.Contains(index))
            {
                Add(CilOperation.Nop, new CilOperand.None(), instruction.SourceOffset);
            }
            else
            {
                if (calls.TryGetValue(index, out var receiver))
                {
                    Add(CilOperation.Constrained, new CilOperand.TypeIdentity(receiver), instruction.SourceOffset);
                }
                Add(instruction.Operation, RenumberedInstructions.Operand(instruction.Operand, starts), instruction.SourceOffset);
            }
        }

        return body with
        {
            Instructions = rewritten.MoveToImmutable(),
            LocalSignatureTypes = localTypes.ToImmutable(),
            Locals = [.. localTypes.Select(type => type.StackKind)],
            ExceptionRegions = [.. body.ExceptionRegions.Select(region => RenumberedInstructions.Region(region, starts))],
        };

        void Add(CilOperation operation, CilOperand operand, int sourceOffset)
        {
            rewritten.Add(new CilInstruction(rewritten.Count, rewritten.Count + 1, operation, operand)
            {
                OriginalOffset = sourceOffset,
            });
        }
    }

    private sealed record Site(int Box, int? Cast, int Call, CliTypeIdentity Type);
}
