using System;
using System.Buffers;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Core.IntermediateRepresentation.Calls;

namespace NetWasm.Compiler.Wasm.Units;

/// <summary>A 128-bit content fingerprint (XXH3-128): fast, and wide enough that two
/// different answers do not collide in practice; it is not a security boundary.</summary>
internal readonly record struct UnitFingerprint(ulong First, ulong Second);

/// <summary>
/// Accumulates a value's content, field by field and length-prefixed, and fingerprints it.
/// Each Add writes a tag-free, fixed layout, so two different values of one shape cannot
/// collide by concatenation.
/// </summary>
internal ref struct UnitFingerprintBuilder
{
    private ArrayBufferWriter<byte>? _bytes;

    private ArrayBufferWriter<byte> Bytes => _bytes ??= new(256);

    public void Add(bool value) => Add(value ? 1 : 0);

    public void Add(int value)
    {
        BinaryPrimitives.WriteInt32LittleEndian(Bytes.GetSpan(sizeof(int)), value);
        Bytes.Advance(sizeof(int));
    }

    public void Add(long value)
    {
        BinaryPrimitives.WriteInt64LittleEndian(Bytes.GetSpan(sizeof(long)), value);
        Bytes.Advance(sizeof(long));
    }

    public void Add(string? value)
    {
        if (value is null)
        {
            Add(-1);
            return;
        }
        var length = System.Text.Encoding.UTF8.GetByteCount(value);
        Add(length);
        System.Text.Encoding.UTF8.GetBytes(value, Bytes.GetSpan(length));
        Bytes.Advance(length);
    }

    public void Add(ReadOnlySpan<byte> value)
    {
        Add(value.Length);
        Bytes.Write(value);
    }

    public void Add(ImmutableArray<int> values)
    {
        Add(values.IsDefault ? -1 : values.Length);
        if (values.IsDefault) return;
        foreach (var value in values) Add(value);
    }

    public void Add(IEnumerable<int> values)
    {
        var list = values as IReadOnlyCollection<int> ?? [.. values];
        Add(list.Count);
        foreach (var value in list) Add(value);
    }

    public void Add(CliTypeIdentity? type) => Add(type?.CanonicalName);

    // A member a fact may lack fingerprints as none, as a missing type does.
    public void Add(MethodInstanceModel? method) => Add(method?.CanonicalName);

    public void Add(FieldInstanceModel? field) => Add(field?.CanonicalName);

    public void Add(EntityKey key)
    {
        Add(key.Assembly.Name);
        Add(key.MetadataToken);
    }

    public void Add(ObjectLayout layout)
    {
        Add(layout.TypeId);
        Add(layout.LastSubtypeId);
        Add(layout.Size);
        Add(layout.ReferenceOffsets);
    }

    public void Add(ValueLayout layout)
    {
        Add(layout.Type);
        Add(layout.Size);
        Add(layout.Alignment);
        Add(layout.ReferenceOffsets);
        Add(layout.ByReferenceOffsets);
    }

    public void Add(FieldLayout layout)
    {
        Add(layout.Offset);
        Add(layout.Size);
        Add(layout.Type);
    }

    public void Add(StaticFieldLayout layout) => Add(layout.Address);

    public void Add(StringLayout layout)
    {
        Add(layout.Address);
        Add(layout.Length);
        Add(layout.DataOffset);
    }

    // A method's analysed body: its CIL by resolved identity (never by metadata token, which
    // shifts when the assembly gains a member), its locals, its exception regions and the
    // proofs analysis attached. Structuring is a function of these and the compile's
    // structuring policy, so a method whose body fingerprints the same structures the same.
    public void Add(NetWasm.Compiler.ControlFlow.ManagedMethodBody method)
    {
        var body = method.Body;
        Add(method.Method.CanonicalName);
        Add(body.MaxStack);
        Add(body.Locals.Select(local => (int)local));
        Add(body.LocalSignatureTypes.Length);
        foreach (var type in body.LocalSignatureTypes) Add(type);
        Add(body.Instructions.Length);
        foreach (var instruction in body.Instructions) Add(instruction);
        // As the structured header carries them. Which references are proven non-null
        // can depend on the rest of the program, as a constructor's this does on its
        // callers, so those offsets count too.
        Add(body.ExceptionRegions.IsEmpty && body.ArrayBoundsProof is not null);
        if (body.ExceptionRegions.IsEmpty && body.ArrayBoundsProof is { } proof) Add(proof.NonNullOffsets.Order());
        Add(body.IntegerCheckProof is not null);
        Add(body.PinnedLocals.Order());
        Add(body.ExceptionRegions.Length);
        foreach (var region in body.ExceptionRegions)
        {
            Add((int)region.Kind);
            Add(region.TryOffset);
            Add(region.TryLength);
            Add(region.HandlerOffset);
            Add(region.HandlerLength);
            Add(region.FilterOffset ?? -1);
            Add(region.CatchType is { } catchType ? UnitQueryKeys.Of(catchType) : null);
            Add(region.CatchTypeIdentity);
        }
    }

    public void Add(CilInstruction instruction)
    {
        Add(instruction.Offset);
        Add(instruction.NextOffset);
        Add(instruction.OriginalOffset ?? -1);
        Add((int)instruction.Operation);
        switch (instruction.Operand)
        {
            case CilOperand.None:
                Add(0);
                break;
            case CilOperand.ConstantI4 constant:
                Add(1); Add(constant.Value);
                break;
            case CilOperand.ConstantI8 constant:
                Add(2); Add(constant.Value);
                break;
            case CilOperand.ConstantF4 constant:
                Add(3); Add(BitConverter.SingleToInt32Bits(constant.Value));
                break;
            case CilOperand.ConstantF8 constant:
                Add(4); Add(BitConverter.DoubleToInt64Bits(constant.Value));
                break;
            case CilOperand.Index index:
                Add(5); Add(index.Value);
                break;
            case CilOperand.BranchTarget target:
                Add(6); Add(target.Offset);
                break;
            case CilOperand.SwitchTargets targets:
                Add(7); Add(targets.Offsets);
                break;
            case CilOperand.Entity entity:
                Add(8); Add(entity.Key);
                break;
            case CilOperand.MethodInstance method:
                Add(9); Add(method.Value.CanonicalName);
                break;
            case CilOperand.FieldInstance field:
                Add(10); Add(field.Value.CanonicalName);
                break;
            case CilOperand.TypeIdentity type:
                Add(11); Add(type.Value);
                break;
            case CilOperand.ExactType type:
                Add(12); Add(type.Value);
                break;
            case CilOperand.CallSite site:
                Add(13);
                Add(site.Unmanaged);
                Add(site.Signature.ReturnSignatureType);
                Add(site.Signature.ParameterSignatureTypes.Length);
                foreach (var parameter in site.Signature.ParameterSignatureTypes) Add(parameter);
                break;
            case CilOperand.NumericConversion conversion:
                Add(14);
                Add(conversion.BitWidth);
                Add(conversion.DestinationUnsigned);
                Add(conversion.Checked);
                Add(conversion.SourceUnsigned);
                Add(conversion.Native);
                break;
            case CilOperand.UserString text:
                Add(15); Add(text.Value);
                break;
            case CilOperand.ArrayInitializerData data:
                Add(16); Add(data.Field.CanonicalName); Add(data.Value.AsSpan());
                break;
            // The last of the closed set of operands (UnitFingerprintOperandTests counts them).
            default:
                var span = (CilOperand.ReadOnlySpanData)instruction.Operand;
                Add(17);
                Add(span.Field.CanonicalName);
                Add(span.SpanType);
                Add(span.ElementType);
                Add(span.ElementCount);
                Add(span.Field.Definition.InitialData.AsSpan());
                break;
        }
    }

    public void Add(MethodRootMap map)
    {
        Add(map.Method);
        Add(map.Slots.Count);
        foreach (var (source, slot) in map.Slots.OrderBy(pair => pair.Value))
        {
            Add(source);
            Add(slot);
        }
        Add(map.Safepoints.Count);
        foreach (var (offset, safepoint) in map.Safepoints.OrderBy(pair => pair.Key))
        {
            Add(offset);
            Add(safepoint.IlOffset);
            Add(safepoint.Roots.Length);
            foreach (var root in safepoint.Roots) Add(root);
            Add(safepoint.ConstructorCallRoots.Length);
            foreach (var root in safepoint.ConstructorCallRoots) Add(root);
        }
        Add(map.AnchorPlan.Anchors.Length);
        foreach (var anchor in map.AnchorPlan.Anchors)
        {
            Add((int)anchor.Location.Kind);
            Add(anchor.Location.Index);
            Add(anchor.BackingType);
        }
        Add(map.AnchorPlan.Assignments.Count);
        foreach (var (offset, assignments) in map.AnchorPlan.Assignments.OrderBy(pair => pair.Key))
        {
            Add(offset);
            Add(assignments.Length);
            // Assignments are small value records whose generated text lists every field.
            foreach (var assignment in assignments) Add(assignment.ToString());
        }
    }

    public void Add(RootSource source)
    {
        Add((int)source.Kind);
        Add(source.Index);
        Add(source.ByteOffset);
    }

    public void Add(ManagedCallSite site)
    {
        Add(site.Key.Caller.CanonicalName);
        Add(site.Key.InstructionOffset);
        Add((int)site.Operation);
        Add(site.TargetIdentity.CanonicalName);
        Add(site.Target.CanonicalName);
        Add(site.ConstrainedType);
    }

    public void Add(ManagedByRefMethodEffects effects)
    {
        Add(effects.WrittenArguments.Order());
        Add(effects.EscapedArguments.Order());
        Add(effects.ReturnedArgumentAliases.Order());
    }

    public void Add(DispatchCallSiteModel site)
    {
        Add(site.Caller);
        Add(site.IlOffset);
        Add(site.Declaration.CanonicalName);
        Add(site.Targets.Length);
        foreach (var target in site.Targets)
        {
            Add(target.ReceiverType);
            Add(target.Method.CanonicalName);
        }
    }

    public void Add(TypeTestSiteModel site)
    {
        Add(site.Caller);
        Add(site.IlOffset);
        Add(site.TargetType);
        Add(site.MatchingTypes.Length);
        foreach (var type in site.MatchingTypes) Add(type);
    }

    public readonly UnitFingerprint Finish()
    {
        var digest = System.IO.Hashing.XxHash128.HashToUInt128(_bytes is null ? [] : _bytes.WrittenSpan);
        return new((ulong)(digest >> 64), (ulong)digest);
    }
}
