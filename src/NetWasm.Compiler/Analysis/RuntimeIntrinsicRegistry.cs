using System;
using System.Collections.Generic;
using NetWasm.Compiler;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Analysis;

public sealed class RuntimeIntrinsicRegistry : IRuntimeIntrinsicRegistry
{
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
                ("System.Array", "InternalCopy") => RuntimeIntrinsic.ArrayCopy,
                ("System.Array", "InternalClear") => RuntimeIntrinsic.ArrayClear,
                ("System.Array", "InternalClone") => RuntimeIntrinsic.ArrayClone,
                ("System.String", "get_Chars") when IsStringCharacterAt(method) =>
                    RuntimeIntrinsic.StringCharacterAt,
                ("System.String", "SetCharUnchecked") =>
                    RuntimeIntrinsic.StringSetCharacterUnchecked,
                ("System.GCCollectionRuntime", "Collect") => RuntimeIntrinsic.GcCollect,
                ("System.GC", "SuppressFinalize") => RuntimeIntrinsic.SuppressFinalize,
                ("System.GC", "ReRegisterForFinalize") => RuntimeIntrinsic.ReRegisterForFinalize,
                ("System.WeakReferenceRuntime", "Create") => RuntimeIntrinsic.WeakHandleCreate,
                ("System.WeakReferenceRuntime", "Get") => RuntimeIntrinsic.WeakHandleGet,
                ("System.WeakReferenceRuntime", "Set") => RuntimeIntrinsic.WeakHandleSet,
                ("System.WeakReferenceRuntime", "Release") => RuntimeIntrinsic.WeakHandleRelease,
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
                ("System.ValueType", "Equals") => RuntimeIntrinsic.ValueTypeEquals,
                ("System.ValueType", "GetHashCode") =>
                    RuntimeIntrinsic.ValueTypeGetHashCode,
                ("System.Threading.Tasks.TaskDiagnostics", "ReportUnobservedException") =>
                    RuntimeIntrinsic.ReportUnobservedTaskException,
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
                ("System.Enum", "InternalParse") when method.GenericArity is 0 or 1 =>
                    RuntimeIntrinsic.EnumParse,
                ("System.Enum", "InternalTryParse") when method.GenericArity is 0 or 1 =>
                    RuntimeIntrinsic.EnumParse,
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
                ("System.Enum", "InternalToType") => RuntimeIntrinsic.EnumConvert,
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
                ("System.Enum", "System.IConvertible.ToType") => RuntimeIntrinsic.EnumConvert,
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
                _ => null,
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

        static bool IsStringCharacterAt(MethodDefinitionModel method) =>
            method.Signature.ParameterTypes.AsSpan().SequenceEqual([CliValueKind.I4]) &&
            method.Signature.ReturnType == CliValueKind.I4;
    }

    public bool TryGetIntrinsic(EntityKey method, out RuntimeIntrinsic intrinsic) =>
        _intrinsics.TryGetValue(method, out intrinsic);
}
