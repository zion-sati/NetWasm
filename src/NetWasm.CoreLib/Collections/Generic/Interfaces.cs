namespace System.Collections.Generic
{
    // Contract definitions adapted from dotnet/runtime System.Private.CoreLib
    // at commit 811225a482702af7ecc35d817966bc70b88a3a23.
    // Licensed under the MIT license; see the upstream repository for the
    // complete copyright notice.

    public interface IEqualityComparer<in T> where T : allows ref struct
    {
        bool Equals(T? left, T? right);
        int GetHashCode(T value);
    }

    public interface IComparer<in T> where T : allows ref struct
    {
        int Compare(T? left, T? right);
    }

    public interface IEnumerator<out T> : System.Collections.IEnumerator, System.IDisposable where T : allows ref struct
    {
        new T Current { get; }
    }

    public interface IEnumerable<out T> : System.Collections.IEnumerable
        where T : allows ref struct
    {
        new IEnumerator<T> GetEnumerator();
    }

    public interface IReadOnlyCollection<out T> : IEnumerable<T>, System.Collections.IEnumerable
    {
        int Count { get; }
    }

    public interface IReadOnlyList<out T> : IEnumerable<T>, IReadOnlyCollection<T>, System.Collections.IEnumerable
    {
        T this[int index] { get; }
    }

    public interface ICollection<T> : IEnumerable<T>, System.Collections.IEnumerable
    {
        int Count { get; }
        bool IsReadOnly { get; }
        void Add(T item);
        void Clear();
        bool Contains(T item);
        void CopyTo(T[] array, int arrayIndex);
        bool Remove(T item);
    }

    public interface IList<T> : ICollection<T>, IEnumerable<T>, System.Collections.IEnumerable
    {
        T this[int index] { get; set; }
        int IndexOf(T item);
        void Insert(int index, T item);
        void RemoveAt(int index);
    }

    public interface IReadOnlyDictionary<TKey, TValue> :
        IEnumerable<KeyValuePair<TKey, TValue>>,
        IReadOnlyCollection<KeyValuePair<TKey, TValue>>,
        System.Collections.IEnumerable
    {
        TValue this[TKey key] { get; }
        IEnumerable<TKey> Keys { get; }
        IEnumerable<TValue> Values { get; }
        bool ContainsKey(TKey key);
        bool TryGetValue(TKey key, out TValue value);
    }

    public interface IDictionary<TKey, TValue> :
        ICollection<KeyValuePair<TKey, TValue>>,
        IEnumerable<KeyValuePair<TKey, TValue>>,
        System.Collections.IEnumerable
    {
        TValue this[TKey key] { get; set; }
        ICollection<TKey> Keys { get; }
        ICollection<TValue> Values { get; }
        void Add(TKey key, TValue value);
        bool ContainsKey(TKey key);
        bool Remove(TKey key);
        bool TryGetValue(TKey key, out TValue value);
    }

    // Portions derived from dotnet/runtime System.Private.CoreLib's set contracts.
    // Licensed to the .NET Foundation under one or more agreements; the .NET
    // Foundation licenses this file to you under the MIT license.
    public interface ISet<T> : ICollection<T>, IEnumerable<T>, System.Collections.IEnumerable
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
        IEnumerable<T>, IReadOnlyCollection<T>, System.Collections.IEnumerable
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
