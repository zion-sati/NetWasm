// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Ported from dotnet/runtime deae2e32bf0e018e09d64597b7c59b62b73bb3ce
// (src/coreclr/nativeaot/System.Private.CoreLib/src/System/TypedReference.cs, with the argument checks of
// src/libraries/System.Private.CoreLib/src/System/TypedReference.cs).
// NetWasm adaptation: the layout is NativeAOT's (the referenced value, then its type handle), with the boxer of the
// referenced type after them, and the compiler maps the CLI typedref primitive to this type. The compiler lowers
// mkrefany, refanyval and refanytype (__makeref, __refvalue, __reftype) to NetWasmMake, NetWasmValue and
// NetWasmTargetType, instantiated for the instruction's type, as NativeAOT's Value and GetRefAny serve its lowering.
// ToObject boxes through the boxer the reference was made with, so only the types a program makes references to
// carry one. MakeTypedReference needs the runtime field layout, so it throws PlatformNotSupportedException after
// upstream's argument checks.

using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace System
{
    [CLSCompliant(false)]
    [StructLayout(LayoutKind.Sequential)]
    public ref struct TypedReference
    {
        // Do not change the ordering of these fields. The JIT has a dependency on this layout.
        // NetWasm: only typed-reference IL would write them, so both keep their default values.
        private readonly ref byte _value;
        private readonly RuntimeTypeHandle _typeHandle;
        private readonly TypedReferenceBoxer? _boxer;

        private TypedReference(ref byte value, RuntimeTypeHandle typeHandle, TypedReferenceBoxer boxer)
        {
            _value = ref value;
            _typeHandle = typeHandle;
            _boxer = boxer;
        }

        // mkrefany T: a reference to the value at the address, of type T.
        internal static TypedReference NetWasmMake<T>(ref T value) =>
            new(ref Unsafe.As<T, byte>(ref value), typeof(T).TypeHandle, TypedReferenceBoxer<T>.Instance);

        // refanyval T: the address the reference holds, when it was made with type T.
        internal static ref T NetWasmValue<T>(TypedReference reference)
        {
            if (!reference._typeHandle.Equals(typeof(T).TypeHandle))
                throw new InvalidCastException();
            return ref Unsafe.As<byte, T>(ref reference._value);
        }

        // refanytype: the reference's type handle.
        internal static RuntimeTypeHandle NetWasmTargetType(TypedReference reference) => reference._typeHandle;

        public static TypedReference MakeTypedReference(object target, FieldInfo[] flds)
        {
            ArgumentNullException.ThrowIfNull(target);
            ArgumentNullException.ThrowIfNull(flds);

            if (flds.Length == 0)
            {
                throw new ArgumentException(SR.Arg_ArrayZeroError, nameof(flds));
            }

            throw new PlatformNotSupportedException(SR.PlatformNotSupported_TypedReference);
        }

        public static Type? GetTargetType(TypedReference value) =>
            value.IsNull ? null : Type.GetTypeFromHandle(value._typeHandle);

        public static RuntimeTypeHandle TargetTypeToken(TypedReference value)
        {
            if (value.IsNull)
                throw new NullReferenceException(); // For compatibility;
            return value._typeHandle;
        }

        public static object ToObject(TypedReference value)
        {
            if (value.IsNull)
                throw new ArgumentNullException(nameof(value), SR.ArgumentNull_TypedRefType);

            return value._boxer!.Box(ref value._value);
        }

        public static void SetTypedReference(TypedReference target, object? value) { throw new NotSupportedException(); }

        public override bool Equals(object? o) { throw new NotSupportedException(SR.NotSupported_NYI); }
        public override int GetHashCode() => IsNull ? 0 : _typeHandle.GetHashCode();

        // NetWasm: RuntimeTypeHandle has no IsNull; the default handle is the null handle.
        internal bool IsNull => _typeHandle.Equals(default(RuntimeTypeHandle));
    }

    // Boxes the value a TypedReference holds as the type it was made with.
    internal abstract class TypedReferenceBoxer
    {
        internal abstract object Box(ref byte value);
    }

    internal sealed class TypedReferenceBoxer<T> : TypedReferenceBoxer
    {
        internal static readonly TypedReferenceBoxer<T> Instance = new();

        internal override object Box(ref byte value) => Unsafe.As<byte, T>(ref value)!;
    }
}
