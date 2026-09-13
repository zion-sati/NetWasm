// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
//
// Adapted from src/libraries/System.Private.CoreLib/src/System/Nullable.cs in
// dotnet/runtime. Reflection-based nullable type discovery is unavailable in
// NetWasm because runtime generic type metadata is not deployed.

using System.Collections.Generic;
using System.Runtime.CompilerServices;

#pragma warning disable CA1066 // Implement IEquatable when overriding Object.Equals

namespace System;

// Because the runtime gives boxed Nullable<T> the same representation as a
// boxed T, Nullable<T> cannot implement interfaces: T may not implement them.
// Do not add interfaces to Nullable<T>.
[Serializable]
[TypeForwardedFrom("mscorlib, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089")]
public partial struct Nullable<T> where T : struct
{
    private readonly bool hasValue;
    internal T value;

    public Nullable(T value)
    {
        this.value = value;
        hasValue = true;
    }

    public readonly bool HasValue
    {
        get => hasValue;
    }

    public readonly T Value
    {
        get
        {
            if (!hasValue)
            {
                throw new InvalidOperationException();
            }

            return value;
        }
    }

    public readonly T GetValueOrDefault() => value;

    public readonly T GetValueOrDefault(T defaultValue) =>
        hasValue ? value : defaultValue;

    public override bool Equals(object? other)
    {
        if (!hasValue)
        {
            return other == null;
        }

        if (other == null)
        {
            return false;
        }

        return value.Equals(other);
    }

    public override int GetHashCode() => hasValue ? value.GetHashCode() : 0;

    public override string ToString() => hasValue ? value.ToString() : "";

    public static implicit operator T?(T value) => new T?(value);

    public static explicit operator T(T? value) => value!.Value;
}

public static class Nullable
{
    public static int Compare<T>(T? n1, T? n2) where T : struct
    {
        if (n1.HasValue)
        {
            if (n2.HasValue)
            {
                return Comparer<T>.Default.Compare(n1.value, n2.value);
            }

            return 1;
        }

        return n2.HasValue ? -1 : 0;
    }

    public static bool Equals<T>(T? n1, T? n2) where T : struct
    {
        if (n1.HasValue)
        {
            if (n2.HasValue)
            {
                return EqualityComparer<T>.Default.Equals(n1.value, n2.value);
            }

            return false;
        }

        return !n2.HasValue;
    }

    // If the type provided is not Nullable<T>, the desktop contract returns
    // null. Determining that distinction requires reflection metadata, which
    // NetWasm deliberately does not deploy. Fail explicitly instead of
    // fabricating a Type or reporting an incorrect underlying type.
    public static Type? GetUnderlyingType(Type nullableType)
    {
        ArgumentNullException.ThrowIfNull(nullableType);
        throw new PlatformNotSupportedException(
            "Nullable.GetUnderlyingType requires runtime generic type metadata, which NetWasm does not deploy.");
    }

    /// <summary>
    /// Retrieves a readonly reference to the location where the value is stored.
    /// </summary>
    public static ref readonly T GetValueRefOrDefaultRef<T>(ref readonly T? nullable)
        where T : struct => ref nullable.value;
}
