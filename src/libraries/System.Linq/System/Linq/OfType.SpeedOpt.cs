// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace System.Linq
{
    public static partial class Enumerable
    {
        private sealed partial class OfTypeIterator<TResult>
        {
            public override int GetCount(bool onlyIfCheap)
            {
                if (onlyIfCheap)
                {
                    return -1;
                }

                int count = 0;
                foreach (object? item in _source)
                {
                    if (item is TResult)
                    {
                        checked { count++; }
                    }
                }

                return count;
            }

            public override TResult[] ToArray()
            {
                SegmentedArrayBuilder<TResult>.ScratchBuffer scratch = default;
                SegmentedArrayBuilder<TResult> builder = new(scratch);

                foreach (object? item in _source)
                {
                    if (item is TResult castItem)
                    {
                        builder.Add(castItem);
                    }
                }

                TResult[] result = builder.ToArray();
                builder.Dispose();

                return result;
            }

            public override List<TResult> ToList()
            {
                SegmentedArrayBuilder<TResult>.ScratchBuffer scratch = default;
                SegmentedArrayBuilder<TResult> builder = new(scratch);

                foreach (object? item in _source)
                {
                    if (item is TResult castItem)
                    {
                        builder.Add(castItem);
                    }
                }

                List<TResult> result = builder.ToList();
                builder.Dispose();

                return result;
            }

            public override TResult? TryGetFirst(out bool found)
            {
                foreach (object? item in _source)
                {
                    if (item is TResult castItem)
                    {
                        found = true;
                        return castItem;
                    }
                }

                found = false;
                return default;
            }

            public override TResult? TryGetLast(out bool found)
            {
                IEnumerator e = _source.GetEnumerator();
                try
                {
                    if (e.MoveNext())
                    {
                        do
                        {
                            if (e.Current is TResult last)
                            {
                                found = true;

                                while (e.MoveNext())
                                {
                                    if (e.Current is TResult castCurrent)
                                    {
                                        last = castCurrent;
                                    }
                                }

                                return last;
                            }
                        }
                        while (e.MoveNext());
                    }
                }
                finally
                {
                    (e as IDisposable)?.Dispose();
                }

                found = false;
                return default;
            }

            public override TResult? TryGetElementAt(int index, out bool found)
            {
                if (index >= 0)
                {
                    foreach (object? item in _source)
                    {
                        if (item is TResult castItem)
                        {
                            if (index == 0)
                            {
                                found = true;
                                return castItem;
                            }

                            index--;
                        }
                    }
                }

                found = false;
                return default;
            }

            public override IEnumerable<TResult2> Select<TResult2>(Func<TResult, TResult2> selector)
                => base.Select(selector);

            public override bool Contains(TResult value)
            {
                // It is tempting to delegate here to IList.Contains if _source is IList (especially
                // if TResult is not a value type, as it would be boxed as an argument to Contains).
                // And while that will be correct in most cases, if any of the items in the source
                // compares equally with value but is not actually of type TResult, doing so would
                // skip the type check implied by OfType<TResult>(). Further, if IList is a multidim
                // array, its IList.Contains will fail for non-1 ranks. We thus just iterate directly.

                foreach (object? item in _source)
                {
                    if (item is TResult castItem && EqualityComparer<TResult>.Default.Equals(castItem, value))
                    {
                        return true;
                    }
                }

                return false;
            }
        }
    }
}
