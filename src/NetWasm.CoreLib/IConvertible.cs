// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace System
{
    // IConvertible is implemented by value types that expose conversions to the
    // primitive System types.  The provider argument is intentionally opaque;
    // each implementation decides whether it needs it for formatting.
    public interface IConvertible
    {
        TypeCode GetTypeCode();

        bool ToBoolean(IFormatProvider? provider);
        char ToChar(IFormatProvider? provider);
        sbyte ToSByte(IFormatProvider? provider);
        byte ToByte(IFormatProvider? provider);
        short ToInt16(IFormatProvider? provider);
        ushort ToUInt16(IFormatProvider? provider);
        int ToInt32(IFormatProvider? provider);
        uint ToUInt32(IFormatProvider? provider);
        long ToInt64(IFormatProvider? provider);
        ulong ToUInt64(IFormatProvider? provider);
        float ToSingle(IFormatProvider? provider);
        double ToDouble(IFormatProvider? provider);
        decimal ToDecimal(IFormatProvider? provider);
        DateTime ToDateTime(IFormatProvider? provider);
        string ToString(IFormatProvider? provider);
        object ToType(Type conversionType, IFormatProvider? provider);
    }
}
