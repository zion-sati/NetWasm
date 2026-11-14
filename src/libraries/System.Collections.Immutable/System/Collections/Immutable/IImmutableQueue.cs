// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Adapted from dotnet/runtime System.Collections.Immutable at commit
// deae2e32bf0e018e09d64597b7c59b62b73bb3ce.

using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace System.Collections.Immutable;

/// <summary>An immutable queue.</summary>
/// <typeparam name="T">The element type.</typeparam>
[CollectionBuilder(typeof(ImmutableQueue), nameof(ImmutableQueue.Create))]
public interface IImmutableQueue<T> : IEnumerable<T>
{
    bool IsEmpty { get; }

    IImmutableQueue<T> Clear();

    T Peek();

    IImmutableQueue<T> Enqueue(T value);

    IImmutableQueue<T> Dequeue();
}
