// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Globalization;

namespace System
{
    // The invariant conversion matrix is adapted from System.Convert in
    // dotnet/runtime. Provider arguments are accepted for API compatibility,
    // but this CoreLib profile intentionally has no culture data; numeric
    // parsing and formatting therefore use the invariant primitive contracts.
    public static partial class Convert
    {
        public static object? ChangeType(object? value, TypeCode typeCode) => ChangeType(value, typeCode, null);

        public static object? ChangeType(object? value, TypeCode typeCode, IFormatProvider? provider)
        {
            if (value is null && typeCode is TypeCode.Empty or TypeCode.String or TypeCode.Object)
            {
                return null;
            }

            return typeCode switch
            {
                TypeCode.Boolean => ToBoolean(value, provider),
                TypeCode.Char => ToChar(value, provider),
                TypeCode.SByte => ToSByte(value, provider),
                TypeCode.Byte => ToByte(value, provider),
                TypeCode.Int16 => ToInt16(value, provider),
                TypeCode.UInt16 => ToUInt16(value, provider),
                TypeCode.Int32 => ToInt32(value, provider),
                TypeCode.UInt32 => ToUInt32(value, provider),
                TypeCode.Int64 => ToInt64(value, provider),
                TypeCode.UInt64 => ToUInt64(value, provider),
                TypeCode.Single => ToSingle(value, provider),
                TypeCode.Double => ToDouble(value, provider),
                TypeCode.Decimal => ToDecimal(value, provider),
                TypeCode.String => ToString(value, provider),
                TypeCode.Object => value,
                TypeCode.DateTime => throw new InvalidCastException(),
                TypeCode.Empty or TypeCode.DBNull => throw new InvalidCastException(),
                _ => throw new ArgumentException(),
            };
        }

        public static bool ToBoolean(bool value) => value;
        public static bool ToBoolean(sbyte value) => value != 0;
        public static bool ToBoolean(byte value) => value != 0;
        public static bool ToBoolean(char value) => throw new InvalidCastException();
        public static bool ToBoolean(short value) => value != 0;
        public static bool ToBoolean(ushort value) => value != 0;
        public static bool ToBoolean(int value) => value != 0;
        public static bool ToBoolean(uint value) => value != 0;
        public static bool ToBoolean(long value) => value != 0;
        public static bool ToBoolean(ulong value) => value != 0;
        public static bool ToBoolean(float value) => value != 0;
        public static bool ToBoolean(double value) => value != 0;
        public static bool ToBoolean(decimal value) => value != 0;
        public static bool ToBoolean(DateTime value) => throw new InvalidCastException();

        public static bool ToBoolean(string? value) => value is null ? false : bool.Parse(value);
        public static bool ToBoolean(string? value, IFormatProvider? provider) => ToBoolean(value);

        public static bool ToBoolean(object? value) => ToBoolean(value, null);
        public static bool ToBoolean(object? value, IFormatProvider? provider) => value switch
        {
            null => false,
            bool v => ToBoolean(v),
            sbyte v => ToBoolean(v),
            byte v => ToBoolean(v),
            char v => ToBoolean(v),
            short v => ToBoolean(v),
            ushort v => ToBoolean(v),
            int v => ToBoolean(v),
            uint v => ToBoolean(v),
            long v => ToBoolean(v),
            ulong v => ToBoolean(v),
            float v => ToBoolean(v),
            double v => ToBoolean(v),
            decimal v => ToBoolean(v),
            string v => ToBoolean(v),
            IConvertible v => v.ToBoolean(provider),
            _ => throw new InvalidCastException(),
        };

        public static char ToChar(char value) => value;
        public static char ToChar(sbyte value) => value < 0 ? throw new OverflowException() : (char)value;
        public static char ToChar(byte value) => (char)value;
        public static char ToChar(short value) => value < 0 ? throw new OverflowException() : (char)value;
        public static char ToChar(ushort value) => (char)value;
        public static char ToChar(int value) => value < 0 || value > char.MaxValue ? throw new OverflowException() : (char)value;
        public static char ToChar(uint value) => value > char.MaxValue ? throw new OverflowException() : (char)value;
        public static char ToChar(long value) => value < 0 || value > char.MaxValue ? throw new OverflowException() : (char)value;
        public static char ToChar(ulong value) => value > char.MaxValue ? throw new OverflowException() : (char)value;
        public static char ToChar(bool value) => throw new InvalidCastException();
        public static char ToChar(float value) => throw new InvalidCastException();
        public static char ToChar(double value) => throw new InvalidCastException();
        public static char ToChar(decimal value) => throw new InvalidCastException();
        public static char ToChar(DateTime value) => throw new InvalidCastException();

        public static char ToChar(string value) => ToChar(value, null);
        public static char ToChar(string value, IFormatProvider? provider)
        {
            ArgumentNullException.ThrowIfNull(value);
            return value.Length == 1 ? value[0] : throw new FormatException();
        }

        public static char ToChar(object? value) => ToChar(value, null);
        public static char ToChar(object? value, IFormatProvider? provider) => value switch
        {
            null => '\0',
            char v => ToChar(v),
            sbyte v => ToChar(v),
            byte v => ToChar(v),
            short v => ToChar(v),
            ushort v => ToChar(v),
            int v => ToChar(v),
            uint v => ToChar(v),
            long v => ToChar(v),
            ulong v => ToChar(v),
            bool v => ToChar(v),
            float v => ToChar(v),
            double v => ToChar(v),
            decimal v => ToChar(v),
            string v => ToChar(v, provider),
            IConvertible v => v.ToChar(provider),
            _ => throw new InvalidCastException(),
        };

        public static sbyte ToSByte(bool value) => value ? (sbyte)1 : (sbyte)0;
        public static sbyte ToSByte(sbyte value) => value;
        public static sbyte ToSByte(char value) => value > sbyte.MaxValue ? throw new OverflowException() : (sbyte)value;
        public static sbyte ToSByte(byte value) => value > sbyte.MaxValue ? throw new OverflowException() : (sbyte)value;
        public static sbyte ToSByte(short value) => value < sbyte.MinValue || value > sbyte.MaxValue ? throw new OverflowException() : (sbyte)value;
        public static sbyte ToSByte(ushort value) => value > sbyte.MaxValue ? throw new OverflowException() : (sbyte)value;
        public static sbyte ToSByte(int value) => value < sbyte.MinValue || value > sbyte.MaxValue ? throw new OverflowException() : (sbyte)value;
        public static sbyte ToSByte(uint value) => value > (uint)sbyte.MaxValue ? throw new OverflowException() : (sbyte)value;
        public static sbyte ToSByte(long value) => value < sbyte.MinValue || value > sbyte.MaxValue ? throw new OverflowException() : (sbyte)value;
        public static sbyte ToSByte(ulong value) => value > (ulong)sbyte.MaxValue ? throw new OverflowException() : (sbyte)value;
        public static sbyte ToSByte(float value) => ToSByte(ToInt32((double)value));
        public static sbyte ToSByte(double value) => ToSByte(ToInt32(value));
        public static sbyte ToSByte(decimal value) => checked((sbyte)decimal.Round(value));
        public static sbyte ToSByte(DateTime value) => throw new InvalidCastException();
        public static sbyte ToSByte(string? value) => value is null ? (sbyte)0 : sbyte.Parse(value);
        public static sbyte ToSByte(string? value, IFormatProvider? provider) => ToSByte(value);
        public static sbyte ToSByte(string? value, int fromBase) => value is null ? (sbyte)0 : (sbyte)ParseSignedBase(value, fromBase, 8);
        public static sbyte ToSByte(object? value) => ToSByte(value, null);
        public static sbyte ToSByte(object? value, IFormatProvider? provider) => value switch
        {
            null => (sbyte)0,
            bool v => ToSByte(v),
            sbyte v => v,
            byte v => ToSByte(v),
            char v => ToSByte(v),
            short v => ToSByte(v),
            ushort v => ToSByte(v),
            int v => ToSByte(v),
            uint v => ToSByte(v),
            long v => ToSByte(v),
            ulong v => ToSByte(v),
            float v => ToSByte(v),
            double v => ToSByte(v),
            decimal v => ToSByte(v),
            string v => ToSByte(v),
            IConvertible v => v.ToSByte(provider),
            _ => throw new InvalidCastException(),
        };

        public static byte ToByte(bool value) => value ? (byte)1 : (byte)0;
        public static byte ToByte(byte value) => value;
        public static byte ToByte(char value) => value > byte.MaxValue ? throw new OverflowException() : (byte)value;
        public static byte ToByte(sbyte value) => value < 0 ? throw new OverflowException() : (byte)value;
        public static byte ToByte(short value) => value < 0 || value > byte.MaxValue ? throw new OverflowException() : (byte)value;
        public static byte ToByte(ushort value) => value > byte.MaxValue ? throw new OverflowException() : (byte)value;
        public static byte ToByte(int value) => value < 0 || value > byte.MaxValue ? throw new OverflowException() : (byte)value;
        public static byte ToByte(uint value) => value > byte.MaxValue ? throw new OverflowException() : (byte)value;
        public static byte ToByte(long value) => value < 0 || value > byte.MaxValue ? throw new OverflowException() : (byte)value;
        public static byte ToByte(ulong value) => value > byte.MaxValue ? throw new OverflowException() : (byte)value;
        public static byte ToByte(float value) => ToByte(ToInt32((double)value));
        public static byte ToByte(double value) => ToByte(ToInt32(value));
        public static byte ToByte(decimal value) => checked((byte)decimal.Round(value));
        public static byte ToByte(DateTime value) => throw new InvalidCastException();
        public static byte ToByte(string? value) => value is null ? (byte)0 : byte.Parse(value);
        public static byte ToByte(string? value, IFormatProvider? provider) => ToByte(value);
        public static byte ToByte(string? value, int fromBase) => value is null ? (byte)0 : ParseUnsignedBase(value, fromBase, 8) is var parsed && parsed <= byte.MaxValue ? (byte)parsed : throw new OverflowException();
        public static byte ToByte(object? value) => ToByte(value, null);
        public static byte ToByte(object? value, IFormatProvider? provider) => value switch
        {
            null => (byte)0,
            bool v => ToByte(v),
            byte v => v,
            char v => ToByte(v),
            sbyte v => ToByte(v),
            short v => ToByte(v),
            ushort v => ToByte(v),
            int v => ToByte(v),
            uint v => ToByte(v),
            long v => ToByte(v),
            ulong v => ToByte(v),
            float v => ToByte(v),
            double v => ToByte(v),
            decimal v => ToByte(v),
            string v => ToByte(v),
            IConvertible v => v.ToByte(provider),
            _ => throw new InvalidCastException(),
        };

        public static short ToInt16(bool value) => value ? (short)1 : (short)0;
        public static short ToInt16(char value) => value > short.MaxValue ? throw new OverflowException() : (short)value;
        public static short ToInt16(sbyte value) => value;
        public static short ToInt16(byte value) => value;
        public static short ToInt16(short value) => value;
        public static short ToInt16(ushort value) => value > short.MaxValue ? throw new OverflowException() : (short)value;
        public static short ToInt16(int value) => value < short.MinValue || value > short.MaxValue ? throw new OverflowException() : (short)value;
        public static short ToInt16(uint value) => value > (uint)short.MaxValue ? throw new OverflowException() : (short)value;
        public static short ToInt16(long value) => value < short.MinValue || value > short.MaxValue ? throw new OverflowException() : (short)value;
        public static short ToInt16(ulong value) => value > (ulong)short.MaxValue ? throw new OverflowException() : (short)value;
        public static short ToInt16(float value) => ToInt16(ToInt32((double)value));
        public static short ToInt16(double value) => ToInt16(ToInt32(value));
        public static short ToInt16(decimal value) => checked((short)decimal.Round(value));
        public static short ToInt16(DateTime value) => throw new InvalidCastException();
        public static short ToInt16(string? value) => value is null ? (short)0 : short.Parse(value);
        public static short ToInt16(string? value, IFormatProvider? provider) => ToInt16(value);
        public static short ToInt16(string? value, int fromBase) => value is null ? (short)0 : (short)ParseSignedBase(value, fromBase, 16);
        public static short ToInt16(object? value) => ToInt16(value, null);
        public static short ToInt16(object? value, IFormatProvider? provider) => value switch
        {
            null => (short)0,
            bool v => ToInt16(v),
            char v => ToInt16(v),
            sbyte v => v,
            byte v => v,
            short v => v,
            ushort v => ToInt16(v),
            int v => ToInt16(v),
            uint v => ToInt16(v),
            long v => ToInt16(v),
            ulong v => ToInt16(v),
            float v => ToInt16(v),
            double v => ToInt16(v),
            decimal v => ToInt16(v),
            string v => ToInt16(v),
            IConvertible v => v.ToInt16(provider),
            _ => throw new InvalidCastException(),
        };

        public static ushort ToUInt16(bool value) => value ? (ushort)1 : (ushort)0;
        public static ushort ToUInt16(char value) => value;
        public static ushort ToUInt16(sbyte value) => value < 0 ? throw new OverflowException() : (ushort)value;
        public static ushort ToUInt16(byte value) => value;
        public static ushort ToUInt16(short value) => value < 0 ? throw new OverflowException() : (ushort)value;
        public static ushort ToUInt16(ushort value) => value;
        public static ushort ToUInt16(int value) => value < 0 || value > ushort.MaxValue ? throw new OverflowException() : (ushort)value;
        public static ushort ToUInt16(uint value) => value > ushort.MaxValue ? throw new OverflowException() : (ushort)value;
        public static ushort ToUInt16(long value) => value < 0 || value > ushort.MaxValue ? throw new OverflowException() : (ushort)value;
        public static ushort ToUInt16(ulong value) => value > ushort.MaxValue ? throw new OverflowException() : (ushort)value;
        public static ushort ToUInt16(float value) => ToUInt16(ToInt32((double)value));
        public static ushort ToUInt16(double value) => ToUInt16(ToInt32(value));
        public static ushort ToUInt16(decimal value) => checked((ushort)decimal.Round(value));
        public static ushort ToUInt16(DateTime value) => throw new InvalidCastException();
        public static ushort ToUInt16(string? value) => value is null ? (ushort)0 : ushort.Parse(value);
        public static ushort ToUInt16(string? value, IFormatProvider? provider) => ToUInt16(value);
        public static ushort ToUInt16(string? value, int fromBase) => value is null ? (ushort)0 : ParseUnsignedBase(value, fromBase, 16) is var parsed && parsed <= ushort.MaxValue ? (ushort)parsed : throw new OverflowException();
        public static ushort ToUInt16(object? value) => ToUInt16(value, null);
        public static ushort ToUInt16(object? value, IFormatProvider? provider) => value switch
        {
            null => (ushort)0,
            bool v => ToUInt16(v),
            char v => v,
            sbyte v => ToUInt16(v),
            byte v => v,
            short v => ToUInt16(v),
            ushort v => v,
            int v => ToUInt16(v),
            uint v => ToUInt16(v),
            long v => ToUInt16(v),
            ulong v => ToUInt16(v),
            float v => ToUInt16(v),
            double v => ToUInt16(v),
            decimal v => ToUInt16(v),
            string v => ToUInt16(v),
            IConvertible v => v.ToUInt16(provider),
            _ => throw new InvalidCastException(),
        };

        public static int ToInt32(bool value) => value ? 1 : 0;
        public static int ToInt32(char value) => value;
        public static int ToInt32(sbyte value) => value;
        public static int ToInt32(byte value) => value;
        public static int ToInt32(short value) => value;
        public static int ToInt32(ushort value) => value;
        public static int ToInt32(int value) => value;
        public static int ToInt32(uint value) => value > int.MaxValue ? throw new OverflowException() : (int)value;
        public static int ToInt32(long value) => value < int.MinValue || value > int.MaxValue ? throw new OverflowException() : (int)value;
        public static int ToInt32(ulong value) => value > int.MaxValue ? throw new OverflowException() : (int)value;
        public static int ToInt32(float value) => ToInt32((double)value);
        public static int ToInt32(double value)
        {
            var rounded = Math.Round(value);
            return double.IsNaN(value) || rounded < int.MinValue || rounded > int.MaxValue ? throw new OverflowException() : (int)rounded;
        }
        public static int ToInt32(decimal value) => checked((int)decimal.Round(value));
        public static int ToInt32(DateTime value) => throw new InvalidCastException();
        public static int ToInt32(string? value) => value is null ? 0 : int.Parse(value);
        public static int ToInt32(string? value, IFormatProvider? provider) => ToInt32(value);
        public static int ToInt32(string? value, int fromBase) => value is null ? 0 : (int)ParseSignedBase(value, fromBase, 32);
        public static int ToInt32(object? value) => ToInt32(value, null);
        public static int ToInt32(object? value, IFormatProvider? provider) => value switch
        {
            null => 0,
            bool v => ToInt32(v),
            char v => v,
            sbyte v => v,
            byte v => v,
            short v => v,
            ushort v => v,
            int v => v,
            uint v => ToInt32(v),
            long v => ToInt32(v),
            ulong v => ToInt32(v),
            float v => ToInt32(v),
            double v => ToInt32(v),
            decimal v => ToInt32(v),
            string v => ToInt32(v),
            IConvertible v => v.ToInt32(provider),
            _ => throw new InvalidCastException(),
        };

        public static uint ToUInt32(bool value) => value ? 1U : 0U;
        public static uint ToUInt32(char value) => value;
        public static uint ToUInt32(sbyte value) => value < 0 ? throw new OverflowException() : (uint)value;
        public static uint ToUInt32(byte value) => value;
        public static uint ToUInt32(short value) => value < 0 ? throw new OverflowException() : (uint)value;
        public static uint ToUInt32(ushort value) => value;
        public static uint ToUInt32(int value) => value < 0 ? throw new OverflowException() : (uint)value;
        public static uint ToUInt32(uint value) => value;
        public static uint ToUInt32(long value) => value < 0 || value > uint.MaxValue ? throw new OverflowException() : (uint)value;
        public static uint ToUInt32(ulong value) => value > uint.MaxValue ? throw new OverflowException() : (uint)value;
        public static uint ToUInt32(float value) => ToUInt32((double)value);
        public static uint ToUInt32(double value)
        {
            var rounded = Math.Round(value);
            return double.IsNaN(value) || rounded < 0 || rounded > uint.MaxValue ? throw new OverflowException() : (uint)rounded;
        }
        public static uint ToUInt32(decimal value) => checked((uint)decimal.Round(value));
        public static uint ToUInt32(DateTime value) => throw new InvalidCastException();
        public static uint ToUInt32(string? value) => value is null ? 0U : uint.Parse(value);
        public static uint ToUInt32(string? value, IFormatProvider? provider) => ToUInt32(value);
        public static uint ToUInt32(string? value, int fromBase) => value is null ? 0U : ParseUnsignedBase(value, fromBase, 32) is var parsed && parsed <= uint.MaxValue ? (uint)parsed : throw new OverflowException();
        public static uint ToUInt32(object? value) => ToUInt32(value, null);
        public static uint ToUInt32(object? value, IFormatProvider? provider) => value switch
        {
            null => 0U,
            bool v => ToUInt32(v),
            char v => v,
            sbyte v => ToUInt32(v),
            byte v => v,
            short v => ToUInt32(v),
            ushort v => v,
            int v => ToUInt32(v),
            uint v => v,
            long v => ToUInt32(v),
            ulong v => ToUInt32(v),
            float v => ToUInt32(v),
            double v => ToUInt32(v),
            decimal v => ToUInt32(v),
            string v => ToUInt32(v),
            IConvertible v => v.ToUInt32(provider),
            _ => throw new InvalidCastException(),
        };

        public static long ToInt64(bool value) => value ? 1L : 0L;
        public static long ToInt64(char value) => value;
        public static long ToInt64(sbyte value) => value;
        public static long ToInt64(byte value) => value;
        public static long ToInt64(short value) => value;
        public static long ToInt64(ushort value) => value;
        public static long ToInt64(int value) => value;
        public static long ToInt64(uint value) => value;
        public static long ToInt64(long value) => value;
        public static long ToInt64(ulong value) => value > long.MaxValue ? throw new OverflowException() : (long)value;
        public static long ToInt64(float value) => ToInt64((double)value);
        public static long ToInt64(double value)
        {
            var rounded = Math.Round(value);
            return double.IsNaN(value) || rounded < long.MinValue || rounded > long.MaxValue ? throw new OverflowException() : (long)rounded;
        }
        public static long ToInt64(decimal value) => checked((long)decimal.Round(value));
        public static long ToInt64(DateTime value) => throw new InvalidCastException();
        public static long ToInt64(string? value) => value is null ? 0L : long.Parse(value);
        public static long ToInt64(string? value, IFormatProvider? provider) => ToInt64(value);
        public static long ToInt64(string? value, int fromBase) => value is null ? 0L : ParseSignedBase(value, fromBase, 64);
        public static long ToInt64(object? value) => ToInt64(value, null);
        public static long ToInt64(object? value, IFormatProvider? provider) => value switch
        {
            null => 0L,
            bool v => ToInt64(v),
            char v => v,
            sbyte v => v,
            byte v => v,
            short v => v,
            ushort v => v,
            int v => v,
            uint v => v,
            long v => v,
            ulong v => ToInt64(v),
            float v => ToInt64(v),
            double v => ToInt64(v),
            decimal v => ToInt64(v),
            string v => ToInt64(v),
            IConvertible v => v.ToInt64(provider),
            _ => throw new InvalidCastException(),
        };

        public static ulong ToUInt64(bool value) => value ? 1UL : 0UL;
        public static ulong ToUInt64(char value) => value;
        public static ulong ToUInt64(sbyte value) => value < 0 ? throw new OverflowException() : (ulong)value;
        public static ulong ToUInt64(byte value) => value;
        public static ulong ToUInt64(short value) => value < 0 ? throw new OverflowException() : (ulong)value;
        public static ulong ToUInt64(ushort value) => value;
        public static ulong ToUInt64(int value) => value < 0 ? throw new OverflowException() : (ulong)value;
        public static ulong ToUInt64(uint value) => value;
        public static ulong ToUInt64(long value) => value < 0 ? throw new OverflowException() : (ulong)value;
        public static ulong ToUInt64(ulong value) => value;
        public static ulong ToUInt64(float value) => ToUInt64((double)value);
        public static ulong ToUInt64(double value)
        {
            var rounded = Math.Round(value);
            return double.IsNaN(value) || rounded < 0 || rounded > ulong.MaxValue ? throw new OverflowException() : (ulong)rounded;
        }
        public static ulong ToUInt64(decimal value) => checked((ulong)decimal.Round(value));
        public static ulong ToUInt64(DateTime value) => throw new InvalidCastException();
        public static ulong ToUInt64(string? value) => value is null ? 0UL : ulong.Parse(value);
        public static ulong ToUInt64(string? value, IFormatProvider? provider) => ToUInt64(value);
        public static ulong ToUInt64(string? value, int fromBase) => value is null ? 0UL : ParseUnsignedBase(value, fromBase, 64);
        public static ulong ToUInt64(object? value) => ToUInt64(value, null);
        public static ulong ToUInt64(object? value, IFormatProvider? provider) => value switch
        {
            null => 0UL,
            bool v => ToUInt64(v),
            char v => v,
            sbyte v => ToUInt64(v),
            byte v => v,
            short v => ToUInt64(v),
            ushort v => v,
            int v => ToUInt64(v),
            uint v => v,
            long v => ToUInt64(v),
            ulong v => v,
            float v => ToUInt64(v),
            double v => ToUInt64(v),
            decimal v => ToUInt64(v),
            string v => ToUInt64(v),
            IConvertible v => v.ToUInt64(provider),
            _ => throw new InvalidCastException(),
        };

        public static float ToSingle(bool value) => value ? 1 : 0;
        public static float ToSingle(sbyte value) => value;
        public static float ToSingle(byte value) => value;
        public static float ToSingle(char value) => value;
        public static float ToSingle(short value) => value;
        public static float ToSingle(ushort value) => value;
        public static float ToSingle(int value) => value;
        public static float ToSingle(uint value) => value;
        public static float ToSingle(long value) => value;
        public static float ToSingle(ulong value) => value;
        public static float ToSingle(float value) => value;
        public static float ToSingle(double value) => (float)value;
        public static float ToSingle(decimal value) => (float)value;
        public static float ToSingle(DateTime value) => throw new InvalidCastException();
        public static float ToSingle(string? value) => value is null ? 0 : float.Parse(value);
        public static float ToSingle(string? value, IFormatProvider? provider) => ToSingle(value);
        public static float ToSingle(object? value) => ToSingle(value, null);
        public static float ToSingle(object? value, IFormatProvider? provider) => value switch
        {
            null => 0,
            bool v => ToSingle(v),
            sbyte v => ToSingle(v),
            byte v => ToSingle(v),
            char v => ToSingle(v),
            short v => ToSingle(v),
            ushort v => ToSingle(v),
            int v => ToSingle(v),
            uint v => ToSingle(v),
            long v => ToSingle(v),
            ulong v => ToSingle(v),
            float v => v,
            double v => ToSingle(v),
            decimal v => ToSingle(v),
            string v => ToSingle(v),
            IConvertible v => v.ToSingle(provider),
            _ => throw new InvalidCastException(),
        };

        public static double ToDouble(bool value) => value ? 1 : 0;
        public static double ToDouble(sbyte value) => value;
        public static double ToDouble(byte value) => value;
        public static double ToDouble(char value) => value;
        public static double ToDouble(short value) => value;
        public static double ToDouble(ushort value) => value;
        public static double ToDouble(int value) => value;
        public static double ToDouble(uint value) => value;
        public static double ToDouble(long value) => value;
        public static double ToDouble(ulong value) => value;
        public static double ToDouble(float value) => value;
        public static double ToDouble(double value) => value;
        public static double ToDouble(decimal value) => (double)value;
        public static double ToDouble(DateTime value) => throw new InvalidCastException();
        public static double ToDouble(string? value) => value is null ? 0 : double.Parse(value);
        public static double ToDouble(string? value, IFormatProvider? provider) => ToDouble(value);
        public static double ToDouble(object? value) => ToDouble(value, null);
        public static double ToDouble(object? value, IFormatProvider? provider) => value switch
        {
            null => 0,
            bool v => ToDouble(v),
            sbyte v => ToDouble(v),
            byte v => ToDouble(v),
            char v => ToDouble(v),
            short v => ToDouble(v),
            ushort v => ToDouble(v),
            int v => ToDouble(v),
            uint v => ToDouble(v),
            long v => ToDouble(v),
            ulong v => ToDouble(v),
            float v => v,
            double v => v,
            decimal v => ToDouble(v),
            string v => ToDouble(v),
            IConvertible v => v.ToDouble(provider),
            _ => throw new InvalidCastException(),
        };

        public static decimal ToDecimal(bool value) => value ? decimal.One : decimal.Zero;
        public static decimal ToDecimal(sbyte value) => value;
        public static decimal ToDecimal(byte value) => value;
        public static decimal ToDecimal(char value) => (decimal)(uint)value;
        public static decimal ToDecimal(short value) => value;
        public static decimal ToDecimal(ushort value) => value;
        public static decimal ToDecimal(int value) => value;
        public static decimal ToDecimal(uint value) => value;
        public static decimal ToDecimal(long value) => value;
        public static decimal ToDecimal(ulong value) => value;
        public static decimal ToDecimal(float value) => (decimal)value;
        public static decimal ToDecimal(double value) => (decimal)value;
        public static decimal ToDecimal(decimal value) => value;
        public static decimal ToDecimal(DateTime value) => throw new InvalidCastException();
        public static decimal ToDecimal(string? value) => value is null ? decimal.Zero : decimal.Parse(value);
        public static decimal ToDecimal(string? value, IFormatProvider? provider) => ToDecimal(value);
        public static decimal ToDecimal(object? value) => ToDecimal(value, null);
        public static decimal ToDecimal(object? value, IFormatProvider? provider) => value switch
        {
            null => decimal.Zero,
            bool v => ToDecimal(v),
            sbyte v => ToDecimal(v),
            byte v => ToDecimal(v),
            char v => ToDecimal(v),
            short v => ToDecimal(v),
            ushort v => ToDecimal(v),
            int v => ToDecimal(v),
            uint v => ToDecimal(v),
            long v => ToDecimal(v),
            ulong v => ToDecimal(v),
            float v => ToDecimal(v),
            double v => ToDecimal(v),
            decimal v => v,
            string v => ToDecimal(v),
            IConvertible v => v.ToDecimal(provider),
            _ => throw new InvalidCastException(),
        };

        public static DateTime ToDateTime(DateTime value) => value;

        public static DateTime ToDateTime(object? value) => value switch
        {
            null => DateTime.MinValue,
            DateTime dateTime => dateTime,
            IConvertible convertible => convertible.ToDateTime(null),
            _ => throw new InvalidCastException(),
        };

        public static DateTime ToDateTime(object? value, IFormatProvider? provider) => value switch
        {
            null => DateTime.MinValue,
            DateTime dateTime => dateTime,
            IConvertible convertible => convertible.ToDateTime(provider),
            _ => throw new InvalidCastException(),
        };

        public static DateTime ToDateTime(string? value) =>
            value is null ? DateTime.MinValue : DateTime.Parse(value);

        public static DateTime ToDateTime(string? value, IFormatProvider? provider) =>
            value is null ? DateTime.MinValue : DateTime.Parse(value, provider);

        public static DateTime ToDateTime(bool value) => throw new InvalidCastException();
        public static DateTime ToDateTime(char value) => throw new InvalidCastException();
        public static DateTime ToDateTime(sbyte value) => throw new InvalidCastException();
        public static DateTime ToDateTime(byte value) => throw new InvalidCastException();
        public static DateTime ToDateTime(short value) => throw new InvalidCastException();
        public static DateTime ToDateTime(ushort value) => throw new InvalidCastException();
        public static DateTime ToDateTime(int value) => throw new InvalidCastException();
        public static DateTime ToDateTime(uint value) => throw new InvalidCastException();
        public static DateTime ToDateTime(long value) => throw new InvalidCastException();
        public static DateTime ToDateTime(ulong value) => throw new InvalidCastException();
        public static DateTime ToDateTime(float value) => throw new InvalidCastException();
        public static DateTime ToDateTime(double value) => throw new InvalidCastException();
        public static DateTime ToDateTime(decimal value) => throw new InvalidCastException();

        public static string ToString(bool value) => value.ToString();
        public static string ToString(bool value, IFormatProvider? provider) => value.ToString();
        public static string ToString(char value) => value.ToString();
        public static string ToString(char value, IFormatProvider? provider) => value.ToString();
        public static string ToString(sbyte value) => value.ToString();
        public static string ToString(sbyte value, IFormatProvider? provider) => value.ToString();
        public static string ToString(byte value) => value.ToString();
        public static string ToString(byte value, IFormatProvider? provider) => value.ToString();
        public static string ToString(short value) => value.ToString();
        public static string ToString(short value, IFormatProvider? provider) => value.ToString();
        public static string ToString(ushort value) => value.ToString();
        public static string ToString(ushort value, IFormatProvider? provider) => value.ToString();
        public static string ToString(int value) => value.ToString();
        public static string ToString(int value, IFormatProvider? provider) => value.ToString();
        public static string ToString(uint value) => value.ToString();
        public static string ToString(uint value, IFormatProvider? provider) => value.ToString();
        public static string ToString(long value) => value.ToString();
        public static string ToString(long value, IFormatProvider? provider) => value.ToString();
        public static string ToString(ulong value) => value.ToString();
        public static string ToString(ulong value, IFormatProvider? provider) => value.ToString();
        public static string ToString(float value) => value.ToString();
        public static string ToString(float value, IFormatProvider? provider) => value.ToString();
        public static string ToString(double value) => value.ToString();
        public static string ToString(double value, IFormatProvider? provider) => value.ToString();
        public static string ToString(decimal value) => value.ToString();
        public static string ToString(decimal value, IFormatProvider? provider) => value.ToString();
        public static string ToString(DateTime value) => value.ToString();
        public static string ToString(DateTime value, IFormatProvider? provider) => value.ToString(provider);
        public static string? ToString(string? value) => value;
        public static string? ToString(string? value, IFormatProvider? provider) => value;

        public static string? ToString(object? value) => ToString(value, null);
        public static string? ToString(object? value, IFormatProvider? provider) => value switch
        {
            null => string.Empty,
            bool v => ToString(v),
            char v => ToString(v),
            sbyte v => ToString(v),
            byte v => ToString(v),
            short v => ToString(v),
            ushort v => ToString(v),
            int v => ToString(v),
            uint v => ToString(v),
            long v => ToString(v),
            ulong v => ToString(v),
            float v => ToString(v),
            double v => ToString(v),
            decimal v => ToString(v),
            string v => v,
            IConvertible v => v.ToString(provider),
            IFormattable v => v.ToString(null, provider),
            _ => value.ToString(),
        };

        public static string ToString(byte value, int toBase) => ToString((int)value, toBase);
        public static string ToString(short value, int toBase) => FormatBaseSigned(value, 16, toBase);
        public static string ToString(int value, int toBase) => FormatBaseSigned(value, 32, toBase);
        public static string ToString(long value, int toBase) => FormatBaseSigned(value, 64, toBase);

        private static string FormatBaseSigned(long value, int bits, int toBase)
        {
            return toBase switch
            {
                2 => Number.FormatSigned(value, bits, "b"),
                8 => FormatBaseUnsigned(bits == 64 ? unchecked((ulong)value) : unchecked((ulong)value) & ((1UL << bits) - 1), bits, 8),
                10 => Number.FormatSigned(value, bits, "d"),
                16 => Number.FormatSigned(value, bits, "x"),
                _ => throw new ArgumentException(),
            };
        }

        private static string FormatBaseUnsigned(ulong value, int bits, int radix)
        {
            var buffer = new char[bits == 64 ? 22 : bits / 3 + 2];
            var index = buffer.Length;
            do
            {
                buffer[--index] = (char)('0' + value % (ulong)radix);
                value /= (ulong)radix;
            }
            while (value != 0);
            return string.Create(buffer).Substring(index);
        }

        private static ulong ParseUnsignedBase(string value, int fromBase, int bits)
        {
            ValidateBase(fromBase);
            if (value.Length == 0) throw new FormatException();
            var index = 0;
            if (fromBase == 16 && value.Length >= 2 && value[0] == '0' && (value[1] == 'x' || value[1] == 'X')) index = 2;
            if (index == value.Length) throw new FormatException();
            var maximum = bits == 64 ? ulong.MaxValue : (1UL << bits) - 1;
            var result = 0UL;
            for (; index < value.Length; index++)
            {
                var digit = BaseDigit(value[index]);
                if (digit < 0 || digit >= fromBase) throw new FormatException();
                if (result > (maximum - (uint)digit) / (uint)fromBase) throw new OverflowException();
                result = result * (uint)fromBase + (uint)digit;
            }
            return result;
        }

        private static long ParseSignedBase(string value, int fromBase, int bits)
        {
            ValidateBase(fromBase);
            if (value.Length == 0) throw new FormatException();
            if (fromBase == 10)
            {
                var negative = value[0] == '-';
                var start = negative || value[0] == '+' ? 1 : 0;
                if (start == value.Length) throw new FormatException();
                var magnitude = ParseMagnitude(value, start, fromBase, negative ? (bits == 64 ? 1UL << 63 : (1UL << (bits - 1))) : (bits == 64 ? (ulong)long.MaxValue : (1UL << (bits - 1)) - 1));
                if (negative)
                {
                    if (bits == 64 && magnitude == 1UL << 63) return long.MinValue;
                    return -(long)magnitude;
                }
                return (long)magnitude;
            }

            var raw = ParseUnsignedBase(value, fromBase, bits);
            if (bits == 64) return unchecked((long)raw);
            var sign = 1UL << (bits - 1);
            return (raw & sign) == 0 ? (long)raw : (long)(raw | ~((1UL << bits) - 1));
        }

        private static ulong ParseMagnitude(string value, int start, int radix, ulong maximum)
        {
            var result = 0UL;
            for (var index = start; index < value.Length; index++)
            {
                var digit = BaseDigit(value[index]);
                if (digit < 0 || digit >= radix || result > (maximum - (uint)digit) / (uint)radix) throw new OverflowException();
                result = result * (uint)radix + (uint)digit;
            }
            return result;
        }

        private static int BaseDigit(char value) => value is >= '0' and <= '9' ? value - '0' :
            value is >= 'a' and <= 'f' ? value - 'a' + 10 : value is >= 'A' and <= 'F' ? value - 'A' + 10 : -1;

        private static void ValidateBase(int fromBase)
        {
            if (fromBase is not (2 or 8 or 10 or 16)) throw new ArgumentException();
        }
    }
}
