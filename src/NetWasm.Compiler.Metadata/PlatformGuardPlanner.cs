// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Reflection.Emit;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Metadata;

// FalseCalls maps each call proven false to the arguments it consumes, which the decoder pops before loading false.
internal sealed record PlatformGuardPlan(
    ImmutableHashSet<int> LiveOffsets,
    ImmutableDictionary<int, int> FalseCalls,
    ImmutableDictionary<int, int> BranchTargets,
    ImmutableDictionary<int, int> NextOffsets);

internal interface IPlatformGuardPlanner
{
    PlatformGuardPlan? Plan(MetadataAssemblySnapshot source, byte[] bytes,
        ImmutableArray<ExceptionRegion> regions);
}

// Strategy: prove only Boolean tests of fixed platform facts, along paths no other live path joins (Debug IL's
// joined || and && tests included, once their other arms are proven dead). Unknown instructions and other live entries
// stop the proof; ordinary binding then reports unsupported requirements. No application code is evaluated.
internal sealed class PlatformGuardPlanner(IRawCilInstructionReader instructions) : IPlatformGuardPlanner
{
    private readonly IRawCilInstructionReader _instructions = instructions ??
        throw new ArgumentNullException(nameof(instructions));

    public PlatformGuardPlan? Plan(MetadataAssemblySnapshot source, byte[] bytes,
        ImmutableArray<ExceptionRegion> regions)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(bytes);
        ImmutableArray<RawCilInstruction> raw;
        try { raw = _instructions.Read(bytes); }
        catch (BadImageFormatException) { return null; } // The normal decoder owns malformed-CIL diagnostics.
        if (raw.IsEmpty) return null;
        var entries = raw.SelectMany(instruction => instruction.BranchTargets).ToHashSet();
        foreach (var region in regions)
        {
            entries.Add(region.HandlerOffset);
            if (region.Kind == ExceptionRegionKind.Filter) entries.Add(region.FilterOffset);
        }
        var falseCalls = ImmutableDictionary.CreateBuilder<int, int>();
        for (var index = 0; index < raw.Length; index++)
        {
            if (FalseCall(source, raw, index, bytes, entries) is { } arguments) falseCalls[raw[index].Offset] = arguments;
        }
        if (falseCalls.Count == 0) return null;

        // Each round proves what it can through joins that only the path it follows still reaches, given the
        // branches earlier rounds proved; a proof only removes paths, so earlier proofs stay true and the rounds end.
        var byOffset = raw.Select((instruction, index) => (instruction.Offset, Index: index))
            .ToDictionary(item => item.Offset, item => item.Index);
        var branches = new Dictionary<int, int>();
        var tries = TryNesting.Build(raw, regions);
        var live = Live(raw, byOffset, regions, tries, branches);
        while (live is not null)
        {
            var predecessors = Predecessors(raw, byOffset, regions, branches, live);
            var proven = branches.Count;
            foreach (var call in falseCalls.Keys.Order()) Prove(raw, byOffset, byOffset[call], bytes, predecessors, branches);
            if (branches.Count == proven) break;
            live = Live(raw, byOffset, regions, tries, branches);
        }
        if (live is null || branches.Count == 0) return null;
        return new(live, falseCalls.ToImmutable(), branches.ToImmutableDictionary(),
            raw.Select((instruction, index) => (instruction.Offset,
                Next: index + 1 < raw.Length ? raw[index + 1].Offset : bytes.Length))
                .ToImmutableDictionary(item => item.Offset, item => item.Next));
    }

    // The instructions the method's entry reaches when each proven branch goes only its proven way, with the handlers
    // and filters of the tries it reaches; null when a target is not an instruction, which the decoder rejects.
    // A live instruction reaches the handlers of the tries around it: with the tries nested, through its innermost try
    // and that try's parents up to the first already reached (whose parents are then reached too), so each try is
    // reached once; tries that overlap without nesting (invalid, rejected later) are checked per instruction.
    private static ImmutableHashSet<int>? Live(ImmutableArray<RawCilInstruction> raw, Dictionary<int, int> byOffset,
        ImmutableArray<ExceptionRegion> regions, TryNesting? tries, Dictionary<int, int> branches)
    {
        var live = ImmutableHashSet.CreateBuilder<int>();
        var pending = new Queue<int>();
        var reached = tries is null ? null : new bool[tries.Entries.Length];
        pending.Enqueue(raw[0].Offset);
        while (pending.Count != 0)
        {
            var offset = pending.Dequeue();
            if (!live.Add(offset)) continue;
            if (!byOffset.TryGetValue(offset, out var index)) return null;
            foreach (var successor in Successors(raw, index, branches)) pending.Enqueue(successor);
            if (tries is not null)
            {
                for (var group = tries.Innermost[index]; group >= 0 && !reached![group]; group = tries.Parents[group])
                {
                    reached[group] = true;
                    foreach (var entry in tries.Entries[group]) pending.Enqueue(entry);
                }
                continue;
            }
            foreach (var region in regions)
            {
                if (offset < region.TryOffset || offset >= region.TryOffset + region.TryLength) continue;
                pending.Enqueue(region.HandlerOffset);
                if (region.Kind == ExceptionRegionKind.Filter) pending.Enqueue(region.FilterOffset);
            }
        }
        return live.ToImmutable();
    }

    // The method's try ranges (clauses sharing one range grouped) as a ProtectedRangeNesting over its instructions,
    // with the handler and filter entries of each range's clauses. Null when two ranges overlap without nesting.
    private sealed record TryNesting(int[] Innermost, int[] Parents, int[][] Entries)
    {
        public static TryNesting? Build(ImmutableArray<RawCilInstruction> raw, ImmutableArray<ExceptionRegion> regions)
        {
            var groups = regions
                .GroupBy(region => (Start: region.TryOffset, End: region.TryOffset + region.TryLength))
                .Select(group => (group.Key, Entries: group
                    .SelectMany(region => region.Kind == ExceptionRegionKind.Filter
                        ? new[] { region.HandlerOffset, region.FilterOffset }
                        : new[] { region.HandlerOffset })
                    .ToArray()))
                .ToArray();
            var nesting = ProtectedRangeNesting.Build(
                [.. groups.Select(group => group.Key)],
                [.. raw.Select(instruction => instruction.Offset)]);
            return nesting is null
                ? null
                : new(nesting.Innermost, nesting.Parents, [.. groups.Select(group => group.Entries)]);
        }
    }

    // Where a live instruction goes: a proven branch its proven way, any other its targets and its next instruction.
    private static ImmutableArray<int> Successors(ImmutableArray<RawCilInstruction> raw, int index,
        Dictionary<int, int> branches)
    {
        var instruction = raw[index];
        if (branches.TryGetValue(instruction.Offset, out var selected)) return [selected];
        return instruction.OpCode.FlowControl is not (FlowControl.Branch or FlowControl.Return or FlowControl.Throw) &&
            index + 1 < raw.Length
                ? [.. instruction.BranchTargets, raw[index + 1].Offset]
                : instruction.BranchTargets;
    }

    // The live ways into each instruction; the method's entry and each handler and filter also have an outside one
    // (-1), so no proof runs into them.
    private static Dictionary<int, HashSet<int>> Predecessors(ImmutableArray<RawCilInstruction> raw,
        Dictionary<int, int> byOffset, ImmutableArray<ExceptionRegion> regions, Dictionary<int, int> branches,
        ImmutableHashSet<int> live)
    {
        var predecessors = new Dictionary<int, HashSet<int>>();
        void Add(int target, int from)
        {
            if (!predecessors.TryGetValue(target, out var set)) predecessors[target] = set = [];
            set.Add(from);
        }
        Add(raw[0].Offset, -1);
        foreach (var region in regions)
        {
            Add(region.HandlerOffset, -1);
            if (region.Kind == ExceptionRegionKind.Filter) Add(region.FilterOffset, -1);
        }
        foreach (var offset in live)
        {
            foreach (var successor in Successors(raw, byOffset[offset], branches)) Add(successor, offset);
        }
        return predecessors;
    }

    // The arguments a call that is false on every NetWasm host consumes, or null for any other instruction: a
    // capability NetWasm lacks (RuntimeFeature's), an OperatingSystem check for another operating system or one of
    // its version forms, and a platform test whose literal name or OSPlatform property, loaded just before it with no
    // branch between, is neither WASI nor the browser (NetWasmPlatformChecks).
    private static int? FalseCall(MetadataAssemblySnapshot source, ImmutableArray<RawCilInstruction> raw, int index,
        byte[] bytes, HashSet<int> entries)
    {
        if (StaticCoreLibCall(source, raw[index]) is not var (owner, name, signature) ||
            signature.ReturnType.CanonicalName != "primitive:bool")
            return null;
        var parameters = signature.ParameterTypes;
        var platform = index > 0 && !entries.Contains(raw[index].Offset) ? raw[index - 1] : (RawCilInstruction?)null;
        return (owner, name) switch
        {
            (NetWasmPlatformChecks.RuntimeFeature, _) when NetWasmPlatformChecks.IsNeverTrueRuntimeFeature(name) &&
                parameters.IsEmpty => 0,
            (NetWasmPlatformChecks.OperatingSystem, _) when NetWasmPlatformChecks.IsNeverTrueOperatingSystemCheck(name) &&
                parameters.All(parameter => parameter.CanonicalName == "primitive:i4") => parameters.Length,
            (NetWasmPlatformChecks.OperatingSystem, "IsOSPlatform")
                when parameters is [{ CanonicalName: "primitive:string" }] &&
                    Literal(source, platform, bytes) is { } literal && NetWasmPlatformChecks.IsNeverMatchingPlatformName(literal) => 1,
            (NetWasmPlatformChecks.RuntimeInformation, "IsOSPlatform")
                when parameters is [{ FullName: NetWasmPlatformChecks.OSPlatform }] && platform is { } getter &&
                    StaticCoreLibCall(source, getter) is (NetWasmPlatformChecks.OSPlatform, var property, { ParameterTypes.IsEmpty: true }) &&
                    NetWasmPlatformChecks.IsNeverMatchingOSPlatformGetter(property) => 1,
            _ => null,
        };
    }

    // A call of a static, non-generic method of NetWasm.CoreLib by member reference: its type, name and signature.
    // Other tables and malformed tokens stay with the ordinary decoder's metadata diagnostics.
    private static (string Owner, string Name, MethodSignature<CliTypeIdentity> Signature)? StaticCoreLibCall(
        MetadataAssemblySnapshot source, RawCilInstruction instruction)
    {
        if (instruction.OpCode != OpCodes.Call || instruction.Token is not { } token ||
            token >>> 24 != (int)TableIndex.MemberRef || (token & 0xffffff) == 0)
            return null;
        try
        {
            var reference = source.Reader.GetMemberReference(MetadataTokens.MemberReferenceHandle(token & 0xffffff));
            if (reference.Parent.Kind != HandleKind.TypeReference) return null;
            var provider = new SignatureTypeProvider(source.Identity, source.Reader, source.AssemblyIdentityAliases);
            // A referenced type is named, so it always has its assembly.
            var owner = provider.GetTypeFromReference(source.Reader, (TypeReferenceHandle)reference.Parent, 0x12);
            if (owner.Assembly.GetValueOrDefault().Name != "NetWasm.CoreLib") return null;
            var signature = reference.DecodeMethodSignature(provider, CliGenericContext.Empty);
            return !signature.Header.IsInstance && signature.GenericParameterCount == 0 &&
                signature.Header.CallingConvention == SignatureCallingConvention.Default
                    ? (owner.FullName!, source.Reader.GetString(reference.Name), signature)
                    : null;
        }
        catch (BadImageFormatException) { return null; }
    }

    // The string an ldstr loads; the raw reader keeps only metadata tokens, so its operand is read from the bytes.
    private static string? Literal(MetadataAssemblySnapshot source, RawCilInstruction? instruction, byte[] bytes)
    {
        if (instruction is not { } load || load.OpCode != OpCodes.Ldstr) return null;
        var token = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(load.Offset + 1));
        if (token >>> 24 != 0x70) return null;
        try { return source.Reader.GetUserString(MetadataTokens.UserStringHandle(token & 0xffffff)); }
        catch (BadImageFormatException) { return null; }
    }

    // Follows the false value from the call along the one path that reaches each next instruction, through Boolean
    // tests, locals, negation and unconditional branches, and proves each conditional branch it decides. It stops at
    // an instruction another live path also reaches, one it does not follow, or one it has seen. Every instruction it
    // follows is live, and the first Live, before any proof, found each live instruction's targets instructions.
    private static void Prove(ImmutableArray<RawCilInstruction> raw, Dictionary<int, int> byOffset, int start,
        byte[] bytes, Dictionary<int, HashSet<int>> predecessors, Dictionary<int, int> branches)
    {
        var stack = new Stack<int>();
        var locals = new Dictionary<int, int>();
        var seen = new HashSet<int>();
        stack.Push(0);
        var from = raw[start].Offset;
        for (var index = start + 1; index < raw.Length;)
        {
            var instruction = raw[index];
            if (!seen.Add(instruction.Offset) || !predecessors.TryGetValue(instruction.Offset, out var into) ||
                into.Count != 1 || !into.Contains(from))
                return;
            var code = instruction.OpCode;
            var name = code.Name!;
            var next = index + 1;
            if (code == OpCodes.Nop) { }
            else if (code == OpCodes.Ldc_I4_0 || code == OpCodes.Ldc_I4_1)
                stack.Push(code == OpCodes.Ldc_I4_0 ? 0 : 1);
            else if (name is "stloc" || name.StartsWith("stloc.", StringComparison.Ordinal))
            {
                if (stack.Count == 0) return;
                locals[LocalIndex(instruction, bytes)] = stack.Pop();
            }
            else if (name is "ldloc" || name.StartsWith("ldloc.", StringComparison.Ordinal))
            {
                if (!locals.TryGetValue(LocalIndex(instruction, bytes), out var value)) return;
                stack.Push(value);
            }
            else if (code == OpCodes.Ceq)
            {
                if (stack.Count < 2) return;
                var right = stack.Pop();
                stack.Push(stack.Pop() == right ? 1 : 0);
            }
            else if (code == OpCodes.Br || code == OpCodes.Br_S)
            {
                next = byOffset[instruction.BranchTargets.Single()];
            }
            else if (code == OpCodes.Brtrue || code == OpCodes.Brtrue_S || code == OpCodes.Brfalse || code == OpCodes.Brfalse_S)
            {
                if (stack.Count == 0 || index + 1 == raw.Length) return;
                var isTrue = stack.Pop() != 0;
                var onTrue = code == OpCodes.Brtrue || code == OpCodes.Brtrue_S;
                var selected = isTrue == onTrue ? instruction.BranchTargets.Single() : raw[index + 1].Offset;
                next = byOffset[selected];
                branches[instruction.Offset] = selected;
            }
            else return;
            from = instruction.Offset;
            index = next;
        }
    }

    private static int LocalIndex(RawCilInstruction instruction, byte[] bytes)
    {
        var name = instruction.OpCode.Name!;
        if (name[^1] is >= '0' and <= '3') return name[^1] - '0';
        var offset = instruction.Offset + instruction.OpCode.Size;
        return instruction.OpCode.OperandType == OperandType.ShortInlineVar
            ? bytes[offset] : BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(offset));
    }
}
