// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace System
{
    // The formatter-based serialization contract is intentionally outside the
    // portable CoreLib profile. The singleton and IConvertible behavior remain
    // useful for managed database-null checks without that legacy dependency.
    [Serializable]
    public sealed class DBNull : IConvertible
    {
        private DBNull()
        {
        }

        public static readonly DBNull Value = new DBNull();

        public override string ToString()
        {
            return string.Empty;
        }

        public string ToString(IFormatProvider? provider)
        {
            return string.Empty;
        }

        public TypeCode GetTypeCode()
        {
            return TypeCode.DBNull;
        }

        bool IConvertible.ToBoolean(IFormatProvider? provider)
        {
            throw new InvalidCastException();
        }

        char IConvertible.ToChar(IFormatProvider? provider)
        {
            throw new InvalidCastException();
        }

        sbyte IConvertible.ToSByte(IFormatProvider? provider)
        {
            throw new InvalidCastException();
        }

        byte IConvertible.ToByte(IFormatProvider? provider)
        {
            throw new InvalidCastException();
        }

        short IConvertible.ToInt16(IFormatProvider? provider)
        {
            throw new InvalidCastException();
        }

        ushort IConvertible.ToUInt16(IFormatProvider? provider)
        {
            throw new InvalidCastException();
        }

        int IConvertible.ToInt32(IFormatProvider? provider)
        {
            throw new InvalidCastException();
        }

        uint IConvertible.ToUInt32(IFormatProvider? provider)
        {
            throw new InvalidCastException();
        }

        long IConvertible.ToInt64(IFormatProvider? provider)
        {
            throw new InvalidCastException();
        }

        ulong IConvertible.ToUInt64(IFormatProvider? provider)
        {
            throw new InvalidCastException();
        }

        float IConvertible.ToSingle(IFormatProvider? provider)
        {
            throw new InvalidCastException();
        }

        double IConvertible.ToDouble(IFormatProvider? provider)
        {
            throw new InvalidCastException();
        }

        decimal IConvertible.ToDecimal(IFormatProvider? provider)
        {
            throw new InvalidCastException();
        }

        DateTime IConvertible.ToDateTime(IFormatProvider? provider)
        {
            throw new InvalidCastException();
        }

        object IConvertible.ToType(Type type, IFormatProvider? provider)
        {
            ArgumentNullException.ThrowIfNull(type);

            if (object.ReferenceEquals(type, typeof(DBNull)) ||
                object.ReferenceEquals(type, typeof(object)))
            {
                return this;
            }

            if (object.ReferenceEquals(type, typeof(string)))
            {
                return string.Empty;
            }

            throw new InvalidCastException();
        }
    }
}
