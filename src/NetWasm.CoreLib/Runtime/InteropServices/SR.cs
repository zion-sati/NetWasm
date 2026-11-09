// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Generated from dotnet/runtime 474ed963699fc29809015392738a15231aaae2de resources;
// only the strings this namespace references are included.

namespace System.Runtime.InteropServices
{
    internal static class SR
    {
        internal const string Argument_NeedNonGenericType =
            "The specified Type must not be a generic type.";
        internal const string Argument_NeedNonGenericObject =
            "The specified object must not be an instance of a generic type.";
        internal const string Argument_StructMustNotBeValueClass =
            "The structure must not be a value class.";
        internal const string Argument_MustHaveLayoutOrBeBlittable =
            "The specified structure must be blittable or have layout information.";
        // NetWasm: upstream's Arg_CannotMarshal, Argument_OffsetOfFieldNotFound and
        // StructMarshalling_MissingInteropData name the type, which would publish every type's name.
        internal const string Arg_CannotMarshal =
            "The type cannot be marshaled as an unmanaged structure; no meaningful size or offset can be computed.";
        internal const string Argument_OffsetOfFieldNotFound =
            "Field passed in is not a marshaled member of the type.";
        internal const string StructMarshalling_MissingInteropData =
            "The type is missing structure marshalling data because the compiled program neither names nor allocates it.";
        internal const string PlatformNotSupported_StructMarshallingConversion =
            "Structure marshalling that converts fields (bool, char, references or formatted classes) is not available on this NetWasm platform.";
        internal const string PlatformNotSupported_StructMarshallingConstructor =
            "PtrToStructure(IntPtr, Type) cannot run a value type's parameterless constructor on this NetWasm platform; use PtrToStructure<T>.";
        internal const string AmbiguousMatch_MultipleEventInterfaceAttributes =
            "More than one ComEventInterfaceAttribute found for '{0}' on the declaring interface of the event.";
        internal const string Arg_InvalidThreshold =
            "maximumThreshold cannot be less than initialThreshold.";
        internal const string Arg_MustBeDecimal =
            "Object must be of type Decimal.";
        internal const string Arg_MustBeInt32 =
            "Object must be of type Int32.";
        internal const string Arg_NeedNonNegNumRequired =
            "Non-negative number required.";
        internal const string ArgumentException_NotIsomorphic =
            "Object contains references.";
        internal const string Argument_StructArrayTooLarge =
            "Array size exceeds addressing limitations.";
        internal const string IndexOutOfRange_ArrayWithOffset =
            "ArrayWithOffset: offset exceeds array size.";
        internal const string InvalidOperation_HCCountOverflow =
            "Handle collector count overflows or underflows.";
        internal const string InvalidOperation_NoComEventInterfaceAttribute =
            "Event invocation for COM objects requires the declaring interface of the event to be attributed with ComEventInterfaceAttribute.";
        internal const string InvalidOperation_NoDispIdAttribute =
            "Event invocation for COM objects requires event to be attributed with DispIdAttribute.";
        internal const string Arg_BufferTooSmall =
            "Not enough space available in the buffer.";
        internal const string ArgumentOutOfRange_AddressSpace =
            "The number of bytes cannot exceed the virtual address space on a 32 bit machine.";
        internal const string ArgumentOutOfRange_UIntPtrMax =
            "The length of the buffer must be less than the maximum UIntPtr value for your platform.";
        internal const string Argument_InvalidOffLen =
            "Offset and length were out of bounds for the array or count is greater than the number of elements from index to the end of the source collection.";
        internal const string InvalidOperation_MustCallInitialize =
            "You must call Initialize on this object instance before using it.";
        internal const string PlatformNotSupported_ComInterop =
            "COM Interop is not supported on this platform.";

        // NetWasm strings for its interop boundaries.
        internal const string PlatformNotSupported_ArrayElementAddress =
            "NetWasm resolves element addresses of non-generic arrays only for primitive element types; use UnsafeAddrOfPinnedArrayElement<T>.";
        internal const string PlatformNotSupported_Prelink =
            "NetWasm binds native imports when it compiles the application, so there is nothing to prelink at run time.";
        internal const string PlatformNotSupported_TypeGuid =
            "Type GUID and ProgID discovery reads type metadata through reflection, which NetWasm does not support.";
        internal const string PlatformNotSupported_EventReflection =
            "COM-aware event lookup reads the event through reflection, which NetWasm does not support.";
        internal const string PlatformNotSupported_DelegateMarshalling =
            "NetWasm does not convert between delegates and native function pointers at run time.";
        internal const string PlatformNotSupported_LastPInvokeError =
            "NetWasm keeps no per-call last-error state for native imports.";

        internal static string Format(string format, object? arg0) =>
            string.Format(format, arg0);

        internal static string Format(string format, object? arg0, object? arg1) =>
            string.Format(format, arg0, arg1);

        internal static string Format(string format, object? arg0, object? arg1, object? arg2) =>
            string.Format(format, arg0, arg1, arg2);
    }
}
