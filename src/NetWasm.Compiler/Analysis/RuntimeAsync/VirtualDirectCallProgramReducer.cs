// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.Analysis.Constants;
using NetWasm.Compiler.ControlFlow;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Metadata;

namespace NetWasm.Compiler.Analysis.RuntimeAsync;

// A lowered method's await of a virtual or interface call asks whichever override runs to take the call directly
// (RuntimeAsync.CallDirectVirtual, right before the call). A lowered override takes it; any other must decline it before
// it calls anything, or the first lowered method it calls for a task would take the request instead. Over the whole
// program, each such call's targets (its dispatch site's, or the method it binds) are known: every target that is not
// lowered gets RuntimeAsync.DeclineDirect as its first instruction, and a call with a target that cannot be given one (no
// body here, an intrinsic, an import, or a delegate's Invoke, which emission calls through to the delegate's target),
// or with no lowered target to take it, keeps a Normal call, its request replaced by a nop. Correctness, not an optimization: it runs in every preset. One
// scan of the program's instructions per round; a declined target or a dropped request is not rewritten again.
internal sealed class VirtualDirectCallProgramReducer(
    ITypeFinder types,
    IMethodRepository methods,
    IDelegateTypeRecognizer delegates,
    IRuntimeIntrinsicRegistry intrinsics) : IReachableProgramReducer
{
    private readonly ITypeFinder _types = types ?? throw new ArgumentNullException(nameof(types));
    private readonly IMethodRepository _methods = methods ?? throw new ArgumentNullException(nameof(methods));
    private readonly IDelegateTypeRecognizer _delegates = delegates ?? throw new ArgumentNullException(nameof(delegates));
    private readonly IRuntimeIntrinsicRegistry _intrinsics = intrinsics ?? throw new ArgumentNullException(nameof(intrinsics));
    private MethodInstanceModel? _decline;

    public ImmutableDictionary<string, CilMethodBody> Reduce(ReachableProgram program, ReachabilityRoots roots)
    {
        ArgumentNullException.ThrowIfNull(program);
        var bodies = program.Methods.Values.Concat(program.ConstructedMethods.Values)
            .DistinctBy(body => body.Method.CanonicalName, StringComparer.Ordinal)
            .ToDictionary(body => body.Method.CanonicalName, StringComparer.Ordinal);
        var declining = new Dictionary<string, ManagedMethodBody>(StringComparer.Ordinal);
        var dropped = new Dictionary<string, HashSet<int>>(StringComparer.Ordinal);
        foreach (var body in bodies.Values)
        {
            var instructions = body.Body.Instructions;
            for (var index = 0; index < instructions.Length; index++)
            {
                if (!RuntimeAsyncVirtualRequest.Is(instructions[index])) continue;
                var targets = index + 1 < instructions.Length
                    ? Targets(program, body, instructions[index + 1])?.ToArray()
                    : null;
                // A call no lowered override can take keeps no request: its targets need not decline it.
                var undeclinable = targets is null || !targets.Any(target => target.Definition.IsRuntimeAsync);
                var needed = new List<ManagedMethodBody>();
                foreach (var target in undeclinable ? [] : targets!)
                {
                    if (target.Definition.IsRuntimeAsync) continue;
                    if (!CanDecline(target) || !bodies.TryGetValue(target.CanonicalName, out var targetBody))
                    {
                        undeclinable = true;
                        break;
                    }
                    if (!Declines(targetBody.Body)) needed.Add(targetBody);
                }
                if (!undeclinable)
                {
                    foreach (var targetBody in needed) declining.TryAdd(targetBody.Method.CanonicalName, targetBody);
                    continue;
                }
                if (!dropped.TryGetValue(body.Method.CanonicalName, out var offsets))
                    dropped[body.Method.CanonicalName] = offsets = [];
                offsets.Add(index);
            }
        }
        if (declining.Count == 0 && dropped.Count == 0) return ImmutableDictionary<string, CilMethodBody>.Empty;

        var result = ImmutableDictionary.CreateBuilder<string, CilMethodBody>(StringComparer.Ordinal);
        foreach (var name in declining.Keys.Concat(dropped.Keys).Distinct(StringComparer.Ordinal))
        {
            var body = bodies[name].Body;
            if (dropped.TryGetValue(name, out var positions)) body = Downgrade(body, positions);
            if (declining.ContainsKey(name))
            {
                body = RenumberedInstructions.Splice(body,
                    [.. body.Instructions.Select(instruction => new List<CilInstruction> { instruction })],
                    [new CilInstruction(0, 1, CilOperation.Call, new CilOperand.MethodInstance(Decline()))]);
            }
            result[name] = body;
        }
        return result.ToImmutable();
    }

    // A call that keeps no request: the request becomes a nop, and the await the lowering wrote for both paths becomes
    // the await of the returned task (IsCompletedAt, SuspendOn, GetResultAt, through the task's address as the lowering
    // passes it). A body a reduction rewrote is not optimized again, so these stay calls. An await not found as the
    // lowering wrote it keeps the sequence for both paths, which serves a task too.
    private CilMethodBody Downgrade(CilMethodBody body, HashSet<int> requests)
    {
        var instructions = body.Instructions;
        var replacements = instructions.Select(instruction => new List<CilInstruction> { instruction }).ToArray();
        CilInstruction Nop(CilInstruction instruction) =>
            instruction with { Operation = CilOperation.Nop, Operand = new CilOperand.None() };
        CilInstruction Call(CilInstruction instruction, MethodInstanceModel method) =>
            instruction with { Operand = new CilOperand.MethodInstance(method) };
        // Each await's helpers come before the next request, so each search stops there: the searches cover disjoint
        // ranges of the body.
        var allRequests = Enumerable.Range(0, instructions.Length)
            .Where(index => RuntimeAsyncVirtualRequest.Is(instructions[index]))
            .ToArray();
        for (var position = 0; position < allRequests.Length; position++)
        {
            var request = allRequests[position];
            if (!requests.Contains(request)) continue;
            var limit = position + 1 < allRequests.Length ? allRequests[position + 1] : instructions.Length;
            replacements[request] = [Nop(instructions[request])];
            var done = Next(instructions, request, limit, "VirtualDone");
            var suspend = done < 0 ? -1 : Next(instructions, done, limit, "SuspendVirtual", "SuspendVirtualAnywhere");
            var resumed = suspend < 0 ? -1 : Next(instructions, suspend, limit, "VirtualResumed");
            var result = resumed < 0 ? -1 : Next(instructions, resumed, limit, "VirtualResult", "VirtualVoid");
            if (result < 0 ||
                instructions[done - 1] is not { Operation: CilOperation.LoadLocalAddress, Operand: CilOperand.Index temporary } ||
                instructions[resumed - 1] is not { Operation: CilOperation.LoadLocalAddress, Operand: CilOperand.Index resumedLocal } ||
                resumedLocal.Value != temporary.Value)
            {
                continue;
            }
            var awaitHelper = Helper(instructions[done]);
            replacements[done] = [Call(instructions[done], Sibling(awaitHelper, "IsCompletedAt"))];
            if (Helper(instructions[suspend]).Definition.Name == "SuspendVirtual")
                replacements[suspend] = [Call(instructions[suspend], Sibling(Helper(instructions[suspend]), "SuspendOn"))];
            replacements[resumed - 1] = [Nop(instructions[resumed - 1])];
            replacements[resumed] = [Nop(instructions[resumed])];
            replacements[result] =
            [
                instructions[result] with { Operation = CilOperation.LoadLocalAddress, Operand = temporary },
                Call(instructions[result], Sibling(awaitHelper, "GetResultAt")),
            ];
        }
        return RenumberedInstructions.Splice(body, replacements);
    }

    // The first call after `from` and before `limit` of a RuntimeAsync helper with one of the names, or -1.
    private static int Next(ImmutableArray<CilInstruction> instructions, int from, int limit, params string[] names)
    {
        for (var index = from + 1; index < limit; index++)
        {
            if (names.Any(name => RuntimeAsyncVirtualRequest.IsRuntimeAsyncCall(instructions[index], name))) return index;
        }
        return -1;
    }

    private static MethodInstanceModel Helper(CilInstruction call) => ((CilOperand.MethodInstance)call.Operand).Value;

    // The RuntimeAsync helper of the name that takes the same awaited type as the helper given, with its type arguments.
    private MethodInstanceModel Sibling(MethodInstanceModel helper, string name)
    {
        var awaited = DefinitionName(helper.Definition.Signature.ParameterSignatureTypes[0]);
        var runtime = _types.FindType(RuntimeAsyncVirtualRequest.RuntimeAsyncName);
        var method = runtime.Methods.Select(_methods.GetMethod).Single(candidate => candidate.Name == name &&
            candidate.GenericArity == helper.Definition.GenericArity &&
            candidate.Signature.ParameterSignatureTypes.Length > 0 &&
            DefinitionName(candidate.Signature.ParameterSignatureTypes[0]) == awaited);
        return new MethodInstanceModel(method, helper.DeclaringType, helper.MethodArguments,
            method.Signature.Substitute([], helper.MethodArguments));
    }

    private static string? DefinitionName(CliTypeIdentity type) => type.Shape switch
    {
        CliTypeShape.ManagedByReference => DefinitionName(type.ElementType!),
        CliTypeShape.GenericInstantiation => type.ElementType!.FullName,
        _ => type.FullName,
    };

    // The methods a call that follows a request may reach: its dispatch site's targets, or the method it binds; null when
    // emission calls through something else (a delegate's Invoke, an intrinsic).
    private IEnumerable<MethodInstanceModel>? Targets(ReachableProgram program, ManagedMethodBody caller, CilInstruction call)
    {
        if (call is not
            {
                Operation: CilOperation.Call or CilOperation.CallVirtual,
                Operand: CilOperand.MethodInstance { Value: var method },
            } ||
            _delegates.Recognize(method.DeclaringType) || !CanDecline(method))
        {
            return null;
        }
        if (program.DispatchCallSites.TryGetValue($"{caller.Method.CanonicalName}@{call.Offset:x8}", out var site))
            return site.Targets.IsDefault ? null : site.Targets.Select(target => target.Method);
        return [method];
    }

    // Whether a method's own body runs when it is called, so that its first instruction can decline the request.
    private bool CanDecline(MethodInstanceModel method) =>
        !_intrinsics.TryGetIntrinsic(method.Definition.Key, out _) && method.Definition.NativeImport is null &&
        method.Definition.JSImport is null && method.Definition.WitImport is null;

    private static bool Declines(CilMethodBody body) =>
        body.Instructions.Length > 0 && RuntimeAsyncVirtualRequest.IsRuntimeAsyncCall(body.Instructions[0], "DeclineDirect");

    private MethodInstanceModel Decline()
    {
        if (_decline is not null) return _decline;
        var runtime = _types.FindType(RuntimeAsyncVirtualRequest.RuntimeAsyncName);
        var method = runtime.Methods.Select(_methods.GetMethod).Single(candidate => candidate.Name == "DeclineDirect");
        return _decline = new MethodInstanceModel(method, CliTypeIdentity.FromDefinition(runtime), [], method.Signature);
    }
}
