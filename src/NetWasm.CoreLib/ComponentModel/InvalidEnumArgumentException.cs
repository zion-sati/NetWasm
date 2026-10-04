// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace System.ComponentModel;

public class InvalidEnumArgumentException : ArgumentException
{
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
                ") is invalid for Enum type '" + enumClass.Name + "'.",
            argumentName)
    {
    }
}
