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

internal sealed record PlatformGuardPlan(
    ImmutableHashSet<int> LiveOffsets,
    ImmutableHashSet<int> FalseCalls,
    ImmutableDictionary<int, int> BranchTargets,
    ImmutableDictionary<int, int> NextOffsets);

internal interface IPlatformGuardPlanner
{
    PlatformGuardPlan? Plan(MetadataAssemblySnapshot source, byte[] bytes,
        ImmutableArray<ExceptionRegion> regions);
}

// Strategy: prove only straight-line Boolean tests of fixed platform facts.
// Unknown instructions/alternate entries stop the proof; ordinary binding then
// reports unsupported requirements. No application code is evaluated.
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
        var falseCalls = ImmutableHashSet.CreateBuilder<int>();
        var branches = ImmutableDictionary.CreateBuilder<int, int>();
        for (var index = 0; index < raw.Length; index++)
        {
            if (!IsFalseCapability(source, raw[index])) continue;
            falseCalls.Add(raw[index].Offset);
            ProveBranch(raw, index, bytes, entries, branches);
        }
        if (branches.Count == 0) return null;

        var byOffset = raw.Select((instruction, index) => (instruction.Offset, Index: index))
            .ToDictionary(item => item.Offset, item => item.Index);
        var live = ImmutableHashSet.CreateBuilder<int>();
        var pending = new Queue<int>();
        pending.Enqueue(raw[0].Offset);
        while (pending.Count != 0)
        {
            var offset = pending.Dequeue();
            if (!live.Add(offset)) continue;
            if (!byOffset.TryGetValue(offset, out var index))
                return null; // Let the ordinary decoder/CFG reject an invalid target.
            var instruction = raw[index];
            if (branches.TryGetValue(offset, out var selected)) pending.Enqueue(selected);
            else
            {
                foreach (var target in instruction.BranchTargets) pending.Enqueue(target);
                if (instruction.OpCode.FlowControl is not (FlowControl.Branch or FlowControl.Return or FlowControl.Throw)
                    && index + 1 < raw.Length)
                    pending.Enqueue(raw[index + 1].Offset);
            }
            foreach (var region in regions)
            {
                if (offset < region.TryOffset || offset >= region.TryOffset + region.TryLength) continue;
                pending.Enqueue(region.HandlerOffset);
                if (region.Kind == ExceptionRegionKind.Filter) pending.Enqueue(region.FilterOffset);
            }
        }
        return new(live.ToImmutable(), falseCalls.ToImmutable(), branches.ToImmutable(),
            raw.Select((instruction, index) => (instruction.Offset,
                Next: index + 1 < raw.Length ? raw[index + 1].Offset : bytes.Length))
                .ToImmutableDictionary(item => item.Offset, item => item.Next));
    }

    private static bool IsFalseCapability(MetadataAssemblySnapshot source, RawCilInstruction instruction)
    {
        // Only a member reference can name a capability getter. Other tables and
        // malformed tokens stay with the ordinary decoder's metadata diagnostics.
        if (instruction.OpCode != OpCodes.Call || instruction.Token is not { } token ||
            token >>> 24 != (int)TableIndex.MemberRef || (token & 0xffffff) == 0)
            return false;
        try
        {
            var reference = source.Reader.GetMemberReference(MetadataTokens.MemberReferenceHandle(token & 0xffffff));
            var name = source.Reader.GetString(reference.Name);
            if (name is not ("get_IsDynamicCodeSupported" or "get_IsDynamicCodeCompiled" or "get_IsMultithreadingSupported")
                || reference.Parent.Kind != HandleKind.TypeReference)
                return false;
            var provider = new SignatureTypeProvider(source.Identity, source.Reader, source.AssemblyIdentityAliases);
            // A referenced type is named, so it always has its assembly.
            var owner = provider.GetTypeFromReference(source.Reader, (TypeReferenceHandle)reference.Parent, 0x12);
            if (owner.Assembly.GetValueOrDefault().Name != "NetWasm.CoreLib" ||
                owner.FullName != "System.Runtime.CompilerServices.RuntimeFeature") return false;
            var signature = reference.DecodeMethodSignature(provider, CliGenericContext.Empty);
            return !signature.Header.IsInstance && signature.GenericParameterCount == 0 &&
                signature.Header.CallingConvention == SignatureCallingConvention.Default &&
                signature.ParameterTypes.IsEmpty && signature.ReturnType.CanonicalName == "primitive:bool";
        }
        catch (BadImageFormatException) { return false; }
    }

    private static void ProveBranch(ImmutableArray<RawCilInstruction> raw, int start, byte[] bytes,
        HashSet<int> entries, ImmutableDictionary<int, int>.Builder branches)
    {
        var stack = new Stack<int>();
        var locals = new Dictionary<int, int>();
        stack.Push(0);
        for (var index = start + 1; index < raw.Length; index++)
        {
            var instruction = raw[index];
            if (entries.Contains(instruction.Offset)) return;
            var code = instruction.OpCode;
            var name = code.Name!;
            if (code == OpCodes.Nop) continue;
            if (code == OpCodes.Ldc_I4_0 || code == OpCodes.Ldc_I4_1)
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
            else if (code == OpCodes.Brtrue || code == OpCodes.Brtrue_S || code == OpCodes.Brfalse || code == OpCodes.Brfalse_S)
            {
                if (stack.Count == 0 || index + 1 == raw.Length) return;
                var isTrue = stack.Pop() != 0;
                var onTrue = code == OpCodes.Brtrue || code == OpCodes.Brtrue_S;
                branches[instruction.Offset] = isTrue == onTrue
                    ? instruction.BranchTargets.Single() : raw[index + 1].Offset;
                return;
            }
            else return;
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
