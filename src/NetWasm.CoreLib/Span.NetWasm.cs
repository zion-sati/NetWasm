// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// NetWasm's Span<T> before the upstream port declared Index and Range indexers, which
// upstream does not have. C# binds `span[^1]` and `span[a..b]` to them over its Length and
// Slice pattern, so the released NetWasm.Libraries binaries the compiler's acceptance tests
// run call them. Delete this file, and `partial` on Span<T>, once
// eng/compiler-qualification.json pins Libraries compiled against a CoreLib without them.

namespace System
{
    public readonly ref partial struct Span<T>
    {
        public ref T this[Index index] => ref this[index.GetOffset(_length)];

        public Span<T> this[Range range]
        {
            get
            {
                (int start, int length) = range.GetOffsetAndLength(_length);
                return Slice(start, length);
            }
        }
    }
}
