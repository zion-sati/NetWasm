// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Ported from dotnet/runtime 474ed963699fc29809015392738a15231aaae2de: the structure-layout
// members of Marshal.cs with native AOT's helpers (Marshal.NativeAot.cs) and struct interop data
// lookups (RuntimeInteropData.cs). NetWasm adaptations: the compiler publishes a structure-
// marshalling descriptor for each type the program names, or allocates as a boxed value, in place
// of native AOT's interop data, and generates no marshalling stubs. Blittable layouts are copied
// as upstream copies a layout without stubs; a layout that needs stubs (bool, char, references,
// formatted classes) throws PlatformNotSupportedException before any memory access, and so do
// SizeOf and OffsetOf for it, since its native size differs from the managed one. Every NetWasm
// Type is a runtime type, and IsFunctionPointer is not distinguished from IsPointer. The generic
// PtrToStructure creates its value with Activator.CreateInstance<T>(), which runs a declared
// parameterless constructor; the Type overload creates zeroed storage and rejects a value type
// that declares one. The type-naming messages drop the type name, which would otherwise publish
// every type's name.

using System.ComponentModel;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace System.Runtime.InteropServices
{
    // NetWasm: structure marshalling needs compiler intrinsics this platform does not have
    // (see StructureMarshallingRuntime), so these members stay internal instead of being public
    // APIs that no program could compile.
    public static partial class Marshal
    {
        [RequiresDynamicCode("Marshalling code for the object might not be available. Use the SizeOf<T> overload instead.")]
        [EditorBrowsable(EditorBrowsableState.Never)]
        internal static int SizeOf(object structure)
        {
            ArgumentNullException.ThrowIfNull(structure);

            return SizeOfHelper(structure.GetType(), throwIfNotMarshalable: true);
        }

        internal static int SizeOf<T>(T structure)
        {
            ArgumentNullException.ThrowIfNull(structure);

            return SizeOfHelper(structure.GetType(), throwIfNotMarshalable: true);
        }

        [RequiresDynamicCode("Marshalling code for the object might not be available. Use the SizeOf<T> overload instead.")]
        [EditorBrowsable(EditorBrowsableState.Never)]
        internal static int SizeOf(Type t)
        {
            ArgumentNullException.ThrowIfNull(t);

            if (t.IsGenericType)
            {
                throw new ArgumentException(SR.Argument_NeedNonGenericType, nameof(t));
            }

            return SizeOfHelper(t, throwIfNotMarshalable: true);
        }

        internal static int SizeOf<T>()
        {
            Type t = typeof(T);
            if (t.IsGenericType)
            {
                throw new ArgumentException(SR.Argument_NeedNonGenericType, nameof(T));
            }

            return SizeOfHelper(t, throwIfNotMarshalable: true);
        }

        internal static IntPtr OffsetOf<T>(string fieldName) => OffsetOf(typeof(T), fieldName);

        [EditorBrowsable(EditorBrowsableState.Never)]
        internal static IntPtr OffsetOf(Type t, string fieldName)
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
        internal static void StructureToPtr<T>([DisallowNull] T structure, IntPtr ptr, bool fDeleteOld)
        {
            StructureToPtr((object)structure!, ptr, fDeleteOld);
        }

        /// <summary>
        /// Creates a new instance of <paramref name="structureType"/> and marshals data from a
        /// native memory block to it.
        /// </summary>
        [RequiresDynamicCode("Marshalling code for the object might not be available")]
        [EditorBrowsable(EditorBrowsableState.Never)]
        internal static object? PtrToStructure(IntPtr ptr,
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

        internal static T? PtrToStructure<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.NonPublicConstructors)] T>(IntPtr ptr)
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

            StructureMarshallingData.Get(structureType).ThrowIfNotCopyable();
            object structure = Activator.CreateInstance<T>()!;
            PtrToStructureHelper(ptr, structure, allowValueClasses: true);
            return (T)structure;
        }

        [UnconditionalSuppressMessage("AotAnalysis", "IL3050:AotUnfriendlyApi",
            Justification = "AOT compilers can see the T.")]
        internal static void DestroyStructure<T>(IntPtr ptr) => DestroyStructure(ptr, typeof(T));

        internal static int SizeOfHelper(Type t, bool throwIfNotMarshalable)
        {
            Debug.Assert(throwIfNotMarshalable);

            if (t.IsPointer)
                return IntPtr.Size;

            if (t.IsByRef || t.IsArray || t.ContainsGenericParameters)
                throw new ArgumentException(SR.Arg_CannotMarshal);

            return StructureMarshallingData.Get(t).Size;
        }

        private static void PtrToStructureHelper(IntPtr ptr, object structure, bool allowValueClasses)
        {
            if (!allowValueClasses && structure.GetType().IsValueType)
            {
                throw new ArgumentException(SR.Argument_StructMustNotBeValueClass, nameof(structure));
            }

            PtrToStructureImpl(ptr, structure);
        }

        internal static unsafe void PtrToStructureImpl(IntPtr ptr, object structure)
        {
            StructureMarshallingData data = StructureMarshallingData.Get(structure.GetType());
            int size = data.Size;
            Buffer.MemoryCopy((void*)ptr, data.GetPayload(structure), size, size);
            GC.KeepAlive(structure);
        }

        [RequiresDynamicCode("Marshalling code for the object might not be available. Use the DestroyStructure<T> overload instead.")]
        [EditorBrowsable(EditorBrowsableState.Never)]
        internal static void DestroyStructure(IntPtr ptr, Type structuretype)
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

            // A blittable layout has no destroy stub, so there is nothing to release.
            StructureMarshallingData.Get(structuretype).ThrowIfNotCopyable();
        }

        [RequiresDynamicCode("Marshalling code for the object might not be available. Use the StructureToPtr<T> overload instead.")]
        [EditorBrowsable(EditorBrowsableState.Never)]
        internal static unsafe void StructureToPtr(object structure, IntPtr ptr, bool fDeleteOld)
        {
            ArgumentNullException.ThrowIfNull(structure);
            ArgumentNullException.ThrowIfNull(ptr);

            Type structureType = structure.GetType();
            if (structureType.IsGenericType)
            {
                throw new ArgumentException(SR.Argument_NeedNonGenericObject, nameof(structure));
            }

            if (fDeleteOld)
            {
                DestroyStructure(ptr, structureType);
            }

            StructureMarshallingData data = StructureMarshallingData.Get(structureType);
            int size = data.Size;
            Buffer.MemoryCopy(data.GetPayload(structure), (void*)ptr, size, size);
            GC.KeepAlive(structure);
        }
    }

    /// <summary>
    /// Read-only view of the compiler's structure-marshalling descriptor, version 1: version,
    /// flags, native size, boxed payload offset and field count, then the field offset and name
    /// tables. The compiler owns its storage and the referenced static strings.
    /// </summary>
    internal readonly unsafe struct StructureMarshallingData
    {
        private const int InvalidLayout = 1;
        private const int RequiresConversion = 2;
        private const int ValueType = 4;
        private const int DefaultConstructor = 8;
        private readonly byte* _address;

        private StructureMarshallingData(byte* address) => _address = address;

        private int Flags => *(int*)(_address + 4);
        private int Count => *(int*)(_address + 16);

        // RuntimeInteropData.GetStructUnsafeStructSize: a layout that needs stubs has a native
        // size NetWasm does not compute.
        internal int Size
        {
            get
            {
                ThrowIfNotCopyable();
                return *(int*)(_address + 8);
            }
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

            if ((data.Flags & InvalidLayout) != 0)
                throw new ArgumentException(SR.Arg_CannotMarshal);

            return data;
        }

        internal void ThrowIfNotCopyable()
        {
            if ((Flags & RequiresConversion) != 0)
                throw new PlatformNotSupportedException(SR.PlatformNotSupported_StructMarshallingConversion);
        }

        internal byte* GetPayload(object structure)
        {
            Debug.Assert((Flags & ValueType) != 0);
            return (byte*)Unsafe.As<object, nint>(ref structure) + *(int*)(_address + 12);
        }

        // RuntimeInteropData.GetStructFieldOffset over the descriptor's field tables.
        internal static int GetFieldOffset(Type type, string fieldName)
        {
            StructureMarshallingData data = Get(type, fieldNames: true);
            data.ThrowIfNotCopyable();
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

        // Activator.CreateInstance(Type, nonPublic: true) for a copyable value type: zeroed
        // storage, which is that call's result unless the type declares a parameterless constructor.
        internal static object CreateStructure(Type type)
        {
            StructureMarshallingData data = Get(type);
            data.ThrowIfNotCopyable();
            if ((data.Flags & DefaultConstructor) != 0)
                throw new PlatformNotSupportedException(SR.PlatformNotSupported_StructMarshallingConstructor);

            return RuntimeHelpers.GetUninitializedObject(type);
        }
    }

    internal static class StructureMarshallingRuntime
    {
        [MethodImpl(MethodImplOptions.InternalCall)]
        internal static extern nint GetDescriptor(int typeId);

        [MethodImpl(MethodImplOptions.InternalCall)]
        internal static extern nint GetDescriptorWithFieldNames(int typeId);
    }
}
