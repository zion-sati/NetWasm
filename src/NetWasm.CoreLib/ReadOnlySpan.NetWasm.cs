// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// NetWasm's ReadOnlySpan<T> before the upstream port declared Index and Range indexers, which
// upstream does not have. Released binaries, as the NetWasm.Libraries packages the
// compiler's acceptance tests run, call their accessors, so the accessors stay as internal
// methods of the same names and signatures. C# binds no element access to a method, so
// `span[^1]` and `span[a..b]` lower to the int indexer and Slice, as they do on .NET, and a
// call compiled against the old indexers still links. Delete this file, and `partial` on
// ReadOnlySpan<T>, once eng/compiler-qualification.json pins Libraries built against a CoreLib
// without the indexers, 0.10.8 or later.

using System.Runtime.CompilerServices;

namespace System
{
    public readonly ref partial struct ReadOnlySpan<T>
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal ref readonly T get_Item(Index index) => ref this[index.GetOffset(_length)];

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal ReadOnlySpan<T> get_Item(Range range)
        {
            (int start, int length) = range.GetOffsetAndLength(_length);
            return Slice(start, length);
        }
    }
}
