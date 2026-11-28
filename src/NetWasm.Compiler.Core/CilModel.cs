// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using System.Collections.Immutable;
using System.Linq;

namespace NetWasm.Compiler.Core;

public enum CilOperation
{
    Nop,
    LoadArgument,
    LoadArgumentAddress,
    StoreArgument,
    LoadLocal,
    LoadLocalAddress,
    StoreLocal,
    LoadInt32,
    LoadInt64,
    LoadFloat32,
    LoadFloat64,
    LoadNull,
    LoadString,
    LoadTypeToken,
    LoadFieldToken,
    Duplicate,
    Pop,
    LoadField,
    LoadFieldAddress,
    StoreField,
    LoadStaticField,
    LoadStaticFieldAddress,
    StoreStaticField,
    LoadObject,
    StoreObject,
    CopyObject,
    InitializeObject,
    SizeOf,
    LocalAllocate,
    CopyBlock,
    InitializeBlock,
    InitializeArrayData,
    CreateReadOnlySpanFromRva,
    DefaultValue,
    Add,
    Subtract,
    Multiply,
    BitwiseAnd,
    BitwiseOr,
    BitwiseXor,
    ShiftLeft,
    ShiftRightSigned,
    ShiftRightUnsigned,
    Negate,
    OnesComplement,
    AddChecked,
    AddCheckedUnsigned,
    SubtractChecked,
    SubtractCheckedUnsigned,
    MultiplyChecked,
    MultiplyCheckedUnsigned,
    Divide,
    DivideUnsigned,
    Remainder,
    RemainderUnsigned,
    ConvertInt32,
    ConvertInt32Unsigned,
    ConvertInt64,
    ConvertInt64Unsigned,
    ConvertNativeInt,
    ConvertNativeUInt,
    ConvertFloat32,
    ConvertFloat64,
    ConvertFloatUnsigned,
    CheckFinite,
    ConvertNumeric,
    CompareEqual,
    CompareGreaterThanSigned,
    CompareGreaterThanUnsigned,
    CompareLessThanSigned,
    CompareLessThanUnsigned,
    Branch,
    BranchIfTrue,
    BranchIfFalse,
    BranchIfEqual,
    BranchIfNotEqual,
    BranchIfGreaterThanSigned,
    BranchIfGreaterThanUnsigned,
    BranchIfGreaterThanOrEqualSigned,
    BranchIfGreaterThanOrEqualUnsigned,
    BranchIfLessThanSigned,
    BranchIfLessThanUnsigned,
    BranchIfLessThanOrEqualSigned,
    BranchIfLessThanOrEqualUnsigned,
    Switch,
    LoadFunction,
    LoadVirtualFunction,
    CompareExchange,
    MaterializeType,
    GetObjectType,
    Call,
    CallVirtual,
    CallIndirect,
    NewObject,
    Box,
    Unbox,
    UnboxAny,
    Constrained,
    Volatile,
    Readonly,
    Unaligned,
    Break,
    NewArray,
    NewRectangularArray,
    NewBoundedRectangularArray,
    LoadArrayLength,
    LoadArrayElementReference,
    StoreArrayElementReference,
    LoadArrayElement,
    LoadArrayElementAddress,
    StoreArrayElement,
    LoadRectangularArrayElement,
    StoreRectangularArrayElement,
    LoadRectangularArrayElementAddress,
    CastClass,
    IsInstance,
    Throw,
    Rethrow,
    Leave,
    EndFinally,
    EndFilter,
    Return,
    LoadMethodToken,
    MaterializeMethod,
    MaterializeField,
    GetTypeFacts,
    LoadStaticDataElement,
    // value-if-true, value-if-false, condition (int32) -> the first when the condition is not zero, as Wasm's
    // select: the compiler's form of a branch whose arms only compute numbers, which it runs without branching.
    Select,
}

public abstract record CilOperand
{
    private CilOperand()
    {
    }

    public sealed record None : CilOperand;
    public sealed record ConstantI4(int Value) : CilOperand;
    public sealed record ConstantI8(long Value) : CilOperand;
    public sealed record ConstantF4(float Value) : CilOperand;
    public sealed record ConstantF8(double Value) : CilOperand;
    public sealed record Index(int Value) : CilOperand;
    public sealed record BranchTarget(int Offset) : CilOperand;
    public sealed record SwitchTargets(ImmutableArray<int> Offsets) : CilOperand;
    public sealed record Entity(EntityKey Key) : CilOperand;
    public sealed record MethodInstance(MethodInstanceModel Value) : CilOperand;
    public sealed record FieldInstance(FieldInstanceModel Value) : CilOperand;
    public sealed record TypeIdentity(CliTypeIdentity Value) : CilOperand;
    // The one type a GetObjectType's receiver can have, when it is not null.
    public sealed record ExactType(CliTypeIdentity Value) : CilOperand;
    // A throw that never runs: the one after a call to a method that never returns, which ends the call's block
    // (NonReturningCallRewriter). It lowers to Wasm's unreachable.
    public sealed record NeverRuns : CilOperand;
    // A calli's signature. An unmanaged one (delegate* unmanaged) calls native code through the
    // C ABI, with its arguments lowered as a DllImport's (issue 226).
    public sealed record CallSite(MethodSignatureModel Signature, bool Unmanaged = false) : CilOperand;
    public sealed record NumericConversion(
        int BitWidth,
        bool DestinationUnsigned,
        bool Checked,
        bool SourceUnsigned,
        bool Native) : CilOperand;
    public sealed record UserString(string Value) : CilOperand;
    // An array initializer's field RVA data, kept with its field so the bytes are placed in static
    // data and copied from there.
    public sealed record ArrayInitializerData(FieldInstanceModel Field) : CilOperand
    {
        public ImmutableArray<byte> Value => Field.Definition.InitialData;
    }
    public sealed record ReadOnlySpanData(
        FieldInstanceModel Field,
        CliTypeIdentity SpanType,
        CliTypeIdentity ElementType,
        int ElementCount) : CilOperand;
}

public sealed record CilInstruction(
    int Offset,
    int NextOffset,
    CilOperation Operation,
    CilOperand Operand)
{
    public int? OriginalOffset { get; init; }

    // Where the instruction came from in the IL as read: a rewrite keeps it for everything it makes from an
    // instruction, so measures of the code left (the inliner's) count that instruction once.
    public int SourceOffset => OriginalOffset ?? Offset;

    // The IL offset whose sequence point the instruction reports in stack traces and the line table, when that is not
    // where it came from: a return folded into the branch to a shared epilogue reports the epilogue's, the method's
    // closing brace, while it still measures as the branch it replaced.
    public int? SequencePointOffset { get; init; }

    public int LineOffset => SequencePointOffset ?? SourceOffset;
}

public sealed record CilMethodBody(
    MethodDefinitionModel Method,
    int MaxStack,
    ImmutableArray<CliValueKind> Locals,
    ImmutableArray<CilInstruction> Instructions)
{
    public ArrayBoundsProof? ArrayBoundsProof { get; init; }

    public IntegerCheckProof? IntegerCheckProof { get; init; }

    public MethodInstanceModel? MethodInstance { get; init; }

    // A class constructor whose this is a young object when it starts: every way the program reaches it passes an
    // object allocated since the last collection. Its stores into this before its first safepoint need not be
    // recorded for a collector with generations. It depends on the rest of the program, as a proof does.
    public bool YoungThisAtEntry { get; init; }

    public ImmutableArray<CliTypeIdentity> LocalSignatureTypes { get; init; } =
        [.. Locals.Select(CliTypeIdentity.FromStackKind)];

    public ImmutableArray<CilExceptionRegion> ExceptionRegions { get; init; } = [];

    // The locals the signature pins: each keeps what it holds alive until it is
    // overwritten, though the code reads it only once to take an address.
    public ImmutableHashSet<int> PinnedLocals { get; init; } = [];

    // The header's localsinit flag (C# sets it unless SkipLocalsInit applies): locals start zeroed
    // and localloc returns zeroed memory (ECMA-335 II.24.4.4, III.3.47).
    public bool InitializesLocals { get; init; }

    // Set when the method's IL names a member its declaring type lacks: the body then only throws
    // MissingMethodException or MissingFieldException, as .NET throws when it first compiles such a method.
    public MissingMemberReference? MissingMember { get; init; }
}

public enum MissingMemberKind
{
    Method,
    Field,
    // A type the IL names that its assembly lacks.
    Type,
}

// A member a method's IL names that its resolved declaring type lacks, or a type its assembly lacks: the member or
// type as the reference names it, where the method names it, and the NW1016 message that reports it.
public sealed record MissingMemberReference(MissingMemberKind Kind, string Member, string Method, int IlOffset,
    string Message)
{
    // For a missing type, the assembly the reference names.
    public string? Assembly { get; init; }

    // The message .NET's MissingMethodException, MissingFieldException or TypeLoadException carries for it.
    public string ExceptionMessage => Kind switch
    {
        MissingMemberKind.Method => $"Method not found: '{Member}'.",
        MissingMemberKind.Field => $"Field not found: '{Member}'.",
        _ => $"Could not load type '{Member}' from assembly '{Assembly}'.",
    };

    // The exception a call of the method throws.
    public string ExceptionType => Kind switch
    {
        MissingMemberKind.Method => "System.MissingMethodException",
        MissingMemberKind.Field => "System.MissingFieldException",
        _ => "System.TypeLoadException",
    };
}

public enum CilExceptionRegionKind
{
    Catch,
    Filter,
    Finally,
    Fault,
}

public sealed record CilExceptionRegion(
    CilExceptionRegionKind Kind,
    int TryOffset,
    int TryLength,
    int HandlerOffset,
    int HandlerLength,
    EntityKey? CatchType,
    int? FilterOffset)
{
    // Definition keys alone cannot distinguish constructed exception types.
    public CliTypeIdentity? CatchTypeIdentity { get; init; }

    // A catch that takes part in the first pass of exception dispatch only while this local is zero. While it is
    // nonzero the search passes the protected block by, as if it had no handler, and the handler, which then ends in
    // a rethrow, runs as the exception unwinds through the block: runtime async's catch in a direct call, where it
    // only puts its caller's state back, as desktop's direct call has no handler there.
    public int? PassThroughLocal { get; init; }
}

public static class SupportedCil
{
    public static ImmutableArray<CilOperation> Operations { get; } =
        [.. Enum.GetValues<CilOperation>()];
}
