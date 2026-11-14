namespace System.Collections
{
    // Contract definitions adapted from dotnet/runtime System.Private.CoreLib
    // at commit deae2e32bf0e018e09d64597b7c59b62b73bb3ce.
    // Licensed under the MIT license; see the upstream repository for the
    // complete copyright notice.

    public interface IEnumerator
    {
#nullable disable
        object Current { get; }
#nullable restore
        bool MoveNext();
        void Reset();
    }

    [System.Runtime.InteropServices.Guid("496B0ABE-CDEE-11d3-88E8-00902754C43A")]
    public interface IEnumerable
    {
        IEnumerator GetEnumerator();
    }

    public interface ICollection : IEnumerable
    {
        void CopyTo(Array array, int index);
        int Count { get; }
        object SyncRoot { get; }
        bool IsSynchronized { get; }
    }

    public interface IComparer
    {
        int Compare(object? x, object? y);
    }

    public interface IEqualityComparer
    {
        bool Equals(object? x, object? y);
        int GetHashCode(object obj);
    }

    public interface IList : ICollection, IEnumerable
    {
        object? this[int index] { get; set; }
        int Add(object? value);
        bool Contains(object? value);
        void Clear();
        bool IsReadOnly { get; }
        bool IsFixedSize { get; }
        int IndexOf(object? value);
        void Insert(int index, object? value);
        void Remove(object? value);
        void RemoveAt(int index);
    }

    public interface IDictionary : ICollection, IEnumerable
    {
        object? this[object key] { get; set; }
        ICollection Keys { get; }
        ICollection Values { get; }
        bool Contains(object key);
        void Add(object key, object? value);
        void Clear();
        bool IsReadOnly { get; }
        bool IsFixedSize { get; }
        void Remove(object key);
        new IDictionaryEnumerator GetEnumerator();
    }

    // Portions derived from dotnet/runtime System.Private.CoreLib collection contracts.
    // Licensed to the .NET Foundation under one or more agreements.
    // The .NET Foundation licenses this file to you under the MIT license.
    public interface IDictionaryEnumerator : IEnumerator
    {
        DictionaryEntry Entry { get; }
        object Key { get; }
        object? Value { get; }
    }

    // Portions derived from dotnet/runtime System.Private.CoreLib collection contracts.
    // Licensed to the .NET Foundation under one or more agreements.
    // The .NET Foundation licenses this file to you under the MIT license.
    [Obsolete("IHashCodeProvider has been deprecated. Use IEqualityComparer instead.")]
    [System.Runtime.CompilerServices.TypeForwardedFrom("mscorlib, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089")]
    public interface IHashCodeProvider
    {
        int GetHashCode(object obj);
    }

    public interface IStructuralComparable
    {
        int CompareTo(object? other, IComparer comparer);
    }

    public interface IStructuralEquatable
    {
        bool Equals(object? other, IEqualityComparer comparer);
        int GetHashCode(IEqualityComparer comparer);
    }
}
