// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace System.Diagnostics;

// NetWasm has no attached managed debugger. Assertions therefore fail loudly
// and deterministically when their DEBUG-conditional call sites are retained.
public static class Debug
{
    // Without an attached debugger, retained debug output uses stderr. Delegate
    // directly to the writer: chaining Conditional methods would erase these
    // calls when CoreLib itself is built in Release, even for DEBUG consumers.
    [Conditional("DEBUG")]
    public static void WriteLine(string? message) => Console.Error.WriteLine(message);

    [Conditional("DEBUG")]
    public static void WriteLine(object? value) => Console.Error.WriteLine(value?.ToString());

    [Conditional("DEBUG")]
    public static void WriteLine(string? message, string? category) =>
        Console.Error.WriteLine(category is null ? message : category + ": " + message);

    [Conditional("DEBUG")]
    public static void WriteLine(object? value, string? category) =>
        Console.Error.WriteLine(category is null ? value?.ToString() : category + ": " + value?.ToString());

    [Conditional("DEBUG")]
    public static void WriteLine(
        [StringSyntax(StringSyntaxAttribute.CompositeFormat)] string format,
        params object?[] args) => Console.Error.WriteLine(string.Format(null, format, args));

    [Conditional("DEBUG")]
    [OverloadResolutionPriority(-1)]
    public static void Assert([DoesNotReturnIf(false)] bool condition)
    {
        if (!condition)
        {
            throw new InvalidOperationException();
        }
    }

    [Conditional("DEBUG")]
    public static void Assert(
        [DoesNotReturnIf(false)] bool condition,
        [CallerArgumentExpression(nameof(condition))] string? message = null)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    [Conditional("DEBUG")]
    public static void Assert(
        [DoesNotReturnIf(false)] bool condition,
        string? message,
        string? detailMessage)
    {
        if (!condition)
        {
            throw new InvalidOperationException(CreateFailureMessage(message, detailMessage));
        }
    }

    [Conditional("DEBUG")]
    [DoesNotReturn]
    public static void Fail(string? message) =>
        throw new InvalidOperationException(message);

    [Conditional("DEBUG")]
    [DoesNotReturn]
    public static void Fail(string? message, string? detailMessage) =>
        throw new InvalidOperationException(CreateFailureMessage(message, detailMessage));

    private static string? CreateFailureMessage(string? message, string? detailMessage)
    {
        if (string.IsNullOrEmpty(message))
        {
            return detailMessage;
        }

        return string.IsNullOrEmpty(detailMessage)
            ? message
            : message + " " + detailMessage;
    }
}
