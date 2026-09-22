using System;
using System.Collections.Immutable;

namespace NetWasm.Compiler.Core;

public readonly record struct ObjectLayout(
    int TypeId,
    int Size,
    ImmutableArray<int> ReferenceOffsets);
public readonly record struct ValueLayout(
    CliTypeIdentity Type,
    int Size,
    int Alignment,
    ImmutableArray<int> ReferenceOffsets)
{
    public bool ContainsReferences => !ReferenceOffsets.IsEmpty;
    public bool IsFlattenedAbiEligible => !ContainsReferences && Size is > 0 and <= 8;
}
public readonly record struct FieldLayout(int Offset)
{
    public int Size { get; init; } = sizeof(int);
    public CliTypeIdentity? Type { get; init; }
}
public readonly record struct StaticFieldLayout(int Address);
public readonly record struct StringLayout(int Address, int Length, int DataOffset);
public enum ManagedExceptionKind
{
    NullReference,
    IndexOutOfRange,
    DivideByZero,
    Arithmetic,
    Overflow,
    InvalidCast,
    OutOfMemory,
    Argument,
    ArgumentNull,
    ArgumentOutOfRange,
    ArrayTypeMismatch,
    JSException,
}
public readonly record struct TypeDescriptorLayout(
    EntityKey Type,
    int TypeId,
    int BaseTypeId,
    int ObjectSize,
    int BitmapAddress,
    int BitmapBitCount,
    EntityKey? Finalizer)
{
    public int AssignableTypeIdsAddress { get; init; }
    public int AssignableTypeIdCount { get; init; }
    public bool IsInterface { get; init; }
}
public readonly record struct ConstructedTypeDescriptorLayout(
    CliTypeIdentity Type,
    int TypeId,
    int BaseTypeId,
    int ObjectSize,
    int BitmapAddress,
    int BitmapBitCount,
    string? Finalizer)
{
    public int AssignableTypeIdsAddress { get; init; }
    public int AssignableTypeIdCount { get; init; }
    public bool IsInterface { get; init; }
}
public readonly record struct ValueTypeDescriptorLayout(
    CliTypeIdentity Type,
    int TypeId,
    int Size,
    int BitmapAddress,
    int BitmapBitCount);

public interface ITargetLayout
{
    WasmTargetLayout Target { get; }
}

public interface IValueLayoutProvider
{
    ValueLayout GetValueLayout(CliTypeIdentity type);
}

public interface ITypeLayoutProvider
{
    ObjectLayout GetObjectLayout(CliTypeIdentity type);
    bool GetObjectLayout(CliTypeIdentity type, out ObjectLayout layout);
    ObjectLayout GetObjectLayout(EntityKey type);
    int ReferenceArrayTypeId { get; }
    int StringTypeId { get; }
    int TypeTypeId { get; }
}

public interface IInstanceFieldLayoutProvider
{
    FieldLayout GetFieldLayout(FieldInstanceModel field);
    FieldLayout GetFieldLayout(EntityKey field);
}

public interface IStaticFieldLayoutProvider
{
    StaticFieldLayout GetStaticFieldLayout(EntityKey field);
    StaticFieldLayout GetStaticFieldLayout(FieldInstanceModel field);
}

public interface IStaticDataLayout
{
    StringLayout GetStringLayout(string value);
    int StaticDataEnd { get; }
    ImmutableArray<int> StaticRootAddresses { get; }
    ImmutableArray<DataSegment> DataSegments { get; }
}

public interface IRuntimeObjectLayout
{
    int StringLengthOffset { get; }
    int StringDataOffset { get; }
    int ArrayLengthOffset { get; }
    int ArrayDataPointerOffset { get; }
    int ArrayElementTypeIdOffset { get; }
    int DelegateTargetOffset { get; }
    int DelegateMethodIdOffset { get; }
    int DelegateLeftOffset { get; }
    int DelegateRightOffset { get; }
}

public readonly record struct RectangularArrayLayout(
    int RankOffset,
    int ShapePointerOffset,
    int ObjectSize,
    int DimensionSize,
    int DimensionLengthOffset,
    int DimensionStrideOffset)
{
    public static RectangularArrayLayout Create(
        WasmTargetLayout target,
        int elementTypeIdOffset)
    {
        ArgumentNullException.ThrowIfNull(target);
        var rankOffset = WasmTargetLayout.Align(
            elementTypeIdOffset + sizeof(int),
            sizeof(int));
        var shapePointerOffset = WasmTargetLayout.Align(
            rankOffset + sizeof(int),
            target.ObjectReferenceAlignment);
        return new(
            rankOffset,
            shapePointerOffset,
            WasmTargetLayout.Align(
                shapePointerOffset + target.ObjectReferenceSize,
                target.ObjectReferenceAlignment),
            sizeof(int) * 2,
            0,
            sizeof(int));
    }
}

public interface IRectangularArrayLayoutProvider
{
    RectangularArrayLayout Provide();
}

public interface IManagedExceptionObjectProvider
{
    int GetExceptionObject(ManagedExceptionKind kind);
}

public interface ITypeDescriptorSource
{
    ImmutableArray<TypeDescriptorLayout> TypeDescriptors { get; }
    ImmutableArray<ConstructedTypeDescriptorLayout> ConstructedTypeDescriptors { get; }
    ImmutableArray<ValueTypeDescriptorLayout> ValueTypeDescriptors { get; }
}

public sealed record DataSegment(int Address, ImmutableArray<byte> Data);

public static class RuntimeAbi
{
    public const string ApplicationModule = "netwasm.application.v1";
    public const string ManagedFinalizerDispatcher = "netwasm.finalize";
    public const string RuntimeModule = "netwasm.runtime.v1";
    public const string HostModule = "netwasm.host.v1";
    public const int HostInteropAbiVersion = 1;
    public const string HostInteropStringLength = "interop_string_length";
    public const string HostInteropCopyStringUtf16 = "interop_copy_string_utf16";
    public const string HostInteropReleaseHandle = "interop_release_handle";
    public const string HostInteropReleaseSubscription = "interop_release_subscription";
    public const string HostInteropByteLength = "interop_byte_length";
    public const string HostInteropCopyBytes = "interop_copy_bytes";
    public const string RuntimeInitialize = "initialize";
    public const string RuntimeRegisterType = "register_type";
    public const string RuntimeRegisterValueType = "register_value_type";
    public const string RuntimeRegisterStaticRoot = "register_static_root";
    public const string RuntimeRootFrameEnter = "root_frame_enter";
    public const string RuntimeRootFrameLeave = "root_frame_leave";
    public const string RuntimeValueFrameEnter = "value_frame_enter";
    public const string RuntimeValueFrameLeave = "value_frame_leave";
    public const string RuntimeStackTraceFrameEnter = "stack_trace_frame_enter";
    public const string RuntimeStackTraceFrameLeave = "stack_trace_frame_leave";
    public const string RuntimeStackTraceInitialize = "stack_trace_initialize";
    public const string RuntimeExceptionFrameEnter = "exception_frame_enter";
    public const string RuntimeExceptionFrameLeave = "exception_frame_leave";
    public const string RuntimeExceptionFrameTargetClause = "exception_frame_target_clause";
    public const string RuntimeExceptionFrameSetEnvironment =
        "exception_frame_set_environment";
    public const string ManagedFilterDispatcher = "netwasm.filter";
    public const string RuntimeFinalizerSafepoint = "finalizer_safepoint";
    public const string RuntimeBeginThrow = "begin_throw";
    public const string RuntimeBeginRethrow = "begin_rethrow";
    public const string RuntimeAllocate = "allocate";
    public const string RuntimeCollect = "collect";
    public const string RuntimeAllocateReferenceArray = "allocate_reference_array";
    public const string RuntimeAllocateValueArray = "allocate_value_array";
    public const string RuntimeAllocateRectangularArray = "allocate_rectangular_array";
    public const string RuntimeArrayRank = "array_rank";
    public const string RuntimeArrayGetLength = "array_get_length";
    public const string RuntimeArrayCopy = "array_copy";
    public const string RuntimeArrayClear = "array_clear";
    public const string RuntimeArrayClone = "array_clone";
    public const string RuntimeAllocateString = "allocate_string";
    public const string RuntimeGetTypeObject = "get_type_object";
    public const string RuntimeHandleNew = "handle_new";
    public const string RuntimeHandleGet = "handle_get";
    public const string RuntimeHandleRelease = "handle_release";
    public const string RuntimeWeakHandleCreate = "weak_handle_new";
    public const string RuntimeWeakHandleGet = "weak_handle_get";
    public const string RuntimeWeakHandleSet = "weak_handle_set";
    public const string RuntimeWeakHandleRelease = "weak_handle_release";
    public const string RuntimeGcHandleCreate = "gc_handle_new";
    public const string RuntimeGcHandleGet = "gc_handle_get";
    public const string RuntimeGcHandleSet = "gc_handle_set";
    public const string RuntimeGcHandleRelease = "gc_handle_release";
    public const string RuntimeGcHandleAddress = "gc_handle_address";
    public const string RuntimeGcGetMetric = "gc_get_metric";
    public const string RuntimeGcMetricIsSupported = "gc_metric_is_supported";
    public const string RuntimeGcWaitForPendingFinalizers = "gc_wait_for_pending_finalizers";
    public const string RuntimeObjectIdentityHash = "object_identity_hash";
    public const string RuntimeComponentReallocate = "component_realloc";
    public const string RuntimeComponentFree = "component_free";
    public const string RuntimeNativeAlloc = "native_alloc";
    public const string RuntimeNativeRealloc = "native_realloc";
    public const string RuntimeNativeFree = "native_free";
    public const string RuntimeNativeAlignedAlloc = "native_aligned_alloc";
    public const string RuntimeNativeAlignedRealloc = "native_aligned_realloc";
    public const string RuntimeNativeAlignedFree = "native_aligned_free";
    public const string RuntimeSuppressFinalize = "suppress_finalize";
    public const string RuntimeReRegisterForFinalize = "reregister_for_finalize";
    public const string RuntimeReportUnobservedTaskException = "report_unobserved_task_exception";
    public const string RuntimeReportTerminalException = "report_terminal_exception_v1";
    public const string RuntimeIsAssignable = "is_assignable";
    public const string RuntimeEndCatch = "end_catch";
}
