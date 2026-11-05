using System;
using System.Collections.Generic;
using System.Linq;
using NetWasm.Compiler;
using NetWasm.Compiler.ControlFlow;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.GarbageCollection;
using NetWasm.Compiler.Metadata;

namespace NetWasm.Compiler.Validation;

internal sealed class RootMapInvariantValidator(
    ICompilerInvariantExceptionFactory exceptions,
    IRootDecisionClassifierFactory rootDecisions) : IRootMapInvariantValidator
{
    private readonly IRootDecisionClassifierFactory _rootDecisions =
        rootDecisions ?? throw new ArgumentNullException(nameof(rootDecisions));

    public void Validate(
        ITypeRepository types,
        IFieldRepository fields,
        IMethodRepository methods,
        ReachableProgram program,
        ITypeClassifier typeClassifier,
        IReadOnlySet<string>? restored = null)
    {
        ArgumentNullException.ThrowIfNull(types);
        ArgumentNullException.ThrowIfNull(fields);
        ArgumentNullException.ThrowIfNull(methods);
        ArgumentNullException.ThrowIfNull(program);
        ArgumentNullException.ThrowIfNull(typeClassifier);
        if (!program.RootMaps.Keys.ToHashSet().SetEquals(program.Methods.Keys))
        {
            throw exceptions.Create(
                "direct root-map keys do not match reachable methods");
        }
        if (!program.ConstructedRootMaps.Keys.ToHashSet(StringComparer.Ordinal)
                .SetEquals(program.ConstructedMethods.Keys))
        {
            throw exceptions.Create(
                "constructed root-map keys do not match reachable methods");
        }
        // Each method is checked on its own; the first failing method in order is reported.
        // Root maps taken from compiled units were checked when they were analysed.
        var checks = new List<(ManagedMethodBody Method, MethodRootMap Roots)>(
            program.Methods.Count + program.ConstructedMethods.Count);
        foreach (var pair in program.Methods)
        {
            if (restored?.Contains(pair.Value.Method.CanonicalName) != true)
                checks.Add((pair.Value, program.RootMaps[pair.Key]));
        }
        foreach (var pair in program.ConstructedMethods)
        {
            if (restored?.Contains(pair.Key) != true)
                checks.Add((pair.Value, program.ConstructedRootMaps[pair.Key]));
        }
        OrderedParallel.For(checks.Count, index => ValidateMethod(
            types,
            fields,
            methods,
            program,
            checks[index].Method,
            checks[index].Roots,
            typeClassifier));
    }

    private void ValidateMethod(
        ITypeRepository types,
        IFieldRepository fields,
        IMethodRepository methods,
        ReachableProgram program,
        ManagedMethodBody method,
        MethodRootMap roots,
        ITypeClassifier typeClassifier)
    {
        var body = method.Body;
        var displayName = method.Method.CanonicalName;
        if (roots.Method != method.Method.Definition.Key)
        {
            throw exceptions.Create(
                "root map method identity does not match its body",
                displayName);
        }
        var slots = roots.Slots.Values.Distinct().Order().ToArray();
        if (roots.Slots.Values.Any(slot => slot < 0) ||
            !slots.SequenceEqual(Enumerable.Range(0, slots.Length)))
        {
            throw exceptions.Create(
                "occupied root slots are not nonnegative and contiguous",
                displayName);
        }
        var instructions = body.Instructions.ToDictionary(
            instruction => instruction.Offset);
        foreach (var safepoint in roots.Safepoints.Values)
        {
            if (!instructions.ContainsKey(safepoint.IlOffset))
            {
                throw exceptions.Create(
                    "root-map safepoint is not an instruction boundary",
                    displayName,
                    safepoint.IlOffset);
            }
            ValidateSources(safepoint.Roots, "ordinary");
            ValidateSources(safepoint.ConstructorCallRoots, "constructor");
            if (!safepoint.ConstructorCallRoots.IsEmpty &&
                safepoint.Roots.Any(source =>
                    !safepoint.ConstructorCallRoots.Contains(source)))
            {
                throw exceptions.Create(
                    "constructor-call roots do not preserve every ordinary live root",
                    displayName,
                    safepoint.IlOffset);
            }

            void ValidateSources(
                IReadOnlyCollection<RootSource> sources,
                string kind)
            {
                if (sources.Count != sources.Distinct().Count())
                {
                    throw exceptions.Create(
                        $"{kind} safepoint roots are duplicated",
                        displayName,
                        safepoint.IlOffset);
                }
                if (sources.Any(source => !roots.Slots.ContainsKey(source)))
                {
                    throw exceptions.Create(
                        $"{kind} safepoint root has no assigned slot",
                        displayName,
                        safepoint.IlOffset);
                }
                if (sources.Select(source => roots.Slots[source]).Distinct().Count() !=
                    sources.Count)
                {
                    throw exceptions.Create(
                        $"{kind} safepoint roots share an assigned slot",
                        displayName,
                        safepoint.IlOffset);
                }
            }
        }
        var rootDecisions = _rootDecisions.Create(
            types,
            fields,
            methods,
            program.DispatchCallSites,
            typeClassifier);
        var graph = method.ControlFlow.Graph;
        foreach (var block in graph.Blocks.Where(block => graph.ReachableBlocks.Contains(block.Index)))
        {
            foreach (var instruction in block.Instructions.Where(instruction =>
                         rootDecisions.Decide(new(
                             method,
                             block.Index,
                             instruction,
                             program.AllocatingMethods,
                             program.ConstructedAllocatingMethods))))
            {
                if (!roots.Safepoints.ContainsKey(instruction.Offset))
                {
                    throw exceptions.Create(
                        $"safepoint operation {instruction.Operation} has no root-map decision",
                        displayName,
                        instruction.Offset);
                }
            }
        }
    }
}
