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
        ReachableProgram program)
    {
        ArgumentNullException.ThrowIfNull(types);
        ArgumentNullException.ThrowIfNull(fields);
        ArgumentNullException.ThrowIfNull(methods);
        ArgumentNullException.ThrowIfNull(program);
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
        foreach (var pair in program.Methods)
        {
            ValidateMethod(
                types,
                fields,
                methods,
                program,
                pair.Value,
                program.RootMaps[pair.Key]);
        }
        foreach (var pair in program.ConstructedMethods)
        {
            ValidateMethod(
                types,
                fields,
                methods,
                program,
                pair.Value,
                program.ConstructedRootMaps[pair.Key]);
        }
    }

    private void ValidateMethod(
        ITypeRepository types,
        IFieldRepository fields,
        IMethodRepository methods,
        ReachableProgram program,
        ManagedMethodBody method,
        MethodRootMap roots)
    {
        var body = method.Body;
        var displayName = method.Method.CanonicalName;
        if (roots.Method != method.Method.Definition.Key)
        {
            throw exceptions.Create(
                "root map method identity does not match its body",
                displayName);
        }
        var slots = roots.Slots.Values.Order().ToArray();
        if (!slots.SequenceEqual(Enumerable.Range(0, slots.Length)))
        {
            throw exceptions.Create(
                "root slots are not unique and contiguous",
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
                if (sources.Count != sources.Distinct().Count() ||
                    sources.Any(source => !roots.Slots.ContainsKey(source)))
                {
                    throw exceptions.Create(
                        $"{kind} safepoint roots are duplicated or have no assigned slot",
                        displayName,
                        safepoint.IlOffset);
                }
            }
        }
        var rootDecisions = _rootDecisions.Create(
            types,
            fields,
            methods,
            program.DispatchCallSites);
        foreach (var block in method.ControlFlow.Graph.Blocks)
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
