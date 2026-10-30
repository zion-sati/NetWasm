// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Adapted from dotnet/runtime System.Collections.Immutable at commit
// 474ed963699fc29809015392738a15231aaae2de.

using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace System.Collections
{
    internal static class ThrowHelper
    {
        /// <summary>Throws an exception for a key not being found in the dictionary.</summary>
        [DoesNotReturn]
        internal static void ThrowKeyNotFound<TKey>(TKey key) =>
            throw new KeyNotFoundException(SR.Format(SR.Arg_KeyNotFoundWithKey, key));

        /// <summary>Throws an exception for trying to insert a duplicate key into the dictionary.</summary>
        [DoesNotReturn]
        internal static void ThrowDuplicateKey<TKey>(TKey key) =>
            throw new ArgumentException(SR.Format(SR.Argument_AddingDuplicate, key), nameof(key));

        /// <summary>Throws an exception when erroneous concurrent use of a collection is detected.</summary>
        [DoesNotReturn]
        internal static void ThrowConcurrentOperation() =>
            throw new InvalidOperationException(SR.InvalidOperation_ConcurrentOperationsNotSupported);

        /// <summary>Throws an exception for an index being out of range.</summary>
        [DoesNotReturn]
        internal static void ThrowIndexArgumentOutOfRange() =>
            throw new ArgumentOutOfRangeException("index");

        [DoesNotReturn]
        internal static void ThrowIfDestinationTooSmall() =>
            throw new ArgumentException(SR.CapacityMustBeGreaterThanOrEqualToCount, "destination");

        [DoesNotReturn]
        internal static void ThrowArgumentNullException(string? paramName) =>
            throw new ArgumentNullException(paramName);

        [DoesNotReturn]
        internal static void ThrowKeyNotFoundException() =>
            throw new KeyNotFoundException();

        [DoesNotReturn]
        internal static void ThrowKeyNotFoundException<TKey>(TKey key) =>
            throw new KeyNotFoundException(SR.Format(SR.Arg_KeyNotFoundWithKey, key));

        [DoesNotReturn]
        internal static void ThrowInvalidOperationException() =>
            throw new InvalidOperationException();

        [DoesNotReturn]
        internal static void ThrowInvalidOperationException(ExceptionResource resource) =>
            throw new InvalidOperationException(GetResourceString(resource));

        [DoesNotReturn]
        internal static void ThrowInvalidOperationException(
            ExceptionResource resource,
            Exception exception) =>
            throw new InvalidOperationException(GetResourceString(resource), exception);

        [DoesNotReturn]
        internal static void ThrowIncompatibleComparer() =>
            throw new InvalidOperationException(SR.InvalidOperation_IncompatibleComparer);

        [DoesNotReturn]
        internal static void ThrowArgumentNullException(ExceptionArgument argument) =>
            throw new ArgumentNullException(GetArgumentName(argument));

        [DoesNotReturn]
        internal static void ThrowArgumentOutOfRangeException(ExceptionArgument argument) =>
            throw new ArgumentOutOfRangeException(GetArgumentName(argument));

        [DoesNotReturn]
        internal static void ThrowArgumentOutOfRangeException(
            ExceptionArgument argument,
            ExceptionResource resource) =>
            throw new ArgumentOutOfRangeException(
                GetArgumentName(argument),
                GetResourceString(resource));

        [DoesNotReturn]
        internal static void ThrowArgumentOutOfRangeException_NeedNonNegNum(string paramName) =>
            throw new ArgumentOutOfRangeException(paramName, SR.ArgumentOutOfRange_NeedNonNegNum);

        [DoesNotReturn]
        internal static void ThrowArgumentOutOfRange_IndexMustBeLessException() =>
            ThrowArgumentOutOfRangeException(
                ExceptionArgument.index,
                ExceptionResource.ArgumentOutOfRange_IndexMustBeLess);

        [DoesNotReturn]
        internal static void ThrowArgumentOutOfRange_IndexMustBeLessOrEqualException() =>
            ThrowArgumentOutOfRangeException(
                ExceptionArgument.index,
                ExceptionResource.ArgumentOutOfRange_IndexMustBeLessOrEqual);

        [DoesNotReturn]
        internal static void ThrowStartIndexArgumentOutOfRange_ArgumentOutOfRange_IndexMustBeLess() =>
            ThrowArgumentOutOfRangeException(
                ExceptionArgument.startIndex,
                ExceptionResource.ArgumentOutOfRange_IndexMustBeLess);

        [DoesNotReturn]
        internal static void ThrowStartIndexArgumentOutOfRange_ArgumentOutOfRange_IndexMustBeLessOrEqual() =>
            ThrowArgumentOutOfRangeException(
                ExceptionArgument.startIndex,
                ExceptionResource.ArgumentOutOfRange_IndexMustBeLessOrEqual);

        [DoesNotReturn]
        internal static void ThrowCountArgumentOutOfRange_ArgumentOutOfRange_Count() =>
            ThrowArgumentOutOfRangeException(
                ExceptionArgument.count,
                ExceptionResource.ArgumentOutOfRange_Count);

        [DoesNotReturn]
        internal static void ThrowArgumentException(ExceptionResource resource) =>
            throw new ArgumentException(GetResourceString(resource));

        [DoesNotReturn]
        internal static void ThrowArgumentException(
            ExceptionResource resource,
            ExceptionArgument argument) =>
            throw new ArgumentException(GetResourceString(resource), GetArgumentName(argument));

        [DoesNotReturn]
        internal static void ThrowArgumentException_BadComparer(object? comparer) =>
            throw new ArgumentException(SR.Format(SR.Arg_BogusIComparer, comparer));

        [DoesNotReturn]
        internal static void ThrowArgumentException_Argument_IncompatibleArrayType() =>
            throw new ArgumentException(SR.Argument_IncompatibleArrayType);

        [DoesNotReturn]
        internal static void ThrowIndexArgumentOutOfRange_NeedNonNegNumException() =>
            throw new ArgumentOutOfRangeException("index", SR.ArgumentOutOfRange_NeedNonNegNum);

        [DoesNotReturn]
        internal static void ThrowAddingDuplicateWithKeyArgumentException<TKey>(TKey key) =>
            throw new ArgumentException("An item with the same key has already been added.");

        [DoesNotReturn]
        internal static void ThrowInvalidOperationException_ConcurrentOperationsNotSupported() =>
            throw new InvalidOperationException("Concurrent operations are not supported.");

        [DoesNotReturn]
        internal static void ThrowInvalidOperationException_InvalidOperation_EnumFailedVersion() =>
            throw new InvalidOperationException("Collection was modified; enumeration operation may not execute.");

        /// <summary>Throws an exception for a version check failing during enumeration.</summary>
        [DoesNotReturn]
        internal static void ThrowVersionCheckFailed() =>
            throw new InvalidOperationException(SR.InvalidOperation_EnumFailedVersion);

        [DoesNotReturn]
        internal static void ThrowInvalidOperationException_InvalidOperation_EnumOpCantHappen() =>
            throw new InvalidOperationException("Enumeration has either not started or has already finished.");

        [DoesNotReturn]
        internal static void ThrowNotSupportedException(ExceptionResource resource) =>
            throw new NotSupportedException(GetResourceString(resource));

        [DoesNotReturn]
        internal static void ThrowWrongKeyTypeArgumentException<TKey>(TKey key, Type targetType) =>
            throw new ArgumentException("The key was of an incompatible type.", "key");

        [DoesNotReturn]
        internal static void ThrowWrongValueTypeArgumentException<TValue>(TValue value, Type targetType) =>
            throw new ArgumentException("The value was of an incompatible type.", "value");

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static void IfNullAndNullsAreIllegalThenThrow<T>(
            object? value,
            ExceptionArgument argument)
        {
            if (!(default(T) == null) && value == null)
            {
                ThrowArgumentNullException(argument);
            }
        }

        private static string GetArgumentName(ExceptionArgument argument) => argument switch
        {
            ExceptionArgument.array => "array",
            ExceptionArgument.arrayIndex => "arrayIndex",
            ExceptionArgument.capacity => "capacity",
            ExceptionArgument.collection => "collection",
            ExceptionArgument.action => "action",
            ExceptionArgument.comparison => "comparison",
            ExceptionArgument.converter => "converter",
            ExceptionArgument.count => "count",
            ExceptionArgument.dictionary => "dictionary",
            ExceptionArgument.index => "index",
            ExceptionArgument.item => "item",
            ExceptionArgument.key => "key",
            ExceptionArgument.list => "list",
            ExceptionArgument.match => "match",
            ExceptionArgument.other => "other",
            ExceptionArgument.startIndex => "startIndex",
            ExceptionArgument.value => "value",
            _ => null!,
        };

        private static string GetResourceString(ExceptionResource resource) => resource switch
        {
            ExceptionResource.Arg_ArrayPlusOffTooSmall => SR.Arg_ArrayPlusOffTooSmall,
            ExceptionResource.Arg_NonZeroLowerBound => SR.Arg_NonZeroLowerBound,
            ExceptionResource.Arg_RankMultiDimNotSupported => SR.Arg_RankMultiDimNotSupported,
            ExceptionResource.Argument_InvalidOffLen => SR.Argument_InvalidOffLen,
            ExceptionResource.ArgumentOutOfRange_BiggerThanCollection => SR.ArgumentOutOfRange_BiggerThanCollection,
            ExceptionResource.ArgumentOutOfRange_Count => SR.ArgumentOutOfRange_Count,
            ExceptionResource.ArgumentOutOfRange_IndexMustBeLess => SR.ArgumentOutOfRange_IndexMustBeLess,
            ExceptionResource.ArgumentOutOfRange_IndexMustBeLessOrEqual => SR.ArgumentOutOfRange_IndexMustBeLessOrEqual,
            ExceptionResource.ArgumentOutOfRange_ListInsert => SR.ArgumentOutOfRange_ListInsert,
            ExceptionResource.ArgumentOutOfRange_NeedNonNegNum => SR.ArgumentOutOfRange_NeedNonNegNum,
            ExceptionResource.ArgumentOutOfRange_SmallCapacity => SR.ArgumentOutOfRange_SmallCapacity,
            ExceptionResource.InvalidOperation_IncompatibleComparer => SR.InvalidOperation_IncompatibleComparer,
            ExceptionResource.InvalidOperation_IComparerFailed => SR.InvalidOperation_IComparerFailed,
            ExceptionResource.NotSupported_ReadOnlyCollection => SR.NotSupported_ReadOnlyCollection,
            ExceptionResource.NotSupported_KeyCollectionSet => "Mutating a dictionary's key collection is not supported.",
            ExceptionResource.NotSupported_ValueCollectionSet => "Mutating a dictionary's value collection is not supported.",
            _ => "The requested operation is not supported.",
        };
    }

    internal enum ExceptionArgument
    {
        array,
        arrayIndex,
        action,
        capacity,
        comparison,
        collection,
        converter,
        count,
        dictionary,
        index,
        item,
        key,
        list,
        match,
        other,
        startIndex,
        value,
    }

    internal enum ExceptionResource
    {
        Arg_ArrayPlusOffTooSmall,
        Arg_NonZeroLowerBound,
        Arg_RankMultiDimNotSupported,
        Argument_InvalidOffLen,
        ArgumentOutOfRange_BiggerThanCollection,
        ArgumentOutOfRange_Count,
        ArgumentOutOfRange_IndexMustBeLess,
        ArgumentOutOfRange_IndexMustBeLessOrEqual,
        ArgumentOutOfRange_ListInsert,
        ArgumentOutOfRange_NeedNonNegNum,
        ArgumentOutOfRange_SmallCapacity,
        InvalidOperation_IncompatibleComparer,
        InvalidOperation_IComparerFailed,
        NotSupported_ReadOnlyCollection,
        NotSupported_KeyCollectionSet,
        NotSupported_ValueCollectionSet,
    }
}
