// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Runtime.CompilerServices;

namespace System.Threading;

/// <summary>Provides tools for avoiding stack overflows.</summary>
internal static class StackHelper
{
    public static bool TryEnsureSufficientExecutionStack()
    {
        try
        {
            RuntimeHelpers.EnsureSufficientExecutionStack();
            return true;
        }
        catch (InsufficientExecutionStackException)
        {
            return false;
        }
    }

    // NetWasm is single-reactor and has no worker stack onto which execution can be moved.
    // The runtime's current execution-stack probe succeeds, so these fallbacks remain direct.
    public static void CallOnEmptyStack<TArg1>(Action<TArg1> action, TArg1 arg1) =>
        action(arg1);

    public static void CallOnEmptyStack<TArg1, TArg2>(
        Action<TArg1, TArg2> action,
        TArg1 arg1,
        TArg2 arg2) => action(arg1, arg2);

    public static void CallOnEmptyStack<TArg1, TArg2, TArg3>(
        Action<TArg1, TArg2, TArg3> action,
        TArg1 arg1,
        TArg2 arg2,
        TArg3 arg3) => action(arg1, arg2, arg3);

    public static void CallOnEmptyStack<TArg1, TArg2, TArg3, TArg4>(
        Action<TArg1, TArg2, TArg3, TArg4> action,
        TArg1 arg1,
        TArg2 arg2,
        TArg3 arg3,
        TArg4 arg4) => action(arg1, arg2, arg3, arg4);

    public static void CallOnEmptyStack<TArg1, TArg2, TArg3, TArg4, TArg5>(
        Action<TArg1, TArg2, TArg3, TArg4, TArg5> action,
        TArg1 arg1,
        TArg2 arg2,
        TArg3 arg3,
        TArg4 arg4,
        TArg5 arg5) => action(arg1, arg2, arg3, arg4, arg5);

    public static void CallOnEmptyStack<TArg1, TArg2, TArg3, TArg4, TArg5, TArg6>(
        Action<TArg1, TArg2, TArg3, TArg4, TArg5, TArg6> action,
        TArg1 arg1,
        TArg2 arg2,
        TArg3 arg3,
        TArg4 arg4,
        TArg5 arg5,
        TArg6 arg6) => action(arg1, arg2, arg3, arg4, arg5, arg6);

    public static TResult CallOnEmptyStack<TResult>(Func<TResult> function) => function();

    public static TResult CallOnEmptyStack<TArg1, TResult>(
        Func<TArg1, TResult> function,
        TArg1 arg1) => function(arg1);

    public static TResult CallOnEmptyStack<TArg1, TArg2, TResult>(
        Func<TArg1, TArg2, TResult> function,
        TArg1 arg1,
        TArg2 arg2) => function(arg1, arg2);

    public static TResult CallOnEmptyStack<TArg1, TArg2, TArg3, TResult>(
        Func<TArg1, TArg2, TArg3, TResult> function,
        TArg1 arg1,
        TArg2 arg2,
        TArg3 arg3) => function(arg1, arg2, arg3);

    public static TResult CallOnEmptyStack<TArg1, TArg2, TArg3, TArg4, TResult>(
        Func<TArg1, TArg2, TArg3, TArg4, TResult> function,
        TArg1 arg1,
        TArg2 arg2,
        TArg3 arg3,
        TArg4 arg4) => function(arg1, arg2, arg3, arg4);
}
