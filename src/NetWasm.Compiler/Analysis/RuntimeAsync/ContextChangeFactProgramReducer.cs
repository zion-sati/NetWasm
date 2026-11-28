// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.Analysis.Constants;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Metadata;

namespace NetWasm.Compiler.Analysis.RuntimeAsync;

// RuntimeAsync.ContextsMayChange answers whether the program can change an ambient context: true as written, false over
// the whole program once no reachable code writes AmbientContextChanges.Made. Each call of it then loads that answer, and
// the code it decides folds: with false a direct await calls its callee's separate body with no request, and an entry
// runs a direct call's body with no catch (RuntimeAsyncDirectSiteEmitter, RuntimeAsyncEntryBuilder); with true those
// paths go and the request and the catch stay. It needs the program to have stopped growing, as any fact about every
// write does, so it runs with the optimizations, after the reductions that reach new code.
internal sealed class ContextChangeFactProgramReducer(
    IStaticFieldWriteCollector writes,
    ITypeFinder types,
    IFieldRepository fields,
    IMethodRepository methods,
    IMethodSpecializer rewrites) : IReachableProgramReducer
{
    private readonly IStaticFieldWriteCollector _writes = writes ?? throw new ArgumentNullException(nameof(writes));
    private readonly ITypeFinder _types = types ?? throw new ArgumentNullException(nameof(types));
    private readonly IFieldRepository _fields = fields ?? throw new ArgumentNullException(nameof(fields));
    private readonly IMethodRepository _methods = methods ?? throw new ArgumentNullException(nameof(methods));
    private readonly IMethodSpecializer _rewrites = rewrites ?? throw new ArgumentNullException(nameof(rewrites));
    private EntityKey? _query;

    public ImmutableDictionary<string, CilMethodBody> Reduce(ReachableProgram program, ReachabilityRoots roots)
    {
        ArgumentNullException.ThrowIfNull(program);
        ArgumentNullException.ThrowIfNull(roots);
        var bodies = program.Methods.Values.Concat(program.ConstructedMethods.Values)
            .DistinctBy(body => body.Method.CanonicalName, StringComparer.Ordinal)
            .ToArray();
        // A program asks only through the lowering's code, so one that asks has the core library's members.
        var asking = bodies.Where(body => body.Body.Instructions.Any(Asks)).ToArray();
        if (asking.Length == 0) return ImmutableDictionary<string, CilMethodBody>.Empty;
        var answer = _writes.Collect(program, roots).Contains(Made()) ? 1 : 0;
        var result = ImmutableDictionary.CreateBuilder<string, CilMethodBody>(StringComparer.Ordinal);
        foreach (var body in asking)
        {
            var instructions = body.Body.Instructions;
            var replacements = instructions.Select(instruction => new List<CilInstruction>
            {
                Asks(instruction)
                    ? instruction with { Operation = CilOperation.LoadInt32, Operand = new CilOperand.ConstantI4(answer) }
                    : instruction,
            }).ToArray();
            result.Add(body.Method.CanonicalName, _rewrites.Rewrite(RenumberedInstructions.Splice(body.Body, replacements)));
        }
        return result.ToImmutable();
    }

    private EntityKey Made() => _types.FindType("System.Threading.AmbientContextChanges").Fields.Select(_fields.GetField)
        .Single(field => field.Name == "Made" && field.IsStatic).Key;

    private EntityKey Query() => _query ??= _types.FindType("System.Runtime.CompilerServices.RuntimeAsync").Methods
        .Select(_methods.GetMethod).Single(method => method.Name == "ContextsMayChange" && method.IsStatic).Key;

    private bool Asks(CilInstruction instruction) =>
        instruction.Operation == CilOperation.Call &&
        instruction.Operand is CilOperand.MethodInstance { Value.Definition: { Name: "ContextsMayChange", IsStatic: true } method } &&
        method.Key == Query();
}
