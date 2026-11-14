// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace System.ComponentModel;

[Serializable]
[System.Runtime.CompilerServices.TypeForwardedFrom("System, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089")]
public class InvalidEnumArgumentException : ArgumentException
{
    // Retain the standard constructor for derived-library compatibility.
    [System.Obsolete("This API supports obsolete formatter-based serialization. It should not be called or extended by application code.", DiagnosticId = "SYSLIB0051", UrlFormat = "https://aka.ms/dotnet-warnings/{0}")]
    protected InvalidEnumArgumentException(System.Runtime.Serialization.SerializationInfo info, System.Runtime.Serialization.StreamingContext context) =>
        throw new System.PlatformNotSupportedException("InvalidEnumArgumentException formatter deserialization is not available on this NetWasm platform.");

    public InvalidEnumArgumentException() :
        base("Value does not fall within the expected range.")
    {
    }

    public InvalidEnumArgumentException(string? message) : base(message)
    {
    }

    public InvalidEnumArgumentException(string? message, Exception? innerException) :
        base(message, innerException)
    {
    }

    public InvalidEnumArgumentException(
        string? argumentName,
        int invalidValue,
        Type enumClass) :
        base(
            "The value of argument '" + argumentName + "' (" + invalidValue +
                ") is invalid for Enum type '" + enumClass?.Name + "'.",
            argumentName)
    {
        ArgumentNullException.ThrowIfNull(enumClass);
    }
}
