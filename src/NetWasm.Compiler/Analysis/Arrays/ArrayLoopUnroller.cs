using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.Analysis.Loops;
using NetWasm.Compiler.ControlFlow;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Analysis.Arrays;

internal sealed class ArrayLoopUnroller(
    IControlFlowGraphBuilder graphs,
    ITypedStackValidator stacks,
    IArrayLoopUnrollingPlanner planner,
    bool enabled) : IArrayLoopUnroller
{
    private readonly IControlFlowGraphBuilder _graphs = graphs ?? throw new ArgumentNullException(nameof(graphs));
    private readonly ITypedStackValidator _stacks = stacks ?? throw new ArgumentNullException(nameof(stacks));
    private readonly IArrayLoopUnrollingPlanner _planner = planner ?? throw new ArgumentNullException(nameof(planner));

    public CilMethodBody Rewrite(CilMethodBody body)
    {
        ArgumentNullException.ThrowIfNull(body);
        if (!enabled) return body;
        // A loop bounded by a length local, as a span's, has no array accesses
        // and needs no proof; an array loop's accesses need one.
        var proof = body.ArrayBoundsProof;
        if (proof is not null && !proof.Describes(body.Instructions, body.Locals, body.LocalSignatureTypes)) return body;
        var plans = _planner.Plan(_stacks.Validate(_graphs.Build(body)))
            .Where(plan => proof is not null || plan.ArrayLocal < 0)
            .ToImmutableArray();
        if (plans.IsEmpty) return body;
        proof ??= new(body.Instructions, body.Locals, body.LocalSignatureTypes, []);

        var code = body.Instructions.ToBuilder();
        var locals = body.Locals.ToBuilder();
        var signatures = body.LocalSignatureTypes.ToBuilder();
        var accesses = proof.AccessOffsets.ToBuilder();
        var indexOffsets = proof.IndexOffsets.ToBuilder();
        var redirects = new Dictionary<int, int>();
        var next = body.Instructions[^1].NextOffset;
        foreach (var plan in plans)
        {
            var entry = next;
            foreach (var branch in plan.EntryBranches) redirects.Add(branch, entry);
            var length = plan.LengthLocal;
            if (plan.ArrayLocal >= 0)
            {
                length = locals.Count;
                locals.Add(CliValueKind.I4);
                signatures.Add(CliTypeIdentity.FromStackKind(CliValueKind.I4));
                // The original first guard evaluates length even on a zero-trip
                // path. Keep that throwing evaluation before the fast loop, then
                // reuse its immutable bound without repeating the null check.
                Append(CilOperation.LoadLocal, new CilOperand.Index(plan.ArrayLocal));
                Append(CilOperation.LoadArrayLength, new CilOperand.None());
                Append(CilOperation.ConvertInt32, new CilOperand.None());
                Append(CilOperation.StoreLocal, new CilOperand.Index(length));
            }
            // A fold of the body chains the group's iterations through its
            // accumulator. The second and later iterations fold into their own
            // accumulators, which join it on the way out of the fast loop. No
            // handler can see them part way: array loops are planned only in
            // methods without protected regions.
            var payload = plan.Body[..^4];
            var folds = IntegerFolds.Find(payload, body.Instructions, body.Locals, local =>
                    local != plan.IndexLocal && local != plan.ArrayLocal && local != plan.LengthLocal)
                .Select(fold => (Statement: fold, Partials: Enumerable.Range(1, plan.Factor - 1).Select(_ =>
                {
                    locals.Add(fold.Kind);
                    signatures.Add(CliTypeIdentity.FromStackKind(fold.Kind));
                    return locals.Count - 1;
                }).ToArray()))
                .ToArray();
            foreach (var (fold, partials) in folds)
            {
                foreach (var partial in partials)
                {
                    var identity = fold.Identity();
                    Append(identity.Operation, identity.Operand);
                    Append(CilOperation.StoreLocal, new CilOperand.Index(partial));
                }
            }
            var fastGuard = next;
            Append(CilOperation.LoadLocal, new CilOperand.Index(plan.IndexLocal));
            Append(CilOperation.LoadLocal, new CilOperand.Index(length));
            Append(CilOperation.LoadInt32, new CilOperand.ConstantI4(plan.Factor - 1));
            Append(CilOperation.Subtract, new CilOperand.None());
            var exit = code.Count;
            Append(CilOperation.BranchIfGreaterThanOrEqualSigned, new CilOperand.BranchTarget(plan.GuardOffset));
            for (var iteration = 0; iteration < plan.Factor; iteration++)
            {
                // The induction proof identifies the final four instructions
                // as index = index + 1. Keep one base for the group instead.
                foreach (var instruction in payload)
                {
                    if (proof.AccessOffsets.Contains(instruction.Offset))
                    {
                        accesses.Add(next);
                        indexOffsets.Add(next, new(plan.IndexLocal, iteration));
                    }
                    var operand = iteration != 0 && instruction is
                    { Operation: CilOperation.LoadLocal or CilOperation.StoreLocal, Operand: CilOperand.Index { Value: var used } } &&
                        folds.FirstOrDefault(fold => fold.Statement.Accumulator == used) is { Partials: { } accumulators }
                        ? new CilOperand.Index(accumulators[iteration - 1])
                        : instruction.Operand;
                    code.Add(instruction with
                    {
                        Offset = next,
                        NextOffset = checked(next + 1),
                        Operand = operand,
                        OriginalOffset = instruction.SourceOffset,
                    });
                    next++;
                    if (iteration != 0 && instruction.Operation == CilOperation.LoadLocal &&
                        instruction.Operand is CilOperand.Index index && index.Value == plan.IndexLocal)
                    {
                        Append(CilOperation.LoadInt32, new CilOperand.ConstantI4(iteration));
                        Append(CilOperation.Add, new CilOperand.None());
                    }
                }
            }
            Append(CilOperation.LoadLocal, new CilOperand.Index(plan.IndexLocal));
            Append(CilOperation.LoadInt32, new CilOperand.ConstantI4(plan.Factor));
            Append(CilOperation.Add, new CilOperand.None());
            Append(CilOperation.StoreLocal, new CilOperand.Index(plan.IndexLocal));
            Append(CilOperation.Branch, new CilOperand.BranchTarget(fastGuard));
            if (folds.Length != 0)
            {
                code[exit] = code[exit] with { Operand = new CilOperand.BranchTarget(next) };
                foreach (var (fold, partials) in folds)
                {
                    Append(CilOperation.LoadLocal, new CilOperand.Index(fold.Accumulator));
                    foreach (var partial in partials)
                    {
                        Append(CilOperation.LoadLocal, new CilOperand.Index(partial));
                        Append(fold.Operation, new CilOperand.None());
                    }
                    Append(CilOperation.StoreLocal, new CilOperand.Index(fold.Accumulator));
                }
                Append(CilOperation.Branch, new CilOperand.BranchTarget(plan.GuardOffset));
            }

            void Append(CilOperation operation, CilOperand operand)
            {
                code.Add(new(next, checked(next + 1), operation, operand) { OriginalOffset = plan.GuardOffset });
                next++;
            }
        }
        for (var index = 0; index < body.Instructions.Length; index++)
        {
            if (redirects.TryGetValue(code[index].Offset, out var target))
                code[index] = code[index] with { Operand = new CilOperand.BranchTarget(target) };
        }
        var result = body with
        {
            Instructions = code.ToImmutable(),
            MaxStack = Math.Max(3, checked(body.MaxStack + 1)),
            Locals = locals.ToImmutable(),
            LocalSignatureTypes = signatures.ToImmutable(),
            ArrayBoundsProof = null,
        };
        _stacks.Validate(_graphs.Build(result));
        return result with
        {
            ArrayBoundsProof = new(result.Instructions, result.Locals, result.LocalSignatureTypes, accesses.ToImmutable())
            {
                IndexOffsets = indexOffsets.ToImmutable(),
            },
        };
    }
}
