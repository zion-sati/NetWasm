// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
//
// IUtfChar members from dotnet/runtime Byte.cs and Char.cs at
// 474ed963699fc29809015392738a15231aaae2de.

namespace System;

public partial struct Byte : IUtfChar<byte>
{
    static byte IUtfChar<byte>.CastFrom(byte value) => value;
    static byte IUtfChar<byte>.CastFrom(char value) => (byte)value;
    static byte IUtfChar<byte>.CastFrom(int value) => (byte)value;
    static byte IUtfChar<byte>.CastFrom(uint value) => (byte)value;
    static byte IUtfChar<byte>.CastFrom(ulong value) => (byte)value;
    static uint IUtfChar<byte>.CastToUInt32(byte value) => value;
}

public partial struct Char : IUtfChar<char>
{
    static char IUtfChar<char>.CastFrom(byte value) => (char)value;
    static char IUtfChar<char>.CastFrom(char value) => value;
    static char IUtfChar<char>.CastFrom(int value) => (char)value;
    static char IUtfChar<char>.CastFrom(uint value) => (char)value;
    static char IUtfChar<char>.CastFrom(ulong value) => (char)value;
    static uint IUtfChar<char>.CastToUInt32(char value) => value;
}
