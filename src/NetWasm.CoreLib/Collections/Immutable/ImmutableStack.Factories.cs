// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Adapted from dotnet/runtime System.Collections.Immutable at commit
// 811225a482702af7ecc35d817966bc70b88a3a23.

using System.Collections.Generic;

namespace System.Collections.Immutable;

/// <summary>Factory methods for immutable stacks.</summary>
public static class ImmutableStack
{
    public static ImmutableStack<T> Create<T>() => ImmutableStack<T>.Empty;

    public static ImmutableStack<T> Create<T>(T item) =>
        ImmutableStack<T>.Empty.Push(item);

    public static ImmutableStack<T> CreateRange<T>(IEnumerable<T> items)
    {
        Requires.NotNull(items, nameof(items));

        var stack = ImmutableStack<T>.Empty;
        foreach (var item in items)
        {
            stack = stack.Push(item);
        }

        return stack;
    }

    public static ImmutableStack<T> Create<T>(params T[] items)
    {
        Requires.NotNull(items, nameof(items));
        return Create((ReadOnlySpan<T>)items);
    }

    public static ImmutableStack<T> Create<T>(params ReadOnlySpan<T> items)
    {
        var stack = ImmutableStack<T>.Empty;
        foreach (var item in items)
        {
            stack = stack.Push(item);
        }

        return stack;
    }

    public static IImmutableStack<T> Pop<T>(this IImmutableStack<T> stack, out T value)
    {
        Requires.NotNull(stack, nameof(stack));
        value = stack.Peek();
        return stack.Pop();
    }
}
