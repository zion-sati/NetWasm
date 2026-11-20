// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: MIT

using System.Runtime.CompilerServices;

namespace System
{
    [Serializable]
    [System.Runtime.CompilerServices.TypeForwardedFrom("mscorlib, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089")]
    public class Object
    {
        public Object()
        {
        }

        ~Object()
        {
        }

        public virtual bool Equals(object? value) => this == value;
        public virtual int GetHashCode() => ObjectIdentityRuntime.GetHashCode(this);
        public virtual string ToString() => GetType().ToString();
        // The compiler lowers every call to GetType, this one included, to a read of the object's
        // type; the body is what a delegate or function pointer to GetType runs.
        [Intrinsic]
        public Type GetType() => GetType();
        [Intrinsic]
        [Runtime.CompilerServices.MethodImpl(Runtime.CompilerServices.MethodImplOptions.InternalCall)]
        protected extern object MemberwiseClone();
        public static bool Equals(object? left, object? right) =>
            left == null ? right == null : left.Equals(right);
        internal static int GetValueHashCode(object? value) =>
            value?.GetHashCode() ?? 0;
        public static bool ReferenceEquals(object? left, object? right) => left == right;
    }

    public interface IEquatable<T> where T : allows ref struct
    {
        bool Equals(T? other);
    }

    public interface IComparable
    {
        int CompareTo(object? value);
    }

    public interface IComparable<in T> where T : allows ref struct
    {
        int CompareTo(T? other);
    }
}
