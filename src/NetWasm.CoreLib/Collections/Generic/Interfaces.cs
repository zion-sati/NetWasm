using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace System.Collections.Generic
{
    // Contract definitions adapted from dotnet/runtime System.Private.CoreLib
    // at commit 474ed963699fc29809015392738a15231aaae2de.
    // Licensed under the MIT license; see the upstream repository for the
    // complete copyright notice.

    public interface IEqualityComparer<in T> where T : allows ref struct
    {
        bool Equals(T? x, T? y);
        int GetHashCode([DisallowNull] T obj);
    }

    public interface IComparer<in T> where T : allows ref struct
    {
        int Compare(T? x, T? y);
    }

    [Intrinsic]
    public interface IEnumerator<out T> : System.Collections.IEnumerator, System.IDisposable where T : allows ref struct
    {
        new T Current { get; }
    }

    public interface IEnumerable<out T> : System.Collections.IEnumerable
        where T : allows ref struct
    {
        [Intrinsic]
        new IEnumerator<T> GetEnumerator();
    }

    public interface IReadOnlyCollection<out T> : IEnumerable<T>
    {
        int Count { get; }
    }

    public interface IReadOnlyList<out T> : IReadOnlyCollection<T>
    {
        T this[int index] { get; }
    }

    public interface ICollection<T> : IEnumerable<T>
    {
        int Count { get; }
        bool IsReadOnly { get; }
        void Add(T item);
        void Clear();
        bool Contains(T item);
        void CopyTo(T[] array, int arrayIndex);
        bool Remove(T item);
    }

    public interface IList<T> : ICollection<T>
    {
        T this[int index] { get; set; }
        int IndexOf(T item);
        void Insert(int index, T item);
        void RemoveAt(int index);
    }

    public interface IReadOnlyDictionary<TKey, TValue> :
        IEnumerable<KeyValuePair<TKey, TValue>>,
        IReadOnlyCollection<KeyValuePair<TKey, TValue>>
    {
        TValue this[TKey key] { get; }
        IEnumerable<TKey> Keys { get; }
        IEnumerable<TValue> Values { get; }
        bool ContainsKey(TKey key);
        bool TryGetValue(TKey key, [MaybeNullWhen(false)] out TValue value);
    }

    public interface IDictionary<TKey, TValue> :
        ICollection<KeyValuePair<TKey, TValue>>
    {
        TValue this[TKey key] { get; set; }
        ICollection<TKey> Keys { get; }
        ICollection<TValue> Values { get; }
        void Add(TKey key, TValue value);
        bool ContainsKey(TKey key);
        bool Remove(TKey key);
        bool TryGetValue(TKey key, [MaybeNullWhen(false)] out TValue value);
    }

    // Portions derived from dotnet/runtime System.Private.CoreLib's set contracts.
    // Licensed to the .NET Foundation under one or more agreements; the .NET
    // Foundation licenses this file to you under the MIT license.
    public interface ISet<T> : ICollection<T>
    {
        new bool Add(T item);
        void UnionWith(IEnumerable<T> other);
        void IntersectWith(IEnumerable<T> other);
        void ExceptWith(IEnumerable<T> other);
        void SymmetricExceptWith(IEnumerable<T> other);
        bool IsSubsetOf(IEnumerable<T> other);
        bool IsSupersetOf(IEnumerable<T> other);
        bool IsProperSupersetOf(IEnumerable<T> other);
        bool IsProperSubsetOf(IEnumerable<T> other);
        bool Overlaps(IEnumerable<T> other);
        bool SetEquals(IEnumerable<T> other);
    }

    // Portions derived from dotnet/runtime System.Private.CoreLib's set contracts.
    public interface IReadOnlySet<T> :
        IReadOnlyCollection<T>
    {
        bool Contains(T item);
        bool IsProperSubsetOf(IEnumerable<T> other);
        bool IsProperSupersetOf(IEnumerable<T> other);
        bool IsSubsetOf(IEnumerable<T> other);
        bool IsSupersetOf(IEnumerable<T> other);
        bool Overlaps(IEnumerable<T> other);
        bool SetEquals(IEnumerable<T> other);
    }
}
