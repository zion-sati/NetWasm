// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Adapted from dotnet/runtime System.Collections.Immutable at commit
// 811225a482702af7ecc35d817966bc70b88a3a23.

using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace System.Collections.Immutable;

/// <summary>An immutable stack.</summary>
/// <typeparam name="T">The element type.</typeparam>
[CollectionBuilder(typeof(ImmutableStack), nameof(ImmutableStack.Create))]
public interface IImmutableStack<T> : IEnumerable<T>
{
    bool IsEmpty { get; }

    IImmutableStack<T> Clear();

    IImmutableStack<T> Push(T value);

    IImmutableStack<T> Pop();

    T Peek();
}
