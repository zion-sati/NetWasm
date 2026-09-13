// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace System.Diagnostics;

// NetWasm has no attached managed debugger. Assertions therefore fail loudly
// and deterministically when their DEBUG-conditional call sites are retained.
public static class Debug
{
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
