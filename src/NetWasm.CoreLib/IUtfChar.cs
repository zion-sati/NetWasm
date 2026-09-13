// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
//
// This internal formatting contract is retained from the pinned Microsoft
// CoreLib source so scalar formatting can share char and UTF-8 paths.
// Pinned upstream runtime commit: 811225a482702af7ecc35d817966bc70b88a3a23.

using System.Buffers;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;

namespace System;

internal interface IUtfChar<TSelf> : IEquatable<TSelf>
    where TSelf : unmanaged, IUtfChar<TSelf>
{
    static abstract TSelf CastFrom(byte value);
    static abstract TSelf CastFrom(char value);
    static abstract TSelf CastFrom(int value);
    static abstract TSelf CastFrom(uint value);
    static abstract TSelf CastFrom(ulong value);
    static abstract uint CastToUInt32(TSelf value);
}

internal readonly struct Utf16Char(char value) : IUtfChar<Utf16Char>
{
    public static Utf16Char CastFrom(byte value) => new((char)value);
    public static Utf16Char CastFrom(char value) => new(value);
    public static Utf16Char CastFrom(int value) => new((char)value);
    public static Utf16Char CastFrom(uint value) => new((char)value);
    public static Utf16Char CastFrom(ulong value) => new((char)value);
    private readonly char _value = value;
    public static uint CastToUInt32(Utf16Char value) => value._value;
    public bool Equals(Utf16Char other) => _value == other._value;
}

internal readonly struct Utf8Char(byte value) : IUtfChar<Utf8Char>
{
    public static Utf8Char CastFrom(byte value) => new(value);
    public static Utf8Char CastFrom(char value) => new((byte)value);
    public static Utf8Char CastFrom(int value) => new((byte)value);
    public static Utf8Char CastFrom(uint value) => new((byte)value);
    public static Utf8Char CastFrom(ulong value) => new((byte)value);
    private readonly byte _value = value;
    public static uint CastToUInt32(Utf8Char value) => value._value;
    public bool Equals(Utf8Char other) => _value == other._value;
}

internal static partial class Number
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static bool IsWhiteSpace<TChar>(this ReadOnlySpan<TChar> span, out int elementsConsumed)
        where TChar : unmanaged, IUtfChar<TChar>
    {
        var consumed = 0;
        while (consumed < span.Length)
        {
            if (DecodeFromUtfChar(span[consumed..], out var rune, out var runeConsumed) != OperationStatus.Done ||
                !Rune.IsWhiteSpace(rune))
            {
                elementsConsumed = consumed;
                return false;
            }

            consumed += runeConsumed;
        }

        elementsConsumed = consumed;
        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static OperationStatus DecodeFromUtfChar<TChar>(ReadOnlySpan<TChar> span, out Rune result, out int elementsConsumed)
        where TChar : unmanaged, IUtfChar<TChar> =>
        typeof(TChar) == typeof(Utf8Char)
            ? Rune.DecodeFromUtf8(MemoryMarshal.Cast<TChar, byte>(span), out result, out elementsConsumed)
            : Rune.DecodeFromUtf16(MemoryMarshal.Cast<TChar, char>(span), out result, out elementsConsumed);
}
