// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Immutable;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Metadata.RuntimeProvidedMembers;

// A CIL body before layout, built by the compiler or read from a decoded body to be rewritten: its declared locals, its
// elements in order and its exception regions. Locals and labels are objects the elements name; the layout numbers the
// declared locals in order, then any other local in the order it is first used, and gives each label the offset of the
// next instruction after its mark.
internal sealed record CilBodyDraft(
    ImmutableArray<CilBodyLocal> Locals,
    ImmutableArray<CilBodyElement> Elements,
    ImmutableArray<CilBodyRegion> Regions)
{
    // The header's localsinit flag: a decoded body keeps its own, and a runtime-provided body sets it.
    public bool InitializesLocals { get; init; } = true;
}

// A protected block from TryStart to TryEnd and its handler from HandlerStart to HandlerEnd; a filter runs from
// FilterStart to HandlerStart, and a catch names the type it catches.
internal sealed record CilBodyRegion(
    CilExceptionRegionKind Kind,
    CilBodyLabel TryStart,
    CilBodyLabel TryEnd,
    CilBodyLabel HandlerStart,
    CilBodyLabel HandlerEnd)
{
    public EntityKey? CatchType { get; init; }

    public CliTypeIdentity? CatchTypeIdentity { get; init; }

    public CilBodyLabel? FilterStart { get; init; }

    // CilExceptionRegion.PassThroughLocal: while this local is nonzero, the catch is passed by in the first pass.
    public CilBodyLocal? PassThroughLocal { get; init; }
}

// A local of a runtime-provided body, which the layout numbers.
internal sealed class CilBodyLocal(CliTypeIdentity type, bool pinned = false)
{
    public CliTypeIdentity Type { get; } = type;

    public bool Pinned { get; } = pinned;
}

// A branch target of a runtime-provided body, which the layout places.
internal sealed class CilBodyLabel;

internal abstract record CilBodyElement
{
    private CilBodyElement()
    {
    }

    // The source offset the instruction maps to (a decoded instruction's own), which stack traces and debug lines read.
    public int? Source { get; init; }

    // An instruction with its operand.
    public sealed record Instruction(CilOperation Operation, CilOperand Operand) : CilBodyElement;

    // An instruction that names a local: ldloc, stloc or ldloca.
    public sealed record LocalInstruction(CilOperation Operation, CilBodyLocal Local) : CilBodyElement;

    // A branch, or leave, to a label.
    public sealed record Branch(CilOperation Operation, CilBodyLabel Target) : CilBodyElement;

    // A switch to one label per case.
    public sealed record Switch(ImmutableArray<CilBodyLabel> Targets) : CilBodyElement;

    // The label names the next instruction.
    public sealed record Mark(CilBodyLabel Label) : CilBodyElement;
}

// Shorthand for the elements a runtime-provided body is written in.
internal static class CilBodyElements
{
    public static CilBodyElement Op(CilOperation operation) =>
        new CilBodyElement.Instruction(operation, new CilOperand.None());

    public static CilBodyElement Op(CilOperation operation, CilOperand operand) =>
        new CilBodyElement.Instruction(operation, operand);

    public static CilBodyElement Argument(int index) =>
        new CilBodyElement.Instruction(CilOperation.LoadArgument, new CilOperand.Index(index));

    public static CilBodyElement Call(MethodInstanceModel method) =>
        new CilBodyElement.Instruction(CilOperation.Call, new CilOperand.MethodInstance(method));

    public static CilBodyElement Load(CilBodyLocal local) =>
        new CilBodyElement.LocalInstruction(CilOperation.LoadLocal, local);

    public static CilBodyElement Store(CilBodyLocal local) =>
        new CilBodyElement.LocalInstruction(CilOperation.StoreLocal, local);

    public static CilBodyElement Address(CilBodyLocal local) =>
        new CilBodyElement.LocalInstruction(CilOperation.LoadLocalAddress, local);

    public static CilBodyElement Branch(CilOperation operation, CilBodyLabel target) =>
        new CilBodyElement.Branch(operation, target);

    public static CilBodyElement Mark(CilBodyLabel label) => new CilBodyElement.Mark(label);
}
