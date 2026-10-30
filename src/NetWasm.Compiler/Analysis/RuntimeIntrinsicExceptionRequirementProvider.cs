using System;
using System.Collections.Immutable;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Analysis;

internal sealed class RuntimeIntrinsicExceptionRequirementProvider(
    IRuntimeIntrinsicRegistry intrinsics) :
    IRuntimeIntrinsicExceptionRequirementProvider
{
    private static readonly ReachabilityExceptionRequirement NullReference =
        new(ManagedExceptionKind.NullReference, "System.NullReferenceException");
    private static readonly ReachabilityExceptionRequirement IndexOutOfRange =
        new(ManagedExceptionKind.IndexOutOfRange, "System.IndexOutOfRangeException");
    private static readonly ReachabilityExceptionRequirement OutOfMemory =
        new(ManagedExceptionKind.OutOfMemory, "System.OutOfMemoryException");
    private static readonly ReachabilityExceptionRequirement ArgumentNull =
        new(ManagedExceptionKind.ArgumentNull, "System.ArgumentNullException");
    private static readonly ReachabilityExceptionRequirement Argument =
        new(ManagedExceptionKind.Argument, "System.ArgumentException");
    private static readonly ReachabilityExceptionRequirement InvalidCast =
        new(ManagedExceptionKind.InvalidCast, "System.InvalidCastException");
    private static readonly ReachabilityExceptionRequirement Overflow =
        new(ManagedExceptionKind.Overflow, "System.OverflowException");
    private static readonly ReachabilityExceptionRequirement DivideByZero =
        new(ManagedExceptionKind.DivideByZero, "System.DivideByZeroException");
    private static readonly ReachabilityExceptionRequirement InvalidOperation =
        new(ManagedExceptionKind.InvalidOperation, "System.InvalidOperationException");
    private static readonly ReachabilityExceptionRequirement ArgumentOutOfRange =
        new(ManagedExceptionKind.ArgumentOutOfRange, "System.ArgumentOutOfRangeException");
    private static readonly ReachabilityExceptionRequirement NotSupported =
        new(ManagedExceptionKind.NotSupported, "System.NotSupportedException");

    private readonly IRuntimeIntrinsicRegistry _intrinsics = intrinsics ??
        throw new ArgumentNullException(nameof(intrinsics));

    public ImmutableArray<ReachabilityExceptionRequirement> Discover(
        MethodDefinitionModel method)
    {
        ArgumentNullException.ThrowIfNull(method);
        if (!_intrinsics.TryGetIntrinsic(method.Key, out var intrinsic))
        {
            return [];
        }

        return intrinsic switch
        {
            RuntimeIntrinsic.StringLength or
            RuntimeIntrinsic.ArrayLength or
            RuntimeIntrinsic.ArrayRank or
            RuntimeIntrinsic.JSObjectDispose or
            RuntimeIntrinsic.JSSubscriptionDispose or
            RuntimeIntrinsic.GetArrayDataReference or
            RuntimeIntrinsic.EnumEquals or
            RuntimeIntrinsic.EnumGetHashCode or
            RuntimeIntrinsic.EnumGetTypeCode => [NullReference],

            RuntimeIntrinsic.NullableGetUnderlyingType => [ArgumentNull],

            RuntimeIntrinsic.ArrayGetLength or
            RuntimeIntrinsic.ArrayGetLowerBound or
            RuntimeIntrinsic.StringCharacterAt => [NullReference, IndexOutOfRange],

            RuntimeIntrinsic.ArrayGetValue => [NullReference, OutOfMemory],

            RuntimeIntrinsic.MemberExecuteMethod =>
                [NullReference, OutOfMemory, InvalidCast, InvalidOperation],

            RuntimeIntrinsic.DelegateDynamicInvoke => [OutOfMemory, Argument],

            RuntimeIntrinsic.MemberReadField => [NullReference, OutOfMemory],

            RuntimeIntrinsic.NativeMemoryAlloc or
            RuntimeIntrinsic.NativeMemoryRealloc or
            RuntimeIntrinsic.NativeMemoryAlignedAlloc or
            RuntimeIntrinsic.NativeMemoryAlignedRealloc or
            RuntimeIntrinsic.ObjectArrayDelegateAdapterCreate => [OutOfMemory],

            RuntimeIntrinsic.UnsafeUnbox => [NullReference, InvalidCast],

            RuntimeIntrinsic.EnumCompareTo or
            RuntimeIntrinsic.EnumHasFlag => [NullReference, Argument],

            RuntimeIntrinsic.EnumGetNames or
            RuntimeIntrinsic.EnumGetName or
            RuntimeIntrinsic.EnumGetValues or
            RuntimeIntrinsic.EnumIsDefined when method.GenericArity == 0 =>
                [ArgumentNull, Argument],

            RuntimeIntrinsic.EnumGetUnderlyingType or
            RuntimeIntrinsic.EnumFormat or
            RuntimeIntrinsic.EnumToObject => [ArgumentNull, Argument],
            RuntimeIntrinsic.IntegerDivRem => DivRemRequirements(method),
            RuntimeIntrinsic.EnumConvert => ConvertRequirements(method),
            // A vector of an element type it does not support, as upstream.
            RuntimeIntrinsic.SimdGetElement => [NotSupported, ArgumentOutOfRange],
            // Integer lanes divide one by one, as upstream's Scalar<T>.Divide.
            RuntimeIntrinsic.SimdDivide => [NotSupported, DivideByZero, Overflow],
            RuntimeIntrinsic.PackedSimdInstruction when
                PackedSimdInstructions.TryGet(method, out var instruction) &&
                instruction.Shape is not (PackedSimdShape.Simple or PackedSimdShape.Memory) =>
                [ArgumentOutOfRange],
            _ when RuntimeIntrinsicKinds.IsSimdVector(intrinsic) => [NotSupported],
            _ => [],
        };
    }

    private static ImmutableArray<ReachabilityExceptionRequirement> ConvertRequirements(
        MethodDefinitionModel method) => method.Name switch
        {
            "InternalToDateTime" => [InvalidCast],
            _ => [],
        };

    private static ImmutableArray<ReachabilityExceptionRequirement> DivRemRequirements(
        MethodDefinitionModel method) =>
        method.Signature.ParameterSignatureTypes[0].CanonicalName is
            "primitive:i4" or "primitive:i8" or "primitive:nativeint"
                ? [DivideByZero, Overflow]
                : [DivideByZero];
}
