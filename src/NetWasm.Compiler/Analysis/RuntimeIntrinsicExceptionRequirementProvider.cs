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

            RuntimeIntrinsic.NativeMemoryAlloc or
            RuntimeIntrinsic.NativeMemoryRealloc or
            RuntimeIntrinsic.NativeMemoryAlignedAlloc or
            RuntimeIntrinsic.NativeMemoryAlignedRealloc => [OutOfMemory],

            RuntimeIntrinsic.UnsafeUnbox => [NullReference, InvalidCast],

            RuntimeIntrinsic.EnumCompareTo or
            RuntimeIntrinsic.EnumHasFlag => [NullReference, Argument],

            RuntimeIntrinsic.EnumGetNames or
            RuntimeIntrinsic.EnumGetName or
            RuntimeIntrinsic.EnumGetValues or
            RuntimeIntrinsic.EnumIsDefined when method.GenericArity == 0 =>
                [ArgumentNull, Argument],

            RuntimeIntrinsic.EnumParse => ParseRequirements(method),
            RuntimeIntrinsic.EnumGetUnderlyingType or
            RuntimeIntrinsic.EnumFormat or
            RuntimeIntrinsic.EnumToObject => [ArgumentNull, Argument],
            RuntimeIntrinsic.EnumConvert => ConvertRequirements(method),
            _ => [],
        };
    }

    private static ImmutableArray<ReachabilityExceptionRequirement> ParseRequirements(
        MethodDefinitionModel method)
    {
        var typeBased = method.GenericArity == 0;
        var tryParse = method.Name == "InternalTryParse";
        return (typeBased, tryParse) switch
        {
            (true, true) => [ArgumentNull, Argument],
            (true, false) => [ArgumentNull, Argument, Overflow],
            (false, false) => [Argument, Overflow],
            _ => [],
        };
    }

    private static ImmutableArray<ReachabilityExceptionRequirement> ConvertRequirements(
        MethodDefinitionModel method) => method.Name switch
        {
            "InternalToType" or "System.IConvertible.ToType" =>
                [ArgumentNull, InvalidCast],
            "InternalToDateTime" => [InvalidCast],
            _ => [],
        };
}
