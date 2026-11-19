// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Generated from dotnet/runtime deae2e32bf0e018e09d64597b7c59b62b73bb3ce resources;
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
            "Structure marshalling of this field is not available on this NetWasm platform.";
        internal const string PlatformNotSupported_StructMarshallingDelegate =
            "Structure marshalling converts a delegate field to a function pointer only when it is null on this NetWasm platform.";
        internal const string Argument_WrongSizeArrayInNativeStruct =
            "Type could not be marshaled because the length of an embedded array instance does not match the declared length in the layout.";
        internal const string InvalidOperation_SafeHandleFieldsNotAllowed =
            "Structures containing SafeHandle fields are not allowed in this operation.";
        internal const string NotSupported_SafeHandleField =
            "SafeHandle fields cannot be created from an unmanaged handle.";
        internal const string NotSupported_CriticalHandleField =
            "CriticalHandle fields cannot be created from an unmanaged handle.";
        // NetWasm: upstream's message names the type and its assembly.
        internal const string TypeLoad_InvalidFormat =
            "Could not load the type because the format is invalid.";
        // NetWasm: upstream's message names the field and type.
        internal const string Marshaler_UnmarshalableField =
            "Cannot marshal a field of the structure: Invalid managed/unmanaged type combination.";
        // NetWasm: upstream's Activator_CannotCreateInstance names the type.
        internal const string Activator_NoParameterlessConstructor =
            "Cannot dynamically create an instance of the type. Reason: No parameterless constructor defined.";
        internal const string Arg_MustBeDecimal =
            "Object must be of type Decimal.";
        internal const string Arg_MustBeInt32 =
            "Object must be of type Int32.";
        internal const string Arg_MustBeNFloat =
            "Object must be of type NFloat.";
        internal const string ArgumentException_NotIsomorphic =
            "Object contains references.";
        internal const string Argument_StructArrayTooLarge =
            "Array size exceeds addressing limitations.";
        internal const string IndexOutOfRange_ArrayWithOffset =
            "ArrayWithOffset: offset exceeds array size.";
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
        internal const string PlatformNotSupported_DelegateMarshalling =
            "NetWasm does not convert between delegates and native function pointers at run time.";

        internal const string ComVariant_SafeArray_PlatformNotSupported =
            "ComVariants containing SAFEARRAYs are not supported on this platform";
        internal const string ComVariant_SizeMustMatchVariantSize =
            "Size of {0} should be the same size as the value specified by {1}";
        internal const string ComVariant_TypeIsNotSupportedType =
            "Variant type '{0}' is not one of the supported variant types for this operation: [{1}]";
        internal const string ComVariant_VT_DECIMAL_NotSupported_CreateRaw =
            "VT_DECIMAL is not supported in CreateRaw. Use the Create method.";
        internal const string ComVariant_VT_DECIMAL_NotSupported_RawDataRef =
            "VT_DECIMAL is not supported in GetRawDataRef. Use the As method.";
        internal const string ComVariant_VT_VARIANT_In_Variant =
            "VT_VARIANT is not supported in variants.";
        internal const string UnsupportedType =
            "Unsupported type";
        internal const string Arg_EntryPointNotFoundExceptionParameterizedNoLibrary =
            "Unable to find an entry point named '{0}' in native library.";
        internal const string InvalidOperation_CannotRegisterSecondResolver =
            "A resolver is already set for the assembly.";
        // NetWasm: native libraries are linked when the program is built, so none loads while it runs.
        internal const string DllNotFound_NetWasm =
            "Unable to load shared library '{0}': native libraries are linked into the program when it is built, and none can be loaded while it runs.";
        internal static string Format(string format, object? arg0) =>
            string.Format(format, arg0);

        internal static string Format(string format, object? arg0, object? arg1) =>
            string.Format(format, arg0, arg1);

        internal static string Format(string format, object? arg0, object? arg1, object? arg2) =>
            string.Format(format, arg0, arg1, arg2);
    }
}
