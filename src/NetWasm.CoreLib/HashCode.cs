// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
//
// The xxHash32 algorithm is based on the implementation published by Yann
// Collet under the BSD 2-Clause license. The upstream source is
// src/libraries/System.Private.CoreLib/src/System/HashCode.cs in dotnet/runtime.

using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;

#pragma warning disable CA1066 // Implement IEquatable when overriding Object.Equals

namespace System;

public struct HashCode
{
    // NetWasm currently has no entropy capability. Keep the module seed fixed
    // and explicit so hash values are deterministic within this CoreLib module
    // while retaining the upstream xxHash32 mixing and avalanche behavior.
    private const uint Seed = 0x9E3779B9U;
    private static readonly uint s_seed = Seed;

    private const uint Prime1 = 2654435761U;
    private const uint Prime2 = 2246822519U;
    private const uint Prime3 = 3266489917U;
    private const uint Prime4 = 668265263U;
    private const uint Prime5 = 374761393U;

    private uint _v1;
    private uint _v2;
    private uint _v3;
    private uint _v4;
    private uint _queue1;
    private uint _queue2;
    private uint _queue3;
    private uint _length;

    public static int Combine<T1>(T1 value1)
    {
        var hash = MixEmptyState() + 4;
        hash = QueueRound(hash, (uint)(value1?.GetHashCode() ?? 0));
        return (int)MixFinal(hash);
    }

    public static int Combine<T1, T2>(T1 value1, T2 value2)
    {
        var hash = MixEmptyState() + 8;
        hash = QueueRound(hash, (uint)(value1?.GetHashCode() ?? 0));
        hash = QueueRound(hash, (uint)(value2?.GetHashCode() ?? 0));
        return (int)MixFinal(hash);
    }

    public static int Combine<T1, T2, T3>(T1 value1, T2 value2, T3 value3)
    {
        var hash = MixEmptyState() + 12;
        hash = QueueRound(hash, (uint)(value1?.GetHashCode() ?? 0));
        hash = QueueRound(hash, (uint)(value2?.GetHashCode() ?? 0));
        hash = QueueRound(hash, (uint)(value3?.GetHashCode() ?? 0));
        return (int)MixFinal(hash);
    }

    public static int Combine<T1, T2, T3, T4>(T1 value1, T2 value2, T3 value3, T4 value4)
    {
        Initialize(out var v1, out var v2, out var v3, out var v4);
        v1 = Round(v1, (uint)(value1?.GetHashCode() ?? 0));
        v2 = Round(v2, (uint)(value2?.GetHashCode() ?? 0));
        v3 = Round(v3, (uint)(value3?.GetHashCode() ?? 0));
        v4 = Round(v4, (uint)(value4?.GetHashCode() ?? 0));
        return (int)MixFinal(MixState(v1, v2, v3, v4) + 16);
    }

    public static int Combine<T1, T2, T3, T4, T5>(T1 value1, T2 value2, T3 value3, T4 value4, T5 value5)
    {
        Initialize(out var v1, out var v2, out var v3, out var v4);
        v1 = Round(v1, (uint)(value1?.GetHashCode() ?? 0));
        v2 = Round(v2, (uint)(value2?.GetHashCode() ?? 0));
        v3 = Round(v3, (uint)(value3?.GetHashCode() ?? 0));
        v4 = Round(v4, (uint)(value4?.GetHashCode() ?? 0));
        var hash = MixState(v1, v2, v3, v4) + 20;
        hash = QueueRound(hash, (uint)(value5?.GetHashCode() ?? 0));
        return (int)MixFinal(hash);
    }

    public static int Combine<T1, T2, T3, T4, T5, T6>(T1 value1, T2 value2, T3 value3, T4 value4, T5 value5, T6 value6)
    {
        Initialize(out var v1, out var v2, out var v3, out var v4);
        v1 = Round(v1, (uint)(value1?.GetHashCode() ?? 0));
        v2 = Round(v2, (uint)(value2?.GetHashCode() ?? 0));
        v3 = Round(v3, (uint)(value3?.GetHashCode() ?? 0));
        v4 = Round(v4, (uint)(value4?.GetHashCode() ?? 0));
        var hash = MixState(v1, v2, v3, v4) + 24;
        hash = QueueRound(hash, (uint)(value5?.GetHashCode() ?? 0));
        hash = QueueRound(hash, (uint)(value6?.GetHashCode() ?? 0));
        return (int)MixFinal(hash);
    }

    public static int Combine<T1, T2, T3, T4, T5, T6, T7>(T1 value1, T2 value2, T3 value3, T4 value4, T5 value5, T6 value6, T7 value7)
    {
        Initialize(out var v1, out var v2, out var v3, out var v4);
        v1 = Round(v1, (uint)(value1?.GetHashCode() ?? 0));
        v2 = Round(v2, (uint)(value2?.GetHashCode() ?? 0));
        v3 = Round(v3, (uint)(value3?.GetHashCode() ?? 0));
        v4 = Round(v4, (uint)(value4?.GetHashCode() ?? 0));
        var hash = MixState(v1, v2, v3, v4) + 28;
        hash = QueueRound(hash, (uint)(value5?.GetHashCode() ?? 0));
        hash = QueueRound(hash, (uint)(value6?.GetHashCode() ?? 0));
        hash = QueueRound(hash, (uint)(value7?.GetHashCode() ?? 0));
        return (int)MixFinal(hash);
    }

    public static int Combine<T1, T2, T3, T4, T5, T6, T7, T8>(T1 value1, T2 value2, T3 value3, T4 value4, T5 value5, T6 value6, T7 value7, T8 value8)
    {
        Initialize(out var v1, out var v2, out var v3, out var v4);
        v1 = Round(v1, (uint)(value1?.GetHashCode() ?? 0));
        v2 = Round(v2, (uint)(value2?.GetHashCode() ?? 0));
        v3 = Round(v3, (uint)(value3?.GetHashCode() ?? 0));
        v4 = Round(v4, (uint)(value4?.GetHashCode() ?? 0));
        v1 = Round(v1, (uint)(value5?.GetHashCode() ?? 0));
        v2 = Round(v2, (uint)(value6?.GetHashCode() ?? 0));
        v3 = Round(v3, (uint)(value7?.GetHashCode() ?? 0));
        v4 = Round(v4, (uint)(value8?.GetHashCode() ?? 0));
        return (int)MixFinal(MixState(v1, v2, v3, v4) + 32);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void Initialize(out uint v1, out uint v2, out uint v3, out uint v4)
    {
        v1 = s_seed + Prime1 + Prime2;
        v2 = s_seed + Prime2;
        v3 = s_seed;
        v4 = s_seed - Prime1;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static uint Round(uint hash, uint input) =>
        RotateLeft(hash + input * Prime2, 13) * Prime1;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static uint QueueRound(uint hash, uint queuedValue) =>
        RotateLeft(hash + queuedValue * Prime3, 17) * Prime4;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static uint MixState(uint v1, uint v2, uint v3, uint v4) =>
        RotateLeft(v1, 1) + RotateLeft(v2, 7) + RotateLeft(v3, 12) + RotateLeft(v4, 18);

    private static uint MixEmptyState() => s_seed + Prime5;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static uint MixFinal(uint hash)
    {
        hash ^= hash >> 15;
        hash *= Prime2;
        hash ^= hash >> 13;
        hash *= Prime3;
        hash ^= hash >> 16;
        return hash;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static uint RotateLeft(uint value, int offset) =>
        (value << offset) | (value >> (32 - offset));

    public void Add<T>(T value) => Add(value?.GetHashCode() ?? 0);

    public void Add<T>(T value, IEqualityComparer<T>? comparer) =>
        Add(value is null ? 0 : (comparer?.GetHashCode(value) ?? value.GetHashCode()));

    /// <summary>Adds a span of bytes to the hash code.</summary>
    /// <param name="value">The span.</param>
    /// <remarks>
    /// This method does not guarantee that the result of adding a span of bytes
    /// will match the result of adding the same bytes individually.
    /// </remarks>
    public void AddBytes(ReadOnlySpan<byte> value)
    {
        if (value.Length < sizeof(int) * 4)
        {
            goto Small;
        }

        if (_length == 0)
        {
            Initialize(out _v1, out _v2, out _v3, out _v4);
        }
        else
        {
            switch (_length % 4)
            {
                case 1:
                    Add(ReadInt32(value));
                    value = value.Slice(sizeof(int));
                    goto case 2;
                case 2:
                    Add(ReadInt32(value));
                    value = value.Slice(sizeof(int));
                    goto case 3;
                case 3:
                    Add(ReadInt32(value));
                    value = value.Slice(sizeof(int));
                    break;
            }
        }

        while (value.Length >= sizeof(int) * 4)
        {
            _v1 = Round(_v1, ReadUInt32(value));
            _v2 = Round(_v2, ReadUInt32(value.Slice(sizeof(int))));
            _v3 = Round(_v3, ReadUInt32(value.Slice(sizeof(int) * 2)));
            _v4 = Round(_v4, ReadUInt32(value.Slice(sizeof(int) * 3)));
            _length += 4;
            value = value.Slice(sizeof(int) * 4);
        }

    Small:
        while (value.Length >= sizeof(int))
        {
            Add(ReadInt32(value));
            value = value.Slice(sizeof(int));
        }

        for (var index = 0; index < value.Length; index++)
        {
            Add((int)value[index]);
        }
    }

    private void Add(int value)
    {
        var val = (uint)value;
        var previousLength = _length++;
        var position = previousLength % 4;

        if (position == 0)
        {
            _queue1 = val;
        }
        else if (position == 1)
        {
            _queue2 = val;
        }
        else if (position == 2)
        {
            _queue3 = val;
        }
        else
        {
            if (previousLength == 3)
            {
                Initialize(out _v1, out _v2, out _v3, out _v4);
            }

            _v1 = Round(_v1, _queue1);
            _v2 = Round(_v2, _queue2);
            _v3 = Round(_v3, _queue3);
            _v4 = Round(_v4, val);
        }
    }

    public int ToHashCode()
    {
        var length = _length;
        var position = length % 4;
        var hash = length < 4 ? MixEmptyState() : MixState(_v1, _v2, _v3, _v4);
        hash += length * 4;

        if (position > 0)
        {
            hash = QueueRound(hash, _queue1);
            if (position > 1)
            {
                hash = QueueRound(hash, _queue2);
                if (position > 2)
                {
                    hash = QueueRound(hash, _queue3);
                }
            }
        }

        return (int)MixFinal(hash);
    }

#pragma warning disable 0809
    [Obsolete("HashCode is a mutable struct and should not be compared with other HashCodes. Use ToHashCode to retrieve the computed hash code.", error: true)]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public override int GetHashCode() =>
        throw new NotSupportedException(
            "HashCode is a mutable struct and should not be compared with other HashCodes. Use ToHashCode to retrieve the computed hash code.");

    [Obsolete("HashCode is a mutable struct and should not be compared with other HashCodes.", error: true)]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public override bool Equals(object? obj) =>
        throw new NotSupportedException(
            "HashCode is a mutable struct and should not be compared with other HashCodes.");
#pragma warning restore 0809

    private static uint ReadUInt32(ReadOnlySpan<byte> value) =>
        (uint)(value[0] |
            ((uint)value[1] << 8) |
            ((uint)value[2] << 16) |
            ((uint)value[3] << 24));

    private static int ReadInt32(ReadOnlySpan<byte> value) =>
        unchecked((int)ReadUInt32(value));
}
