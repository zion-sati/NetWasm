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
    DelegateCombine,
    DelegateRemove,
    DelegateEqual,
    DelegateNotEqual,
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
    public sealed record CallSite(MethodSignatureModel Signature) : CilOperand;
    public sealed record NumericConversion(
        int BitWidth,
        bool DestinationUnsigned,
        bool Checked,
        bool SourceUnsigned,
        bool Native) : CilOperand;
    public sealed record UserString(string Value) : CilOperand;
    public sealed record ByteData(ImmutableArray<byte> Value) : CilOperand;
}

public sealed record CilInstruction(
    int Offset,
    int NextOffset,
    CilOperation Operation,
    CilOperand Operand);

public sealed record CilMethodBody(
    MethodDefinitionModel Method,
    int MaxStack,
    ImmutableArray<CliValueKind> Locals,
    ImmutableArray<CilInstruction> Instructions)
{
    public MethodInstanceModel? MethodInstance { get; init; }

    public ImmutableArray<CliTypeIdentity> LocalSignatureTypes { get; init; } =
        [.. Locals.Select(CliTypeIdentity.FromStackKind)];

    public ImmutableArray<CilExceptionRegion> ExceptionRegions { get; init; } = [];
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
    int? FilterOffset);

public static class SupportedCil
{
    public static ImmutableArray<CilOperation> Operations { get; } =
        [.. Enum.GetValues<CilOperation>()];
}
