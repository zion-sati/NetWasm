// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Adapted from dotnet/runtime at commit
// 474ed963699fc29809015392738a15231aaae2de.

using System.Collections.Generic;

namespace System.Collections.ObjectModel
{
    internal static class CollectionHelpers
    {
        internal static void ValidateCopyToArguments(int sourceCount, Array array, int index)
        {
            ArgumentNullException.ThrowIfNull(array);

            if (array.Rank != 1)
            {
                throw new ArgumentException(SR.Arg_RankMultiDimNotSupported, nameof(array));
            }

            if (array.GetLowerBound(0) != 0)
            {
                throw new ArgumentException(SR.Arg_NonZeroLowerBound, nameof(array));
            }

            ArgumentOutOfRangeException.ThrowIfNegative(index);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(index, array.Length);

            if (array.Length - index < sourceCount)
            {
                throw new ArgumentException(SR.Arg_ArrayPlusOffTooSmall);
            }
        }

        internal static void CopyTo<T>(ICollection<T> collection, Array array, int index)
        {
            ValidateCopyToArguments(collection.Count, array, index);

            if (collection is ICollection nonGenericCollection)
            {
                nonGenericCollection.CopyTo(array, index);
            }
            else if (array is T[] items)
            {
                collection.CopyTo(items, index);
            }
            else
            {
                if (array is not object?[] objects)
                {
                    throw new ArgumentException(SR.Argument_IncompatibleArrayType, nameof(array));
                }

                try
                {
                    foreach (T item in collection)
                    {
                        objects[index++] = item;
                    }
                }
                catch (ArrayTypeMismatchException)
                {
                    throw new ArgumentException(SR.Argument_IncompatibleArrayType, nameof(array));
                }
            }
        }
    }
}
