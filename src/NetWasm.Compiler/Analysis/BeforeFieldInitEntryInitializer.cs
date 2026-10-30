using System;
using System.Collections.Generic;
using System.Linq;
using NetWasm.Compiler.ControlFlow;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Core.Types;

namespace NetWasm.Compiler.Analysis;

internal interface IBeforeFieldInitEntryInitializer
{
    CilMethodBody Rewrite(CilMethodBody body);
}

// A BeforeFieldInit type may initialize at or before the first access to its
// static fields (ECMA-335 I.8.9.5), and CoreCLR initializes it when it compiles
// a method that accesses them. A method that accesses such a field on every
// path to a return therefore checks the type once, with an access at its
// entry, and the static field initialization classifier treats its other
// accesses to that type as initialized, so a loop over a static field no longer
// checks, and keeps references in a root frame across that check, on every
// iteration. A method with exception regions keeps its checks where they are,
// so an initializer that fails still fails inside the region that handles it.
internal sealed class BeforeFieldInitEntryInitializer(
    IControlFlowGraphBuilder graphs,
    IControlFlowDominanceAnalyzer dominance,
    ITypeRepository types,
    IMethodRepository methods,
    IFieldRepository fields,
    bool enabled) : IBeforeFieldInitEntryInitializer
{
    private readonly IControlFlowGraphBuilder _graphs = graphs ?? throw new ArgumentNullException(nameof(graphs));
    private readonly IControlFlowDominanceAnalyzer _dominance = dominance ??
        throw new ArgumentNullException(nameof(dominance));
    private readonly ITypeRepository _types = types ?? throw new ArgumentNullException(nameof(types));
    private readonly IMethodRepository _methods = methods ?? throw new ArgumentNullException(nameof(methods));
    private readonly IFieldRepository _fields = fields ?? throw new ArgumentNullException(nameof(fields));

    public CilMethodBody Rewrite(CilMethodBody body)
    {
        ArgumentNullException.ThrowIfNull(body);
        if (!enabled || !body.ExceptionRegions.IsEmpty || body.Method.Name == ".cctor") return body;
        var initialized = StaticFieldInitializationClassifier.EntryAccesses(body.Instructions)
            .Select(entry => StaticFieldInitializationClassifier.Owner(entry, _fields))
            .ToArray();
        var candidates = body.Instructions
            .Where(StaticFieldInitializationClassifier.IsStaticFieldAccess)
            .Select(access => (Access: access, Owner: StaticFieldInitializationClassifier.Owner(access, _fields)))
            .Where(candidate => !initialized.Any(owner => owner.Initializes(candidate.Owner)) &&
                InitializesOnAccess(candidate.Owner.Type))
            .ToArray();
        if (candidates.Length == 0) return body;

        var graph = _graphs.Build(body);
        var blocks = graph.Blocks
            .SelectMany(block => block.Instructions.Select(instruction => (instruction.Offset, block.Index)))
            .ToDictionary(pair => pair.Offset, pair => pair.Index);
        var dominators = _dominance.Analyze(graph, graph.Entry.Index, graph.ReachableBlocks);
        var returns = graph.Blocks
            .Where(block => graph.ReachableBlocks.Contains(block.Index) && block.Terminator.Operation == CilOperation.Return)
            .Select(block => block.Index)
            .ToArray();
        // Only a type the method checks on every path to a return moves to the
        // entry, so no path checks a type it did not check before.
        var hoisted = new List<CilInstruction>();
        foreach (var candidate in candidates)
        {
            if (hoisted.Any(access => StaticFieldInitializationClassifier.Owner(access, _fields).Initializes(candidate.Owner)) ||
                !blocks.TryGetValue(candidate.Access.Offset, out var block) ||
                !returns.All(exit => dominators.TryGetValue(exit, out var exitDominators) && exitDominators.Contains(block)))
            {
                continue;
            }
            hoisted.Add(candidate.Access);
        }
        if (hoisted.Count == 0) return body;

        var source = body.Instructions[0].SourceOffset;
        var expanded = hoisted
            .SelectMany(access => new[]
            {
                new CilInstruction(-1, -1, CilOperation.LoadStaticFieldAddress, access.Operand) { OriginalOffset = source },
                new CilInstruction(-1, -1, CilOperation.Pop, new CilOperand.None()) { OriginalOffset = source },
            })
            .Concat(body.Instructions)
            .ToArray();
        var offsets = body.Instructions
            .Select((instruction, index) => (instruction.Offset, Index: index + hoisted.Count * 2))
            .ToDictionary(pair => pair.Offset, pair => pair.Index);
        // A branch to the first instruction re-enters the method's body after the entry checks.
        return body with
        {
            MaxStack = Math.Max(body.MaxStack, 1),
            Instructions = [.. expanded.Select((instruction, index) => instruction with
            {
                Offset = index,
                NextOffset = index + 1,
                OriginalOffset = instruction.SourceOffset,
                Operand = instruction.Operand switch
                {
                    CilOperand.BranchTarget target => new CilOperand.BranchTarget(offsets[target.Offset]),
                    CilOperand.SwitchTargets targets => new CilOperand.SwitchTargets(
                        [.. targets.Offsets.Select(offset => offsets[offset])]),
                    var operand => operand,
                },
            })],
        };
    }

    private bool InitializesOnAccess(EntityKey type) =>
        _types.GetTypeDefinition(type) is { IsBeforeFieldInit: true } definition &&
        definition.Methods.Any(method => _methods.GetMethod(method).Name == ".cctor");
}
