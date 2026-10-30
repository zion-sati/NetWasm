using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using NetWasm.Compiler;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Analysis;

public sealed class RuntimeIntrinsicRegistry : IRuntimeIntrinsicRegistry
{
    private static readonly FrozenSet<string> IntegralPrimitiveNames =
        new[]
        {
            "primitive:i1", "primitive:u1", "primitive:i2", "primitive:u2",
            "primitive:char", "primitive:i4", "primitive:u4", "primitive:i8",
            "primitive:u8", "primitive:nativeint", "primitive:nativeuint",
        }.ToFrozenSet(StringComparer.Ordinal);

    private readonly Dictionary<EntityKey, RuntimeIntrinsic> _intrinsics;

    public RuntimeIntrinsicRegistry(ISymbolFormatter symbols, IEnumerable<MethodDefinitionModel> methods)
    {
        ArgumentNullException.ThrowIfNull(symbols);
        ArgumentNullException.ThrowIfNull(methods);
        var intrinsics = new Dictionary<EntityKey, RuntimeIntrinsic>();
        foreach (var method in methods)
        {
            var typeName = symbols.Format(method.DeclaringType);
            RuntimeIntrinsic? intrinsic = (typeName, method.Name) switch
            {
                ("System.String", "get_Length") => RuntimeIntrinsic.StringLength,
                ("System.Array", "get_Length") => RuntimeIntrinsic.ArrayLength,
                ("System.Array", "get_Rank") => RuntimeIntrinsic.ArrayRank,
                ("System.Array", "GetLength") => RuntimeIntrinsic.ArrayGetLength,
                ("System.Array", "GetLowerBound") => RuntimeIntrinsic.ArrayGetLowerBound,
                ("System.Array", "InternalGetValue") => RuntimeIntrinsic.ArrayGetValue,
                ("System.Array", "InternalCopy") => RuntimeIntrinsic.ArrayCopy,
                ("System.Array", "InternalClear") => RuntimeIntrinsic.ArrayClear,
                ("System.Array", "InternalClone") => RuntimeIntrinsic.ArrayClone,
                ("System.Array", "InternalCloneRange") => RuntimeIntrinsic.ArrayCloneRange,
                ("System.String", "get_Chars") when IsStringCharacterAt(method) =>
                    RuntimeIntrinsic.StringCharacterAt,
                ("System.String", "SetCharUnchecked") =>
                    RuntimeIntrinsic.StringSetCharacterUnchecked,
                ("System.String", "GetRawStringData") =>
                    RuntimeIntrinsic.StringGetRawDataReference,
                ("System.String", "EqualsHelper") when IsStringEqualsHelper(method) =>
                    RuntimeIntrinsic.StringEqualsOrdinal,
                ("System.GCCollectionRuntime", "Collect") => RuntimeIntrinsic.GcCollect,
                ("System.GC", "SuppressFinalize") => RuntimeIntrinsic.SuppressFinalize,
                ("System.GC", "ReRegisterForFinalize") => RuntimeIntrinsic.ReRegisterForFinalize,
                ("System.WeakReferenceRuntime", "Create") => RuntimeIntrinsic.WeakHandleCreate,
                ("System.WeakReferenceRuntime", "Get") => RuntimeIntrinsic.WeakHandleGet,
                ("System.WeakReferenceRuntime", "Set") => RuntimeIntrinsic.WeakHandleSet,
                ("System.WeakReferenceRuntime", "Release") => RuntimeIntrinsic.WeakHandleRelease,
                ("System.Runtime.CompilerServices.ConditionalWeakTableRuntime", "Create") =>
                    RuntimeIntrinsic.EphemeronHandleCreate,
                ("System.Runtime.CompilerServices.ConditionalWeakTableRuntime", "GetKey") =>
                    RuntimeIntrinsic.EphemeronHandleGetKey,
                ("System.Runtime.CompilerServices.ConditionalWeakTableRuntime", "GetValue") =>
                    RuntimeIntrinsic.EphemeronHandleGetValue,
                ("System.Runtime.CompilerServices.ConditionalWeakTableRuntime", "SetTargetToNull") =>
                    RuntimeIntrinsic.EphemeronHandleSetTargetToNull,
                ("System.Runtime.CompilerServices.ConditionalWeakTableRuntime", "SetValue") =>
                    RuntimeIntrinsic.EphemeronHandleSetValue,
                ("System.Runtime.CompilerServices.ConditionalWeakTableRuntime", "Release") =>
                    RuntimeIntrinsic.EphemeronHandleRelease,
                ("System.GCHandleRuntime", "Create") => RuntimeIntrinsic.GcHandleCreate,
                ("System.GCHandleRuntime", "Get") => RuntimeIntrinsic.GcHandleGet,
                ("System.GCHandleRuntime", "Set") => RuntimeIntrinsic.GcHandleSet,
                ("System.GCHandleRuntime", "Release") => RuntimeIntrinsic.GcHandleRelease,
                ("System.GCHandleRuntime", "Address") => RuntimeIntrinsic.GcHandleAddress,
                ("System.GCMetricRuntime", "Read") => RuntimeIntrinsic.GcGetMetric,
                ("System.GCMetricSupportRuntime", "IsSupported") =>
                    RuntimeIntrinsic.GcMetricIsSupported,
                ("System.GCFinalizerRuntime", "WaitForPending") => RuntimeIntrinsic.GcWaitForPendingFinalizers,
                ("System.ObjectIdentityRuntime", "GetHashCode") => RuntimeIntrinsic.ObjectIdentityHash,
                ("System.Runtime.CompilerServices.RuntimeHelpersRuntime", "CloneBoxedValue") =>
                    RuntimeIntrinsic.BoxedValueClone,
                ("System.DelegateRuntime", "Clone") => RuntimeIntrinsic.DelegateClone,
                ("System.Runtime.CompilerServices.RuntimeHelpersRuntime", "AreEqual") =>
                    RuntimeIntrinsic.RuntimeHelpersEquals,
                ("System.MarvinSeedRuntime", "Get") => RuntimeIntrinsic.RandomSeed,
                ("System.ValueType", "Equals") => RuntimeIntrinsic.ValueTypeEquals,
                ("System.ValueType", "GetHashCode") =>
                    RuntimeIntrinsic.ValueTypeGetHashCode,
                ("System.Threading.Tasks.TaskDiagnostics", "ReportUnobservedException") =>
                    RuntimeIntrinsic.ReportUnobservedTaskException,
                ("System.Runtime.ExceptionServices.ExceptionDispatchInfo", "InternalThrow") =>
                    RuntimeIntrinsic.ExceptionDispatchPreserved,
                ("System.Runtime.InteropServices.JavaScript.JSObject", "Dispose") =>
                    RuntimeIntrinsic.JSObjectDispose,
                ("System.Runtime.InteropServices.JavaScript.JSSubscription", "Dispose") =>
                    RuntimeIntrinsic.JSSubscriptionDispose,
                ("System.Runtime.CompilerServices.RuntimeHelpers",
                    "IsReferenceOrContainsReferences") =>
                    RuntimeIntrinsic.IsReferenceOrContainsReferences,
                ("System.Runtime.CompilerServices.Unsafe", "Add") =>
                    RuntimeIntrinsic.UnsafeAdd,
                ("System.Runtime.CompilerServices.Unsafe", "As")
                    when method.GenericArity == 2 =>
                    RuntimeIntrinsic.UnsafeAs,
                ("System.Runtime.CompilerServices.Unsafe", "AsRef")
                    when method.Signature.ParameterTypes.AsSpan().SequenceEqual(
                        [CliValueKind.ManagedAddress]) =>
                    RuntimeIntrinsic.UnsafeAsRefManaged,
                ("System.Runtime.CompilerServices.Unsafe", "AsRef") =>
                    RuntimeIntrinsic.UnsafeAsRef,
                ("System.Runtime.CompilerServices.Unsafe", "NullRef") =>
                    RuntimeIntrinsic.UnsafeNullRef,
                ("System.Runtime.CompilerServices.Unsafe", "IsAddressGreaterThan") =>
                    RuntimeIntrinsic.UnsafeIsAddressGreaterThan,
                ("System.Runtime.CompilerServices.Unsafe", "SizeOf") =>
                    RuntimeIntrinsic.UnsafeSizeOf,
                ("System.Runtime.CompilerServices.Unsafe", "ByteOffset") =>
                    RuntimeIntrinsic.UnsafeByteOffset,
                ("System.Runtime.CompilerServices.Unsafe", "AddByteOffset") =>
                    RuntimeIntrinsic.UnsafeAddByteOffset,
                ("System.Runtime.CompilerServices.Unsafe", "As")
                    when method.GenericArity == 1 =>
                    RuntimeIntrinsic.UnsafeObjectAs,
                ("System.Runtime.CompilerServices.Unsafe", "Unbox") =>
                    RuntimeIntrinsic.UnsafeUnbox,
                ("System.Runtime.InteropServices.NativeMemory", "Alloc")
                    when method.Signature.ParameterTypes.Length == 1 =>
                    RuntimeIntrinsic.NativeMemoryAlloc,
                ("System.Runtime.InteropServices.NativeMemory", "Realloc") =>
                    RuntimeIntrinsic.NativeMemoryRealloc,
                ("System.Runtime.InteropServices.NativeMemory", "Free") =>
                    RuntimeIntrinsic.NativeMemoryFree,
                ("System.Runtime.InteropServices.NativeMemory", "AlignedAllocCore") =>
                    RuntimeIntrinsic.NativeMemoryAlignedAlloc,
                ("System.Runtime.InteropServices.NativeMemory", "AlignedReallocCore") =>
                    RuntimeIntrinsic.NativeMemoryAlignedRealloc,
                ("System.Runtime.InteropServices.NativeMemory", "AlignedFree") =>
                    RuntimeIntrinsic.NativeMemoryAlignedFree,
                ("System.Runtime.InteropServices.NativeMemory", "Clear") =>
                    RuntimeIntrinsic.NativeMemoryClear,
                ("System.Runtime.InteropServices.NativeMemory", "Copy") =>
                    RuntimeIntrinsic.NativeMemoryCopy,
                ("System.Runtime.InteropServices.NativeMemory", "Fill") =>
                    RuntimeIntrinsic.NativeMemoryFill,
                ("System.Runtime.CompilerServices.ArrayLoops", "Fill") =>
                    RuntimeIntrinsic.ArrayLoopFill,
                ("System.Runtime.CompilerServices.ArrayLoops", "Copy") =>
                    RuntimeIntrinsic.ArrayLoopCopy,
                ("System.Runtime.CompilerServices.ArrayLoops", "Xor32" or "Xor64") =>
                    RuntimeIntrinsic.ArrayLoopXor,
                ("System.Runtime.CompilerServices.ArrayLoops", "Or32" or "Or64") =>
                    RuntimeIntrinsic.ArrayLoopOr,
                ("System.Runtime.CompilerServices.ArrayLoops", "And32" or "And64") =>
                    RuntimeIntrinsic.ArrayLoopAnd,
                ("System.Runtime.InteropServices.MemoryMarshal", "GetArrayDataReference") =>
                    RuntimeIntrinsic.GetArrayDataReference,
                ("System.Nullable", "GetUnderlyingType") =>
                    RuntimeIntrinsic.NullableGetUnderlyingType,
                ("System.Runtime.InteropServices.WebAssembly.CanonicalAbi", "Reallocate") =>
                    RuntimeIntrinsic.ComponentReallocate,
                ("System.Runtime.InteropServices.WebAssembly.CanonicalAbi", "Free") =>
                    RuntimeIntrinsic.ComponentFree,
                ("System.Runtime.InteropServices.WebAssembly.CanonicalAbi",
                    "CreateResourceHandle") =>
                    RuntimeIntrinsic.ComponentResourceHandleCreate,
                ("System.Runtime.InteropServices.WebAssembly.CanonicalAbi",
                    "GetResourceHandle") =>
                    RuntimeIntrinsic.ComponentResourceHandleGet,
                ("System.Runtime.InteropServices.WebAssembly.CanonicalAbi",
                    "ReleaseResourceHandle") =>
                    RuntimeIntrinsic.ComponentResourceHandleRelease,
                ("System.Runtime.CompilerServices.ObjectArrayDelegateAdapter", "Create") =>
                    RuntimeIntrinsic.ObjectArrayDelegateAdapterCreate,
                ("System.Runtime.CompilerServices.RuntimeMemberExecution", "InvokeMethod") =>
                    RuntimeIntrinsic.MemberExecuteMethod,
                ("System.Runtime.CompilerServices.RuntimeMemberExecution", "InvokeDelegate") =>
                    RuntimeIntrinsic.DelegateDynamicInvoke,
                ("System.Runtime.CompilerServices.RuntimeMemberExecution", "ReadField") =>
                    RuntimeIntrinsic.MemberReadField,
                ("System.Enum", "Equals") => RuntimeIntrinsic.EnumEquals,
                ("System.Enum", "InternalEquals") => RuntimeIntrinsic.EnumEquals,
                ("System.Enum", "GetHashCode") => RuntimeIntrinsic.EnumGetHashCode,
                ("System.Enum", "InternalGetHashCode") => RuntimeIntrinsic.EnumGetHashCode,
                ("System.Enum", "CompareTo") => RuntimeIntrinsic.EnumCompareTo,
                ("System.Enum", "InternalCompareTo") => RuntimeIntrinsic.EnumCompareTo,
                ("System.Enum", "GetTypeCode") => RuntimeIntrinsic.EnumGetTypeCode,
                ("System.Enum", "InternalGetTypeCode") => RuntimeIntrinsic.EnumGetTypeCode,
                ("System.Enum", "HasFlag") => RuntimeIntrinsic.EnumHasFlag,
                ("System.Enum", "InternalHasFlag") => RuntimeIntrinsic.EnumHasFlag,
                ("System.Enum", "InternalGetNames") when method.GenericArity == 1 =>
                    RuntimeIntrinsic.EnumGetNames,
                ("System.Enum", "InternalGetNames") => RuntimeIntrinsic.EnumGetNames,
                ("System.Enum", "InternalGetName") when method.GenericArity == 1 =>
                    RuntimeIntrinsic.EnumGetName,
                ("System.Enum", "InternalGetName") => RuntimeIntrinsic.EnumGetName,
                ("System.Enum", "InternalGetValues") when method.GenericArity == 1 =>
                    RuntimeIntrinsic.EnumGetValues,
                ("System.Enum", "InternalGetValues") => RuntimeIntrinsic.EnumGetValues,
                ("System.Enum", "InternalGetValuesAsUnderlyingType") when method.GenericArity == 1 =>
                    RuntimeIntrinsic.EnumGetValues,
                ("System.Enum", "InternalGetValuesAsUnderlyingType") => RuntimeIntrinsic.EnumGetValues,
                ("System.Enum", "InternalIsDefined") when method.GenericArity == 1 =>
                    RuntimeIntrinsic.EnumIsDefined,
                ("System.Enum", "InternalIsDefined") => RuntimeIntrinsic.EnumIsDefined,
                ("System.Enum", "InternalGetMetadata") when method.GenericArity is 0 or 1 =>
                    RuntimeIntrinsic.EnumGetMetadata,
                ("System.Enum", "InternalGetUnderlyingType") => RuntimeIntrinsic.EnumGetUnderlyingType,
                ("System.Enum", "ToString") => RuntimeIntrinsic.EnumToString,
                ("System.Enum", "InternalToString") => RuntimeIntrinsic.EnumToString,
                ("System.Enum", "InternalFormat") => RuntimeIntrinsic.EnumFormat,
                ("System.Enum", "InternalToObject") => RuntimeIntrinsic.EnumToObject,
                ("System.Enum", "InternalToBoolean") => RuntimeIntrinsic.EnumConvert,
                ("System.Enum", "InternalToChar") => RuntimeIntrinsic.EnumConvert,
                ("System.Enum", "InternalToSByte") => RuntimeIntrinsic.EnumConvert,
                ("System.Enum", "InternalToByte") => RuntimeIntrinsic.EnumConvert,
                ("System.Enum", "InternalToInt16") => RuntimeIntrinsic.EnumConvert,
                ("System.Enum", "InternalToUInt16") => RuntimeIntrinsic.EnumConvert,
                ("System.Enum", "InternalToInt32") => RuntimeIntrinsic.EnumConvert,
                ("System.Enum", "InternalToUInt32") => RuntimeIntrinsic.EnumConvert,
                ("System.Enum", "InternalToInt64") => RuntimeIntrinsic.EnumConvert,
                ("System.Enum", "InternalToUInt64") => RuntimeIntrinsic.EnumConvert,
                ("System.Enum", "InternalToSingle") => RuntimeIntrinsic.EnumConvert,
                ("System.Enum", "InternalToDouble") => RuntimeIntrinsic.EnumConvert,
                ("System.Enum", "InternalToDecimal") => RuntimeIntrinsic.EnumConvert,
                ("System.Enum", "InternalToDateTime") => RuntimeIntrinsic.EnumConvert,
                ("System.Enum", "System.IConvertible.ToBoolean") => RuntimeIntrinsic.EnumConvert,
                ("System.Enum", "System.IConvertible.ToChar") => RuntimeIntrinsic.EnumConvert,
                ("System.Enum", "System.IConvertible.ToSByte") => RuntimeIntrinsic.EnumConvert,
                ("System.Enum", "System.IConvertible.ToByte") => RuntimeIntrinsic.EnumConvert,
                ("System.Enum", "System.IConvertible.ToInt16") => RuntimeIntrinsic.EnumConvert,
                ("System.Enum", "System.IConvertible.ToUInt16") => RuntimeIntrinsic.EnumConvert,
                ("System.Enum", "System.IConvertible.ToInt32") => RuntimeIntrinsic.EnumConvert,
                ("System.Enum", "System.IConvertible.ToUInt32") => RuntimeIntrinsic.EnumConvert,
                ("System.Enum", "System.IConvertible.ToInt64") => RuntimeIntrinsic.EnumConvert,
                ("System.Enum", "System.IConvertible.ToUInt64") => RuntimeIntrinsic.EnumConvert,
                ("System.Enum", "System.IConvertible.ToSingle") => RuntimeIntrinsic.EnumConvert,
                ("System.Enum", "System.IConvertible.ToDouble") => RuntimeIntrinsic.EnumConvert,
                (("System.IntPtr", "get_Size") or ("System.UIntPtr", "get_Size")) =>
                    RuntimeIntrinsic.NativeIntegerSize,
                ("System.BitConverter", "SingleToInt32Bits") =>
                    RuntimeIntrinsic.SingleToInt32Bits,
                ("System.BitConverter", "Int32BitsToSingle") =>
                    RuntimeIntrinsic.Int32BitsToSingle,
                ("System.BitConverter", "DoubleToInt64Bits") =>
                    RuntimeIntrinsic.DoubleToInt64Bits,
                ("System.BitConverter", "Int64BitsToDouble") =>
                    RuntimeIntrinsic.Int64BitsToDouble,
                ("System.Numerics.BitOperations", "LeadingZeroCount") =>
                    RuntimeIntrinsic.IntegerLeadingZeroCount,
                ("System.Numerics.BitOperations", "Log2") =>
                    RuntimeIntrinsic.IntegerLog2,
                ("System.Numerics.BitOperations", "PopCount") =>
                    RuntimeIntrinsic.IntegerPopulationCount,
                ("System.Numerics.BitOperations", "TrailingZeroCount") =>
                    RuntimeIntrinsic.IntegerTrailingZeroCount,
                ("System.Numerics.BitOperations", "RotateLeft") =>
                    RuntimeIntrinsic.IntegerRotateLeft,
                ("System.Numerics.BitOperations", "RotateRight") =>
                    RuntimeIntrinsic.IntegerRotateRight,
                (var declaringType, "DivRem")
                    when IsIntegralDivRem(declaringType, method) =>
                    RuntimeIntrinsic.IntegerDivRem,
                (("System.Math", "Abs") or ("System.MathF", "Abs"))
                    when IsUnaryFloating(method) =>
                    RuntimeIntrinsic.FloatingAbsolute,
                (("System.Math", "Ceiling") or ("System.MathF", "Ceiling"))
                    when IsUnaryFloating(method) =>
                    RuntimeIntrinsic.FloatingCeiling,
                (("System.Math", "Floor") or ("System.MathF", "Floor"))
                    when IsUnaryFloating(method) =>
                    RuntimeIntrinsic.FloatingFloor,
                (("System.Math", "Truncate") or ("System.MathF", "Truncate"))
                    when IsUnaryFloating(method) =>
                    RuntimeIntrinsic.FloatingTruncate,
                (("System.Math", "Round") or ("System.MathF", "Round"))
                    when IsUnaryFloating(method) =>
                    RuntimeIntrinsic.FloatingRound,
                (("System.Math", "Sqrt") or ("System.MathF", "Sqrt"))
                    when IsUnaryFloating(method) =>
                    RuntimeIntrinsic.FloatingSquareRoot,
                ("System.Math", "CopySign") when IsFloatingBinary(method, CliValueKind.F8) =>
                    RuntimeIntrinsic.FloatingCopySign,
                ("System.MathF", "CopySign") when IsFloatingBinary(method, CliValueKind.F4) =>
                    RuntimeIntrinsic.FloatingCopySign,
                ("System.Math", "Max") when IsFloatingBinary(method, CliValueKind.F8) ||
                    IsFloatingBinary(method, CliValueKind.F4) => RuntimeIntrinsic.FloatingMaximum,
                ("System.MathF", "Max") when IsFloatingBinary(method, CliValueKind.F4) =>
                    RuntimeIntrinsic.FloatingMaximum,
                ("System.Math", "Min") when IsFloatingBinary(method, CliValueKind.F8) ||
                    IsFloatingBinary(method, CliValueKind.F4) => RuntimeIntrinsic.FloatingMinimum,
                ("System.MathF", "Min") when IsFloatingBinary(method, CliValueKind.F4) =>
                    RuntimeIntrinsic.FloatingMinimum,
                (PackedSimdInstructions.DeclaringType, "get_IsSupported") => RuntimeIntrinsic.PackedSimdIsSupported,
                (PackedSimdInstructions.DeclaringType, _) when PackedSimdInstructions.TryGet(method, out _) =>
                    RuntimeIntrinsic.PackedSimdInstruction,
                (var declaringType, _) => SimdVectorIntrinsicClassifier.Classify(declaringType, method),
            };
            if (intrinsic is not null)
            {
                intrinsics.Add(method.Key, intrinsic.Value);
            }
        }
        _intrinsics = intrinsics;

        static bool IsUnaryFloating(MethodDefinitionModel method) =>
            method.Signature.ParameterTypes.Length == 1 &&
            method.Signature.ParameterTypes[0] is CliValueKind.F4 or CliValueKind.F8;

        static bool IsFloatingBinary(MethodDefinitionModel method, CliValueKind kind) =>
            method.IsStatic &&
            method.GenericArity == 0 &&
            method.Signature.ReturnType == kind &&
            method.Signature.ParameterTypes.AsSpan().SequenceEqual([kind, kind]);

        static bool IsStringCharacterAt(MethodDefinitionModel method) =>
            method.Signature.ParameterTypes.AsSpan().SequenceEqual([CliValueKind.I4]) &&
            method.Signature.ReturnType == CliValueKind.I4;

        static bool IsStringEqualsHelper(MethodDefinitionModel method) =>
            method.IsStatic &&
            method.Signature.ParameterTypes.AsSpan().SequenceEqual(
                [CliValueKind.ManagedReference, CliValueKind.ManagedReference]) &&
            method.Signature.ReturnType == CliValueKind.I4;

        static bool IsIntegralDivRem(
            string declaringType,
            MethodDefinitionModel method)
        {
            if (declaringType is not (
                    "System.Math" or
                    "System.SByte" or "System.Byte" or "System.Char" or
                    "System.Int16" or "System.UInt16" or
                    "System.Int32" or "System.UInt32" or
                    "System.Int64" or "System.UInt64" or
                    "System.IntPtr" or "System.UIntPtr") ||
                !method.IsStatic ||
                method.Signature.ReturnType != CliValueKind.ValueType ||
                method.Signature.ParameterSignatureTypes is not [var left, var right] ||
                !left.Equals(right) ||
                !IsIntegralPrimitive(left))
            {
                return false;
            }

            var result = method.Signature.ReturnSignatureType;
            return result.TypeArguments is [var quotient, var remainder] &&
                quotient.Equals(left) &&
                remainder.Equals(left);
        }

        static bool IsIntegralPrimitive(CliTypeIdentity type) =>
            IntegralPrimitiveNames.Contains(type.CanonicalName);
    }

    public bool TryGetIntrinsic(EntityKey method, out RuntimeIntrinsic intrinsic) =>
        _intrinsics.TryGetValue(method, out intrinsic);
}
