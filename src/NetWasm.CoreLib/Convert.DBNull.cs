// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Diagnostics.CodeAnalysis;

namespace System
{
    public static partial class Convert
    {
        // Convert.DBNull is distinct from a null object reference, which is
        // represented by TypeCode.Empty.
        public static readonly object DBNull = System.DBNull.Value;

        public static TypeCode GetTypeCode(object? value)
        {
            if (value is null)
            {
                return TypeCode.Empty;
            }

            return value is IConvertible convertible
                ? convertible.GetTypeCode()
                : TypeCode.Object;
        }

        public static bool IsDBNull([NotNullWhen(true)] object? value)
        {
            if (value == System.DBNull.Value)
            {
                return true;
            }

            return value is IConvertible convertible &&
                convertible.GetTypeCode() == TypeCode.DBNull;
        }
    }
}
