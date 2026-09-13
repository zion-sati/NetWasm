// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
//
// Ported from dotnet/runtime System.Collections.Immutable Validation/Requires.cs
// at commit 811225a482702af7ecc35d817966bc70b88a3a23. The reflection-dependent
// object-name lookup is intentionally reduced to Type.ToString(), because the
// NetWasm CoreLib profile does not deploy reflection metadata.

using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace System.Collections.Immutable
{
    /// <summary>Common runtime checks used by immutable collections.</summary>
    internal static class Requires
    {
        [DebuggerStepThrough]
        public static void NotNull<T>([NotNull] T value, string? parameterName)
            where T : class
        {
            if (value == null)
            {
                FailArgumentNullException(parameterName);
            }
        }

        [DebuggerStepThrough]
        public static T NotNullPassthrough<T>([NotNull] T value, string? parameterName)
            where T : class
        {
            NotNull(value, parameterName);
            return value;
        }

        [DebuggerStepThrough]
        public static void NotNullAllowStructs<T>([NotNull] T value, string? parameterName)
        {
            if (null == value)
            {
                FailArgumentNullException(parameterName);
            }
        }

        [DoesNotReturn]
        [DebuggerStepThrough]
        public static void FailArgumentNullException(string? parameterName)
        {
            throw new ArgumentNullException(parameterName);
        }

        [DebuggerStepThrough]
        public static void Range(
            [DoesNotReturnIf(false)] bool condition,
            string? parameterName,
            string? message = null)
        {
            if (!condition)
            {
                FailRange(parameterName, message);
            }
        }

        [DoesNotReturn]
        [DebuggerStepThrough]
        public static void FailRange(string? parameterName, string? message = null)
        {
            if (string.IsNullOrEmpty(message))
            {
                throw new ArgumentOutOfRangeException(parameterName);
            }

            throw new ArgumentOutOfRangeException(parameterName, message);
        }

        [DebuggerStepThrough]
        public static void Argument(
            [DoesNotReturnIf(false)] bool condition,
            string? parameterName,
            string? message)
        {
            if (!condition)
            {
                throw new ArgumentException(message, parameterName);
            }
        }

        [DebuggerStepThrough]
        public static void Argument([DoesNotReturnIf(false)] bool condition)
        {
            if (!condition)
            {
                throw new ArgumentException();
            }
        }

        [DoesNotReturn]
        [DebuggerStepThrough]
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static void FailObjectDisposed<TDisposed>(TDisposed disposed)
        {
            // Type.FullName is reflection-backed and unsupported by this profile.
            // Type.ToString() remains deterministic and gives the exception an
            // object identity without requiring metadata deployment.
            throw new ObjectDisposedException(disposed!.GetType().ToString());
        }
    }
}
