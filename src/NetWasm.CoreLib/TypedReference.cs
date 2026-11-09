// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Ported from dotnet/runtime 474ed963699fc29809015392738a15231aaae2de
// (src/coreclr/nativeaot/System.Private.CoreLib/src/System/TypedReference.cs, with the argument checks of
// src/libraries/System.Private.CoreLib/src/System/TypedReference.cs).
// NetWasm adaptation: the layout is NativeAOT's (the referenced value, then its type handle), and the compiler
// maps the CLI typedref primitive to this type. NetWasm does not compile mkrefany, refanyval or refanytype, and
// MakeTypedReference needs the runtime field layout, so it throws PlatformNotSupportedException after upstream's
// argument checks; the only TypedReference a NetWasm program can hold is default. The members behave as
// NativeAOT's for that value, and ToObject throws PNSE for any other. NativeAOT's internal Value and GetRefAny
// serve its refanyval lowering, which NetWasm does not have, so they are not ported.

using System.Reflection;
using System.Runtime.InteropServices;

namespace System
{
    [CLSCompliant(false)]
    [StructLayout(LayoutKind.Sequential)]
    public ref struct TypedReference
    {
        // Do not change the ordering of these fields. The JIT has a dependency on this layout.
        // NetWasm: only typed-reference IL would write them, so both keep their default values.
#pragma warning disable CS0169, CS0649
        private readonly ref byte _value;
        private readonly RuntimeTypeHandle _typeHandle;
#pragma warning restore CS0169, CS0649

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

            throw new PlatformNotSupportedException(SR.PlatformNotSupported_TypedReference);
        }

        public static void SetTypedReference(TypedReference target, object? value) { throw new NotSupportedException(); }

        public override bool Equals(object? o) { throw new NotSupportedException(SR.NotSupported_NYI); }
        public override int GetHashCode() => IsNull ? 0 : _typeHandle.GetHashCode();

        // NetWasm: RuntimeTypeHandle has no IsNull; the default handle is the null handle.
        internal bool IsNull => _typeHandle.Equals(default(RuntimeTypeHandle));
    }
}
