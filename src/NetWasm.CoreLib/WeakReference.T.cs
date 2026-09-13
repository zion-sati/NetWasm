// Portions derived from dotnet/runtime System.Private.CoreLib at commit
// 811225a482702af7ecc35d817966bc70b88a3a23.
// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace System;

// This class is sealed to mitigate security issues caused by Object::MemberwiseClone.
public sealed class WeakReference<T>
    where T : class?
{
    // Keep the handle in the wrapper so the collector can apply the requested
    // short-weak or track-resurrection lifetime independently of the target.
    private int _handle;

    // Creates a new WeakReference that keeps track of target.
    // Assumes a Short Weak Reference (ie TrackResurrection is false.)
    public WeakReference(T target)
        : this(target, false)
    {
    }

    // Creates a new WeakReference that keeps track of target.
    public WeakReference(T target, bool trackResurrection)
    {
        Create(target, trackResurrection);
    }

    //
    // We are exposing TryGetTarget instead of a simple getter to avoid a common problem where people write incorrect code like:
    //
    //      WeakReference ref = ...;
    //      if (ref.Target != null)
    //          DoSomething(ref.Target)
    //
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryGetTarget([MaybeNullWhen(false), NotNullWhen(true)] out T target)
    {
        T? value = Target;
        target = value!;
        return value != null;
    }

    // Creates a new WeakReference that keeps track of target.
    private void Create(T target, bool trackResurrection)
    {
        _handle = WeakReferenceRuntime.Create(target, trackResurrection);
    }

    public void SetTarget(T target)
    {
        if (_handle == 0)
        {
            throw new InvalidOperationException("The weak-reference handle is not initialized.");
        }

        WeakReferenceRuntime.Set(_handle, target);
    }

    private T? Target
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => WeakReferenceRuntime.Get(_handle) as T;
    }

    ~WeakReference()
    {
        WeakReferenceRuntime.Release(_handle);
    }
}
