// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Analysis.Constants;

// X.GetType() where the program allocates one type only that x's static type admits names that type, as
// ExactObjectTypeRewriter names a sealed one: the receiver keeps its null check, and the Type object is the static one,
// with no lookup and no safepoint. StreamReader's `GetType() != typeof(StreamReader)` guard, in a program that derives
// nothing from StreamReader, then compares two constants. The receiver is `this`, an argument or a local, just before.
internal sealed class SoleAllocatedTypeProgramReducer(
    ITypeRelationshipClassifier relationships,
    IMethodSpecializer rewrites) : IReachableProgramReducer
{
    private readonly ITypeRelationshipClassifier _relationships = relationships ?? throw new ArgumentNullException(nameof(relationships));
    private readonly IMethodSpecializer _rewrites = rewrites ?? throw new ArgumentNullException(nameof(rewrites));

    public ImmutableDictionary<string, CilMethodBody> Reduce(ReachableProgram program, ReachabilityRoots roots)
    {
        ArgumentNullException.ThrowIfNull(program);
        var allocated = program.AllocatedTypes.Concat(program.UninitializedObjectTypes)
            .DistinctBy(type => type.CanonicalName, StringComparer.Ordinal).ToArray();
        var sole = new Dictionary<string, CliTypeIdentity?>(StringComparer.Ordinal);
        var result = ImmutableDictionary.CreateBuilder<string, CilMethodBody>(StringComparer.Ordinal);
        foreach (var method in program.Methods.Values.Concat(program.ConstructedMethods.Values)
            .DistinctBy(body => body.Method.CanonicalName, StringComparer.Ordinal))
        {
            var body = method.Body;
            var instructions = body.Instructions;
            ImmutableArray<CilInstruction>.Builder? rewritten = null;
            HashSet<int>? targets = null;
            for (var index = 1; index < instructions.Length; index++)
            {
                if (instructions[index] is not { Operation: CilOperation.GetObjectType, Operand: CilOperand.None } getType) continue;
                targets ??= BranchTargets(instructions);
                if (targets.Contains(getType.Offset) || StaticType(method.Method, body, instructions[index - 1]) is not { } type ||
                    type.IsValueType || type.ContainsGenericParameters ||
                    type.Shape is CliTypeShape.SzArray or CliTypeShape.ManagedByReference)
                {
                    continue;
                }
                if (!sole.TryGetValue(type.CanonicalName, out var exact))
                {
                    var admitted = allocated.Where(candidate => !candidate.IsValueType &&
                        _relationships.Classify(candidate, type).IsAssignmentCompatible).Take(2).ToArray();
                    // A shared array's GetType answers its element's array type, not the shared type's.
                    sole[type.CanonicalName] = exact = admitted.Length == 1 && !SharedArrayShapes.IsShared(admitted[0])
                        ? admitted[0]
                        : null;
                }
                if (exact is null) continue;
                rewritten ??= instructions.ToBuilder();
                rewritten[index] = getType with { Operand = new CilOperand.ExactType(exact) };
                FoldComparison(instructions, rewritten, index, exact, targets);
            }
            if (rewritten is not null)
            {
                result.Add(method.Method.CanonicalName, _rewrites.Rewrite(body with { Instructions = rewritten.ToImmutable() }));
            }
        }
        return result.ToImmutable();
    }

    // A comparison of the named type with typeof(U) right after it, `ldtoken U; MaterializeType; ceq` or the inlined
    // Type equality through two temporaries (`stloc a; stloc b; ldloc b; ldloc a; ceq`, each read only there): the
    // receiver keeps its check, the type is popped, and the comparison is the constant its answer is, which the
    // specializer's branch folding then takes.
    private static void FoldComparison(ImmutableArray<CilInstruction> instructions, ImmutableArray<CilInstruction>.Builder rewritten,
        int index, CliTypeIdentity exact, HashSet<int> targets)
    {
        var at = index + 1;
        if (at + 2 >= instructions.Length ||
            instructions[at] is not { Operation: CilOperation.LoadTypeToken, Operand: CilOperand.TypeIdentity { Value: var other } } ||
            instructions[at + 1].Operation != CilOperation.MaterializeType ||
            other.ContainsGenericParameters)
        {
            return;
        }
        var comparison = at + 2;
        if (instructions[comparison] is { Operation: CilOperation.StoreLocal, Operand: CilOperand.Index { Value: var first } } &&
            comparison + 4 < instructions.Length &&
            instructions[comparison + 1] is { Operation: CilOperation.StoreLocal, Operand: CilOperand.Index { Value: var second } } &&
            instructions[comparison + 2] is { Operation: CilOperation.LoadLocal, Operand: CilOperand.Index { Value: var secondRead } } &&
            instructions[comparison + 3] is { Operation: CilOperation.LoadLocal, Operand: CilOperand.Index { Value: var firstRead } } &&
            first != second && secondRead == second && firstRead == first &&
            References(instructions, first) == 2 && References(instructions, second) == 2)
        {
            comparison += 4;
        }
        if (instructions[comparison].Operation != CilOperation.CompareEqual) return;
        for (var inside = index + 1; inside <= comparison; inside++)
        {
            if (targets.Contains(instructions[inside].Offset)) return;
        }
        rewritten[at] = instructions[at] with { Operation = CilOperation.Pop, Operand = new CilOperand.None() };
        for (var gone = at + 1; gone < comparison; gone++)
        {
            rewritten[gone] = instructions[gone] with { Operation = CilOperation.Nop, Operand = new CilOperand.None() };
        }
        rewritten[comparison] = instructions[comparison] with
        {
            Operation = CilOperation.LoadInt32,
            Operand = new CilOperand.ConstantI4(other.CanonicalName == exact.CanonicalName ? 1 : 0),
        };
    }

    private static int References(ImmutableArray<CilInstruction> instructions, int local) =>
        instructions.Count(instruction => instruction is
        {
            Operation: CilOperation.LoadLocal or CilOperation.StoreLocal or CilOperation.LoadLocalAddress,
            Operand: CilOperand.Index { Value: var index },
        } && index == local);

    // The static type of the value the receiver pushes: `this` in an instance method, an argument or a local.
    private static CliTypeIdentity? StaticType(MethodInstanceModel method, CilMethodBody body, CilInstruction receiver)
    {
        switch (receiver)
        {
            case { Operation: CilOperation.LoadArgument, Operand: CilOperand.Index { Value: var argument } }:
                if (!method.Definition.IsStatic)
                {
                    if (argument == 0) return method.DeclaringType;
                    argument--;
                }
                var parameters = method.Signature.ParameterSignatureTypes;
                return (uint)argument < (uint)parameters.Length ? parameters[argument] : null;
            case { Operation: CilOperation.LoadLocal, Operand: CilOperand.Index { Value: var local } }
                when (uint)local < (uint)body.LocalSignatureTypes.Length:
                return body.LocalSignatureTypes[local];
            default:
                return null;
        }
    }

    private static HashSet<int> BranchTargets(ImmutableArray<CilInstruction> instructions)
    {
        var targets = new HashSet<int>();
        foreach (var instruction in instructions)
        {
            switch (instruction.Operand)
            {
                case CilOperand.BranchTarget branch:
                    targets.Add(branch.Offset);
                    break;
                case CilOperand.SwitchTargets cases:
                    targets.UnionWith(cases.Offsets);
                    break;
            }
        }
        return targets;
    }
}
