// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Ported from dotnet/runtime deae2e32bf0e018e09d64597b7c59b62b73bb3ce: the structure-layout
// members of Marshal.cs with native AOT's helpers (Marshal.NativeAot.cs) and struct interop data
// lookups (RuntimeInteropData.cs). NetWasm adaptations: the compiler publishes a structure-
// marshalling descriptor for each type the program names, or allocates as a boxed value, in place
// of native AOT's interop data, and generates no marshalling stubs. Blittable layouts are copied
// as upstream copies a layout without stubs; a struct that holds a bool, char, string, delegate or
// inline array, and a formatted class, convert field by field to and from the native layout desktop
// .NET's runtime marshalling gives them on Unix, whose size and field offsets SizeOf and OffsetOf
// report: strings as UTF-8 (ANSI and LPUTF8Str) or UTF-16 copies in CoTaskMem, which
// DestroyStructure frees, or inline (ByValTStr); ANSI characters in a ByValArray as UTF-8 in
// SizeConst bytes; an embedded formatted class, and a ByValArray of structures, inline through the
// descriptor of their own type; a CriticalHandle as its handle; a delegate field only when null. A
// field desktop rejects throws what desktop throws, as a SafeHandle field does in both directions and
// a formatted class on a base without layout does everywhere; StructureToPtr then leaves what desktop
// leaves, the native structure cleared, no field written and no string allocated, embedded classes
// and arrays of structures included. One NetWasm does not convert throws
// PlatformNotSupportedException before any memory access, before fDeleteOld frees anything and before
// PtrToStructure changes the structure it fills. Every NetWasm Type is a runtime
// type. Both PtrToStructure overloads create the structure as Activator.CreateInstance(Type,
// nonPublic: true) does on desktop: its parameterless constructor runs, public or not, which the
// compiler dispatches to by the type's ID; a value type without one is zero, and a class without
// one throws MissingMethodException. The native data then overwrites every marshalled field. The
// type-naming messages drop the type name, which would otherwise publish every type's name.

using System.ComponentModel;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace System.Runtime.InteropServices
{
    public static partial class Marshal
    {
        [RequiresDynamicCode("Marshalling code for the object might not be available. Use the SizeOf<T> overload instead.")]
        [EditorBrowsable(EditorBrowsableState.Never)]
        public static int SizeOf(object structure)
        {
            ArgumentNullException.ThrowIfNull(structure);

            return SizeOfHelper(structure.GetType(), throwIfNotMarshalable: true);
        }

        public static int SizeOf<T>(T structure)
        {
            ArgumentNullException.ThrowIfNull(structure);

            return SizeOfHelper(structure.GetType(), throwIfNotMarshalable: true);
        }

        [RequiresDynamicCode("Marshalling code for the object might not be available. Use the SizeOf<T> overload instead.")]
        [EditorBrowsable(EditorBrowsableState.Never)]
        public static int SizeOf(Type t)
        {
            ArgumentNullException.ThrowIfNull(t);

            if (t.IsGenericType)
            {
                throw new ArgumentException(SR.Argument_NeedNonGenericType, nameof(t));
            }

            return SizeOfHelper(t, throwIfNotMarshalable: true);
        }

        public static int SizeOf<T>()
        {
            Type t = typeof(T);
            if (t.IsGenericType)
            {
                throw new ArgumentException(SR.Argument_NeedNonGenericType, nameof(T));
            }

            return SizeOfHelper(t, throwIfNotMarshalable: true);
        }

        public static IntPtr OffsetOf<T>(string fieldName) => OffsetOf(typeof(T), fieldName);

        [EditorBrowsable(EditorBrowsableState.Never)]
        public static IntPtr OffsetOf(Type t, string fieldName)
        {
            ArgumentNullException.ThrowIfNull(t);

            if (string.IsNullOrEmpty(fieldName))
                throw new ArgumentNullException(nameof(fieldName));

            if (t.IsGenericTypeDefinition)
                throw new ArgumentException(SR.Argument_NeedNonGenericType, nameof(t));

            return new IntPtr(StructureMarshallingData.GetFieldOffset(t, fieldName));
        }

        [UnconditionalSuppressMessage("AotAnalysis", "IL3050:AotUnfriendlyApi",
            Justification = "AOT compilers can see the T.")]
        public static void StructureToPtr<T>([DisallowNull] T structure, IntPtr ptr, bool fDeleteOld)
        {
            StructureToPtr((object)structure!, ptr, fDeleteOld);
        }

        /// <summary>
        /// Creates a new instance of <paramref name="structureType"/> and marshals data from a
        /// native memory block to it.
        /// </summary>
        [RequiresDynamicCode("Marshalling code for the object might not be available")]
        [EditorBrowsable(EditorBrowsableState.Never)]
        public static object? PtrToStructure(IntPtr ptr,
            [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.NonPublicConstructors)]
            Type structureType)
        {
            ArgumentNullException.ThrowIfNull(structureType);

            if (ptr == IntPtr.Zero)
            {
                return null;
            }

            if (structureType.IsGenericType)
            {
                throw new ArgumentException(SR.Argument_NeedNonGenericType, nameof(structureType));
            }

            object structure = StructureMarshallingData.CreateStructure(structureType);
            PtrToStructureHelper(ptr, structure, allowValueClasses: true);
            return structure;
        }

        public static T? PtrToStructure<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.NonPublicConstructors)] T>(IntPtr ptr)
        {
            if (ptr == IntPtr.Zero)
            {
                // Compat: this was originally implemented as a call to the non-generic version+cast.
                // It would throw for non-nullable valuetypes here and return null for Nullable<T> even
                // though it's generic.
                return (T)(object)null!;
            }

            Type structureType = typeof(T);
            if (structureType.IsGenericType)
            {
                throw new ArgumentException(SR.Argument_NeedNonGenericType, nameof(T));
            }

            // As desktop's, which creates it through the Type overload: the same constructor, public or not.
            object structure = StructureMarshallingData.CreateStructure(structureType);
            PtrToStructureHelper(ptr, structure, allowValueClasses: true);
            return (T)structure;
        }

        [UnconditionalSuppressMessage("AotAnalysis", "IL3050:AotUnfriendlyApi",
            Justification = "AOT compilers can see the T.")]
        public static void DestroyStructure<T>(IntPtr ptr) => DestroyStructure(ptr, typeof(T));

        internal static int SizeOfHelper(Type t, bool throwIfNotMarshalable)
        {
            Debug.Assert(throwIfNotMarshalable);

            if (t.IsPointer || t.IsFunctionPointer)
                return IntPtr.Size;

            if (t.IsByRef || t.IsArray || t.ContainsGenericParameters)
                throw new ArgumentException(SR.Arg_CannotMarshal);

            return StructureMarshallingData.Get(t).Size;
        }

        /// <summary>
        /// Marshals data from a native memory block to a preallocated structure class.
        /// </summary>
        [RequiresDynamicCode("Marshalling code for the object might not be available")]
        [EditorBrowsable(EditorBrowsableState.Never)]
        public static void PtrToStructure(IntPtr ptr, object structure)
        {
            PtrToStructureHelper(ptr, structure, allowValueClasses: false);
        }

        public static void PtrToStructure<T>(IntPtr ptr, [DisallowNull] T structure)
        {
            PtrToStructureHelper(ptr, structure!, allowValueClasses: false);
        }

        private static void PtrToStructureHelper(IntPtr ptr, object structure, bool allowValueClasses)
        {
            ArgumentNullException.ThrowIfNull(structure);
            if (!allowValueClasses && structure.GetType().IsValueType)
            {
                throw new ArgumentException(SR.Argument_StructMustNotBeValueClass, nameof(structure));
            }

            PtrToStructureImpl(ptr, structure);
        }

        internal static unsafe void PtrToStructureImpl(IntPtr ptr, object structure)
        {
            StructureMarshallingData data = StructureMarshallingData.Get(structure.GetType());
            data.ThrowIfNotConvertible(nameof(structure));
            int size = data.NativeSize;
            if (data.Converts)
            {
                data.ThrowIfUnsupportedNative((byte*)ptr);
                data.FromNative((byte*)ptr, structure);
            }
            else
                Buffer.MemoryCopy((void*)ptr, data.GetPayload(structure), size, size);
            GC.KeepAlive(structure);
        }

        [RequiresDynamicCode("Marshalling code for the object might not be available. Use the DestroyStructure<T> overload instead.")]
        [EditorBrowsable(EditorBrowsableState.Never)]
        public static unsafe void DestroyStructure(IntPtr ptr, Type structuretype)
        {
            ArgumentNullException.ThrowIfNull(ptr);
            ArgumentNullException.ThrowIfNull(structuretype);

            if (structuretype.IsGenericType)
                throw new ArgumentException(SR.Argument_NeedNonGenericType, nameof(structuretype));

            if (structuretype.IsEnum ||
                structuretype.IsInterface ||
                typeof(Delegate).IsAssignableFrom(structuretype))
            {
                throw new ArgumentException(SR.Argument_MustHaveLayoutOrBeBlittable);
            }

            // A blittable layout has no destroy stub, so there is nothing to release; a converted one frees the
            // native strings StructureToPtr allocated.
            StructureMarshallingData data = StructureMarshallingData.Get(structuretype);
            data.ThrowIfNotConvertible(nameof(structuretype));
            if (data.Converts)
                data.Destroy((byte*)ptr);
        }

        [RequiresDynamicCode("Marshalling code for the object might not be available. Use the StructureToPtr<T> overload instead.")]
        [EditorBrowsable(EditorBrowsableState.Never)]
        public static unsafe void StructureToPtr(object structure, IntPtr ptr, bool fDeleteOld)
        {
            ArgumentNullException.ThrowIfNull(structure);
            ArgumentNullException.ThrowIfNull(ptr);

            Type structureType = structure.GetType();
            if (structureType.IsGenericType)
            {
                throw new ArgumentException(SR.Argument_NeedNonGenericObject, nameof(structure));
            }

            StructureMarshallingData data = StructureMarshallingData.Get(structureType);
            data.ThrowIfNotConvertible(nameof(structure));
            // What NetWasm does not convert throws before fDeleteOld frees the old strings, which would otherwise
            // leave the native structure pointing at freed memory.
            if (data.Converts)
                data.ThrowIfUnsupported(structure);
            if (fDeleteOld)
            {
                DestroyStructure(ptr, structureType);
            }

            int size = data.NativeSize;
            if (data.Converts)
                data.ToNative(structure, (byte*)ptr);
            else
                Buffer.MemoryCopy(data.GetPayload(structure), (void*)ptr, size, size);
            GC.KeepAlive(structure);
        }
    }

    /// <summary>
    /// Read-only view of the compiler's structure-marshalling descriptor, version 1: version,
    /// flags, native size, payload offset, field count and conversion step count, then the field
    /// offset and name tables. A structure that converts has a native layout, as desktop .NET's
    /// runtime marshalling lays it out on Unix: its size and field offsets are native, and its
    /// conversion steps (kind, managed offset, native offset, size, count, element) follow the
    /// field offsets. Managed offsets count from the payload: a boxed struct's value, or a class's
    /// object. The compiler owns its storage and the referenced static strings.
    /// </summary>
    internal readonly unsafe struct StructureMarshallingData
    {
        private const int InvalidLayout = 1;
        private const int RequiresConversion = 2;
        private const int ValueType = 4;
        private const int DefaultConstructor = 8;
        private const int NativeLayout = 16;
        private const int UnmarshalableField = 32;
        private const int InvalidType = 64;
        private const int StepInts = 6;
        private readonly byte* _address;

        private StructureMarshallingData(byte* address) => _address = address;

        private int Flags => *(int*)(_address + 4);
        private int Count => *(int*)(_address + 16);
        private int StepCount => *(int*)(_address + 20);
        private int* FirstStep => *(int**)(_address + 24) + Count;

        // Whether the structure converts between its managed value and its native layout, field by field.
        internal bool Converts => (Flags & NativeLayout) != 0;

        // The native size, for a layout the converting members accept.
        internal int NativeSize => *(int*)(_address + 8);

        // The step kinds the compiler writes (NativeConversionKind) and an inline array's element types
        // (NativeArrayElement).
        private enum Step
        {
            Copy,
            Boolean,
            Ansi,
            Utf8String,
            Utf16String,
            Utf8Inline,
            Utf16Inline,
            Delegate,
            ArrayCopy,
            ArrayBoolean,
            ArrayAnsi,
            SafeHandle,
            CriticalHandle,
            EmbeddedClass,
            ArrayStructure,
            ArrayEnum,
        }

        // StructureToPtr's copy into the native layout: padding and gaps zero, each field copied, a bool as a native
        // boolean of its size (true as 1), a char as one ANSI byte, a string as a CoTaskMem copy or inline (truncated
        // to its size, then terminated), a null delegate as a null function pointer, and an inline array's first
        // elements, as desktop .NET's runtime marshalling converts them on Unix.
        // A field that fails throws what desktop throws, as desktop does: with the native structure cleared, before
        // any field is written or string allocated (so nothing leaks), whichever field fails.
        internal void ToNative(object structure, byte* native)
        {
            byte* managed = GetPayload(structure);
            if (Unwritable(managed) is { } failure)
            {
                new Span<byte>(native, NativeSize).Clear();
                throw failure;
            }
            ToNative(managed, native);
        }

        // What NetWasm does not convert, a non-null delegate, anywhere in the structure.
        internal void ThrowIfUnsupported(object structure) => ThrowIfUnsupportedFields(GetPayload(structure));

        private void ThrowIfUnsupportedFields(byte* managed)
        {
            int* step = FirstStep;
            for (int index = 0; index < StepCount; index++, step += StepInts)
            {
                byte* from = managed + step[1];
                switch ((Step)step[0])
                {
                    case Step.Delegate when Unsafe.AsRef<object?>(from) is not null:
                        throw new PlatformNotSupportedException(SR.PlatformNotSupported_StructMarshallingDelegate);
                    case Step.EmbeddedClass when Unsafe.AsRef<object?>(from) is { } embedded:
                        StructureMarshallingData nested = Of(step[5]);
                        if (nested.Converts)
                            nested.ThrowIfUnsupportedFields(nested.GetPayload(embedded));
                        break;
                    case Step.ArrayStructure when Unsafe.AsRef<Array?>(from) is { } array:
                        StructureMarshallingData element = Of(step[5]);
                        for (int item = 0; element.Converts && item < Math.Min(array.Length, step[4]); item++)
                            element.ThrowIfUnsupportedFields(element.GetPayload(array.GetValue(item)!));
                        break;
                }
            }
        }

        // The exception the first field that cannot be written throws, in field order, or null: a SafeHandle, a
        // null CriticalHandle, an inline array shorter than its size, characters that do not fit their bytes, and
        // the same in an embedded class or an array's structures.
        private Exception? Unwritable(byte* managed)
        {
            int* step = FirstStep;
            for (int index = 0; index < StepCount; index++, step += StepInts)
            {
                byte* from = managed + step[1];
                int count = step[4];
                Exception? failure = (Step)step[0] switch
                {
                    Step.SafeHandle => new InvalidOperationException(SR.InvalidOperation_SafeHandleFieldsNotAllowed),
                    Step.CriticalHandle when Unsafe.AsRef<CriticalHandle?>(from) is null => new NullReferenceException(),
                    Step.Delegate or Step.Copy or Step.Boolean or Step.Ansi or Step.Utf8String or Step.Utf16String or
                        Step.Utf16Inline or Step.CriticalHandle => null,
                    Step.Utf8Inline => Unsafe.AsRef<string?>(from) is { } text ? FitsUtf8(text.AsSpan(0, Math.Min(text.Length, count - 1)), count) : null,
                    Step.EmbeddedClass => Unsafe.AsRef<object?>(from) is { } embedded && Of(step[5]) is { Converts: true } nested
                        ? nested.Unwritable(nested.GetPayload(embedded))
                        : null,
                    Step.ArrayStructure => UnwritableStructures(Unsafe.AsRef<Array?>(from), Of(step[5]), count),
                    _ => Unsafe.AsRef<Array?>(from) is not { } array ? null
                        : array.Length < count ? new ArgumentException(SR.Argument_WrongSizeArrayInNativeStruct)
                        : (Step)step[0] == Step.ArrayAnsi ? FitsUtf8(((char[])array).AsSpan(0, count), count)
                        : null,
                };
                if (failure is not null)
                    return failure;
            }
            return null;
        }

        private static Exception? UnwritableStructures(Array? array, StructureMarshallingData element, int count)
        {
            if (array is null)
                return null;
            if (array.Length < count)
                return new ArgumentException(SR.Argument_WrongSizeArrayInNativeStruct);
            for (int index = 0; element.Converts && index < count; index++)
            {
                if (element.Unwritable(element.GetPayload(array.GetValue(index)!)) is { } failure)
                    return failure;
            }
            return null;
        }

        // Characters written as UTF-8 into count bytes: the exception the encoder throws when they do not fit.
        private static Exception? FitsUtf8(ReadOnlySpan<char> characters, int count)
        {
            if (Text.Encoding.UTF8.GetByteCount(characters) <= count)
                return null;
            try
            {
                Text.Encoding.UTF8.GetBytes(characters, new byte[count]);
                return null;
            }
            catch (ArgumentException exception)
            {
                return exception;
            }
        }

        // The structure's fields from its payload (a boxed value's, a class's object, or an array element), the
        // caller keeping its holder alive.
        private void ToNative(byte* managed, byte* native)
        {
            new Span<byte>(native, NativeSize).Clear();
            int* step = FirstStep;
            for (int index = 0; index < StepCount; index++, step += StepInts)
            {
                byte* from = managed + step[1];
                byte* to = native + step[2];
                int size = step[3];
                int count = step[4];
                switch ((Step)step[0])
                {
                    case Step.Copy:
                        Buffer.MemoryCopy(from, to, size, size);
                        break;
                    case Step.Boolean:
                        if (size == sizeof(int))
                            *(int*)to = *from != 0 ? 1 : 0;
                        else
                            *to = *from != 0 ? (byte)1 : (byte)0;
                        break;
                    case Step.Ansi:
                        *to = StubHelpers.AnsiCharToNative(*(char*)from, fBestFit: true, fThrowOnUnmappableChar: false);
                        break;
                    case Step.Utf8String:
                        *(nint*)to = Unsafe.AsRef<string?>(from) is { } utf8 ? Marshal.StringToCoTaskMemUTF8(utf8) : 0;
                        break;
                    case Step.Utf16String:
                        *(nint*)to = Unsafe.AsRef<string?>(from) is { } utf16 ? Marshal.StringToCoTaskMemUni(utf16) : 0;
                        break;
                    case Step.Utf8Inline:
                        Utf8ToFixed(Unsafe.AsRef<string?>(from), to, count);
                        break;
                    case Step.Utf16Inline:
                        if (Unsafe.AsRef<string?>(from) is { } text)
                            text.AsSpan(0, Math.Min(text.Length, count - 1)).CopyTo(new Span<char>(to, count));
                        break;
                    case Step.Delegate:
                        if (Unsafe.AsRef<object?>(from) is not null)
                            throw new PlatformNotSupportedException(SR.PlatformNotSupported_StructMarshallingDelegate);
                        break;
                    case Step.SafeHandle:
                        throw new InvalidOperationException(SR.InvalidOperation_SafeHandleFieldsNotAllowed);
                    case Step.CriticalHandle:
                        // A null one throws NullReferenceException, as desktop's marshalling does.
                        *(nint*)to = Unsafe.AsRef<CriticalHandle>(from).Handle;
                        break;
                    case Step.EmbeddedClass:
                        // A null one leaves its bytes zero.
                        if (Unsafe.AsRef<object?>(from) is { } embedded)
                            Of(step[5]).Write(embedded, to);
                        break;
                    case Step.ArrayStructure:
                        StructuresToNative(Unsafe.AsRef<Array?>(from), Of(step[5]), to, size, count);
                        break;
                    case Step.ArrayEnum:
                        ArrayToNative(Unsafe.AsRef<Array?>(from), Step.ArrayCopy, to, size, count);
                        break;
                    default:
                        ArrayToNative(Unsafe.AsRef<Array?>(from), (Step)step[0], to, size, count);
                        break;
                }
            }
        }

        // What NetWasm does not convert, a function pointer that is not null, anywhere in the native structure.
        internal void ThrowIfUnsupportedNative(byte* native)
        {
            int* step = FirstStep;
            for (int index = 0; index < StepCount; index++, step += StepInts)
            {
                byte* from = native + step[2];
                switch ((Step)step[0])
                {
                    case Step.Delegate when *(nint*)from != 0:
                        throw new PlatformNotSupportedException(SR.PlatformNotSupported_StructMarshallingDelegate);
                    case Step.EmbeddedClass when Of(step[5]) is { Converts: true } nested:
                        nested.ThrowIfUnsupportedNative(from);
                        break;
                    case Step.ArrayStructure when Of(step[5]) is { Converts: true } element:
                        for (int item = 0; item < step[4]; item++)
                            element.ThrowIfUnsupportedNative(from + (item * (step[3] / step[4])));
                        break;
                }
            }
        }

        // PtrToStructure's copy out of the native layout: a native boolean is true when it is not zero, an ANSI byte
        // becomes the character it decodes to, a string pointer a new string (null for a null pointer), an inline
        // string the text before its terminator, and an inline array a new array of its elements.
        internal void FromNative(byte* native, object structure)
        {
            int* step = FirstStep;
            for (int index = 0; index < StepCount; index++, step += StepInts)
            {
                // A new string or array may collect; the collectors move nothing, so the payload stays put.
                byte* from = native + step[2];
                byte* to = GetPayload(structure) + step[1];
                int size = step[3];
                int count = step[4];
                switch ((Step)step[0])
                {
                    case Step.Copy:
                        Buffer.MemoryCopy(from, to, size, size);
                        break;
                    case Step.Boolean:
                        *to = (size == sizeof(int) ? *(int*)from : *from) != 0 ? (byte)1 : (byte)0;
                        break;
                    case Step.Ansi:
                        *(char*)to = StubHelpers.AnsiCharToManaged(*from);
                        break;
                    case Step.Utf8String:
                        Unsafe.AsRef<string?>(to) = *(nint*)from is var utf8 and not 0 ? Marshal.PtrToStringUTF8(utf8) : null;
                        break;
                    case Step.Utf16String:
                        Unsafe.AsRef<string?>(to) = *(nint*)from is var utf16 and not 0 ? Marshal.PtrToStringUni(utf16) : null;
                        break;
                    case Step.Utf8Inline:
                        var bytes = new ReadOnlySpan<byte>(from, count);
                        int end = bytes.IndexOf((byte)0);
                        Unsafe.AsRef<string?>(to) = Text.Encoding.UTF8.GetString(end < 0 ? bytes : bytes[..end]);
                        break;
                    case Step.Utf16Inline:
                        var chars = new ReadOnlySpan<char>(from, count);
                        int length = chars.IndexOf('\0');
                        Unsafe.AsRef<string?>(to) = new string(length < 0 ? chars : chars[..length]);
                        break;
                    case Step.Delegate:
                        if (*(nint*)from != 0)
                            throw new PlatformNotSupportedException(SR.PlatformNotSupported_StructMarshallingDelegate);
                        Unsafe.AsRef<object?>(to) = null;
                        break;
                    case Step.SafeHandle:
                        throw new NotSupportedException(SR.NotSupported_SafeHandleField);
                    case Step.CriticalHandle:
                        throw new NotSupportedException(SR.NotSupported_CriticalHandleField);
                    case Step.EmbeddedClass:
                        // The new instance takes the field before it is read, as desktop leaves it when one of its
                        // fields fails.
                        StructureMarshallingData embedded = Of(step[5]);
                        object created = embedded.Create(step[5]);
                        Unsafe.AsRef<object?>(GetPayload(structure) + step[1]) = created;
                        embedded.Read(from, created);
                        break;
                    case Step.ArrayStructure:
                        Unsafe.AsRef<Array?>(GetPayload(structure) + step[1]) = StructuresFromNative(Of(step[5]), step[5], from, size, count);
                        break;
                    case Step.ArrayEnum:
                        Array values = Array.CreateInstance(Type.GetTypeFromSemanticId(step[5]), count);
                        ArrayFromNative(values, Step.ArrayCopy, from, size, count);
                        Unsafe.AsRef<Array?>(GetPayload(structure) + step[1]) = values;
                        break;
                    default:
                        Array array = NewArray(step[5], count);
                        ArrayFromNative(array, (Step)step[0], from, size, count);
                        Unsafe.AsRef<Array?>(GetPayload(structure) + step[1]) = array;
                        break;
                }
            }
        }

        // DestroyStructure: frees the native strings StructureToPtr allocated, an embedded class's and an array's
        // structures' too.
        internal void Destroy(byte* native)
        {
            int* step = FirstStep;
            for (int index = 0; index < StepCount; index++, step += StepInts)
            {
                switch ((Step)step[0])
                {
                    case Step.Utf8String or Step.Utf16String:
                        Marshal.FreeCoTaskMem(*(nint*)(native + step[2]));
                        break;
                    case Step.EmbeddedClass:
                        Of(step[5]).DestroyNested(native + step[2]);
                        break;
                    case Step.ArrayStructure:
                        StructureMarshallingData element = Of(step[5]);
                        for (int item = 0; item < step[4]; item++)
                            element.DestroyNested(native + step[2] + (item * (step[3] / step[4])));
                        break;
                }
            }
        }

        private void DestroyNested(byte* native)
        {
            if (Converts)
                Destroy(native);
        }

        // The descriptor of an embedded class or an array's structure, by its type's ID.
        private static StructureMarshallingData Of(int typeId) =>
            new((byte*)StructureMarshallingRuntime.GetDescriptor(typeId));

        // An embedded class or an array element in its native layout: converted, or copied when blittable.
        private void Write(object structure, byte* native)
        {
            if (Converts)
            {
                ToNative(GetPayload(structure), native);
                return;
            }
            Buffer.MemoryCopy(GetPayload(structure), native, NativeSize, NativeSize);
        }

        private void Read(byte* native, object structure)
        {
            if (Converts)
            {
                FromNative(native, structure);
                return;
            }
            Buffer.MemoryCopy(native, GetPayload(structure), NativeSize, NativeSize);
        }

        // A new instance as PtrToStructure creates one, by the type's ID: its parameterless constructor runs, or a
        // value without one is zero.
        private object Create(int typeId)
        {
            object structure = RuntimeHelpersRuntime.AllocateUninitializedObject(typeId) ??
                throw new MissingMethodException(SR.Activator_NoParameterlessConstructor);
            if ((Flags & DefaultConstructor) != 0)
            {
                try
                {
                    StructureMarshallingRuntime.Construct(typeId, structure);
                }
                catch (Exception exception)
                {
                    throw new Reflection.TargetInvocationException(exception);
                }
            }
            else if ((Flags & ValueType) == 0)
            {
                throw new MissingMethodException(SR.Activator_NoParameterlessConstructor);
            }

            return structure;
        }

        // A ByValArray of structures: each of the first count elements in its native layout; desktop rejects a
        // shorter array and writes zeros for a null one.
        private static void StructuresToNative(Array? array, StructureMarshallingData element, byte* native, int size, int count)
        {
            if (array is null)
                return;
            if (array.Length < count)
                throw new ArgumentException(SR.Argument_WrongSizeArrayInNativeStruct);
            for (int index = 0; index < count; index++)
                element.Write(array.GetValue(index)!, native + (index * (size / count)));
        }

        private static Array StructuresFromNative(StructureMarshallingData element, int typeId, byte* native, int size, int count)
        {
            Array array = Array.CreateInstance(Type.GetTypeFromSemanticId(typeId), count);
            for (int index = 0; index < count; index++)
            {
                object value = element.Create(typeId);
                element.Read(native + (index * (size / count)), value);
                array.SetValue(value, index);
            }

            return array;
        }

        // StubHelpers.CSTRMarshaler.ConvertFixedToNative on Unix: at most count - 1 characters, as UTF-8 into count
        // bytes, then a terminator.
        private static void Utf8ToFixed(string? text, byte* native, int count)
        {
            if (text is null)
                return;
            int written = Text.Encoding.UTF8.GetBytes(text.AsSpan(0, Math.Min(text.Length, count - 1)),
                new Span<byte>(native, count));
            native[written == count ? written - 1 : written] = 0;
        }

        // A ByValArray's first count elements; desktop rejects a shorter array and writes zeros for a null one. ANSI
        // characters are the first count characters as UTF-8 in count bytes, which throws ArgumentException when
        // they do not fit, as one outside ASCII does not.
        private static void ArrayToNative(Array? array, Step kind, byte* native, int size, int count)
        {
            if (array is null)
                return;
            if (array.Length < count)
                throw new ArgumentException(SR.Argument_WrongSizeArrayInNativeStruct);
            if (kind == Step.ArrayAnsi)
            {
                Text.Encoding.UTF8.GetBytes(((char[])array).AsSpan(0, count), new Span<byte>(native, count));
                return;
            }
            ref byte data = ref MemoryMarshal.GetArrayDataReference(array);
            int elementSize = size / count;
            for (int index = 0; index < count; index++)
            {
                if (kind == Step.ArrayCopy)
                {
                    Unsafe.CopyBlockUnaligned(ref Unsafe.AsRef<byte>(native + (index * elementSize)),
                        ref Unsafe.Add(ref data, index * elementSize), (uint)elementSize);
                    continue;
                }
                int flag = Unsafe.Add(ref data, index) != 0 ? 1 : 0;
                if (elementSize == sizeof(int))
                    ((int*)native)[index] = flag;
                else
                    native[index] = (byte)flag;
            }
        }

        // ANSI characters are the count bytes as UTF-8, a character for each sequence and U+FFFD for each byte that is
        // none, as desktop .NET reads them on Unix.
        private static void ArrayFromNative(Array array, Step kind, byte* native, int size, int count)
        {
            if (kind == Step.ArrayAnsi)
            {
                Text.Encoding.UTF8.GetChars(new ReadOnlySpan<byte>(native, count), (char[])array);
                return;
            }
            ref byte data = ref MemoryMarshal.GetArrayDataReference(array);
            int elementSize = size / count;
            for (int index = 0; index < count; index++)
            {
                switch (kind)
                {
                    case Step.ArrayCopy:
                        Unsafe.CopyBlockUnaligned(ref Unsafe.Add(ref data, index * elementSize),
                            ref Unsafe.AsRef<byte>(native + (index * elementSize)), (uint)elementSize);
                        break;
                    default:
                        Unsafe.Add(ref data, index) =
                            (elementSize == sizeof(int) ? ((int*)native)[index] : native[index]) != 0 ? (byte)1 : (byte)0;
                        break;
                }
            }
        }

        // The managed array PtrToStructure fills, of the element type the compiler recorded (NativeArrayElement).
        private static Array NewArray(int element, int count) => element switch
        {
            1 => new sbyte[count],
            2 => new byte[count],
            3 => new short[count],
            4 => new ushort[count],
            5 => new int[count],
            6 => new uint[count],
            7 => new long[count],
            8 => new ulong[count],
            9 => new float[count],
            10 => new double[count],
            11 => new nint[count],
            12 => new nuint[count],
            13 => new bool[count],
            14 => new char[count],
            _ => throw new InvalidOperationException(),
        };

        // RuntimeInteropData.GetStructUnsafeStructSize: SizeOf and OffsetOf reject a layout desktop .NET does not
        // marshal and one with a field it rejects, and a layout NetWasm does not convert.
        internal int Size
        {
            get
            {
                ThrowIfNotMeasurable();
                return NativeSize;
            }
        }

        private void ThrowIfNotMeasurable()
        {
            ThrowIfNotLoadable();
            if ((Flags & (InvalidLayout | UnmarshalableField)) != 0)
                throw new ArgumentException(SR.Arg_CannotMarshal);
            ThrowIfNotCopyable();
        }

        // StructureToPtr, PtrToStructure and DestroyStructure: a class without layout, as desktop throws for it; a
        // field desktop rejects, as it throws when it builds the marshalling stub; a conversion NetWasm lacks.
        internal void ThrowIfNotConvertible(string paramName)
        {
            ThrowIfNotLoadable();
            if ((Flags & InvalidLayout) != 0)
            {
                throw (Flags & ValueType) != 0
                    ? new ArgumentException(SR.Arg_CannotMarshal)
                    : new ArgumentException(SR.Argument_MustHaveLayoutOrBeBlittable, paramName);
            }
            if ((Flags & UnmarshalableField) != 0)
                throw new TypeLoadException(SR.Marshaler_UnmarshalableField);
            ThrowIfNotCopyable();
        }

        // A formatted class on a base without layout is a type desktop .NET cannot load: every member throws.
        private void ThrowIfNotLoadable()
        {
            if ((Flags & InvalidType) != 0)
                throw new TypeLoadException(SR.TypeLoad_InvalidFormat);
        }

        internal static StructureMarshallingData Get(Type type, bool fieldNames = false)
        {
            nint address = fieldNames
                ? StructureMarshallingRuntime.GetDescriptorWithFieldNames(type.SemanticTypeId)
                : StructureMarshallingRuntime.GetDescriptor(type.SemanticTypeId);
            if (address == 0)
            {
                if (type.IsInterface || type.IsGenericType)
                    throw new ArgumentException(SR.Arg_CannotMarshal);

                throw new NotSupportedException(SR.StructMarshalling_MissingInteropData);
            }

            var data = new StructureMarshallingData((byte*)address);
            if (*(int*)address != 1 || data.Count < 0)
                throw new InvalidOperationException();

            return data;
        }

        internal void ThrowIfNotCopyable()
        {
            if ((Flags & RequiresConversion) != 0)
                throw new PlatformNotSupportedException(SR.PlatformNotSupported_StructMarshallingConversion);
        }

        internal byte* GetPayload(object structure) =>
            (byte*)Unsafe.As<object, nint>(ref structure) + *(int*)(_address + 12);

        // RuntimeInteropData.GetStructFieldOffset over the descriptor's field tables.
        internal static int GetFieldOffset(Type type, string fieldName)
        {
            StructureMarshallingData data = Get(type, fieldNames: true);
            data.ThrowIfNotMeasurable();
            int count = data.Count;
            int* offsets = *(int**)(data._address + 24);
            void* names = *(void**)(data._address + 24 + sizeof(nint));
            if (count != 0 && (offsets == null || names == null))
                throw new InvalidOperationException();

            ReadOnlySpan<string> fieldNames = MemoryMarshal.CreateReadOnlySpan(ref Unsafe.AsRef<string>(names), count);
            for (int index = 0; index < count; index++)
            {
                if (fieldNames[index] == fieldName)
                    return offsets[index];
            }

            throw new ArgumentException(SR.Argument_OffsetOfFieldNotFound, nameof(fieldName));
        }

        // Activator.CreateInstance(Type, nonPublic: true), which PtrToStructure creates its structure with: a new
        // instance on which the type's parameterless constructor, public or not, runs, its exception wrapped in
        // TargetInvocationException; a value type without one is zero, and a class without one throws
        // MissingMethodException, as desktop throws. PtrToStructure then fills in every marshalled field.
        internal static object CreateStructure(Type type)
        {
            StructureMarshallingData data = Get(type);
            data.ThrowIfNotConvertible("structure");
            if ((data.Flags & (ValueType | DefaultConstructor)) == 0)
                throw new MissingMethodException(SR.Activator_NoParameterlessConstructor);

            object structure = RuntimeHelpers.GetUninitializedObject(type);
            if ((data.Flags & DefaultConstructor) != 0)
            {
                try
                {
                    StructureMarshallingRuntime.Construct(type.SemanticTypeId, structure);
                }
                catch (Exception exception)
                {
                    throw new Reflection.TargetInvocationException(exception);
                }
            }

            return structure;
        }
    }

    internal static class StructureMarshallingRuntime
    {
        [MethodImpl(MethodImplOptions.InternalCall)]
        internal static extern nint GetDescriptor(int typeId);

        [MethodImpl(MethodImplOptions.InternalCall)]
        internal static extern nint GetDescriptorWithFieldNames(int typeId);

        // Runs the parameterless constructor of the type whose ID it is on the new instance.
        [MethodImpl(MethodImplOptions.InternalCall)]
        internal static extern void Construct(int typeId, object instance);
    }
}
