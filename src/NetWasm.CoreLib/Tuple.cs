// Adapted from dotnet/runtime System.Private.CoreLib Tuple contracts.
// The upstream implementation is licensed under MIT.
// Copyright (c) .NET Foundation and Contributors.
namespace System
{
    internal static class TupleValue
    {
        internal static readonly Collections.IComparer DefaultComparer = new ObjectComparer();

        internal static bool Equal<T>(T left, T right) =>
            Collections.Generic.EqualityComparer<T>.Default.Equals(left, right);
        internal static int Hash<T>(T value) =>
            Collections.Generic.EqualityComparer<T>.Default.GetHashCode(value!);
        internal static string Text(object? value) => value?.ToString() ?? "";

        internal static int StructuralCompare(Collections.IComparer comparer, params object?[] values)
        {
            ArgumentNullException.ThrowIfNull(comparer);
            for (var index = 0; index < values.Length; index += 2)
            {
                var result = comparer.Compare(values[index], values[index + 1]);
                if (result != 0) return result;
            }
            return 0;
        }

        internal static bool StructuralEquals(Collections.IEqualityComparer comparer, params object?[] values)
        {
            ArgumentNullException.ThrowIfNull(comparer);
            for (var index = 0; index < values.Length; index += 2)
            {
                if (!comparer.Equals(values[index], values[index + 1])) return false;
            }
            return true;
        }

        internal static int StructuralHash(Collections.IEqualityComparer comparer, params object?[] values)
        {
            ArgumentNullException.ThrowIfNull(comparer);
            var hash = 0;
            for (var index = 0; index < values.Length; index += 2)
            {
                hash = hash * 31 + comparer.GetHashCode(values[index]!);
            }
            return hash;
        }

        private sealed class ObjectComparer : Collections.IComparer
        {
            public int Compare(object? x, object? y) => Collections.Generic.Comparer<object>.Default.Compare(x, y);
        }
    }

    public abstract class Tuple
    {
        public static Tuple<T1> Create<T1>(T1 item1) => new(item1);
        public static Tuple<T1, T2> Create<T1, T2>(T1 item1, T2 item2) => new(item1, item2);
        public static Tuple<T1, T2, T3> Create<T1, T2, T3>(T1 item1, T2 item2, T3 item3) => new(item1, item2, item3);
        public static Tuple<T1, T2, T3, T4> Create<T1, T2, T3, T4>(T1 item1, T2 item2, T3 item3, T4 item4) => new(item1, item2, item3, item4);
        public static Tuple<T1, T2, T3, T4, T5> Create<T1, T2, T3, T4, T5>(T1 item1, T2 item2, T3 item3, T4 item4, T5 item5) => new(item1, item2, item3, item4, item5);
        public static Tuple<T1, T2, T3, T4, T5, T6> Create<T1, T2, T3, T4, T5, T6>(T1 item1, T2 item2, T3 item3, T4 item4, T5 item5, T6 item6) => new(item1, item2, item3, item4, item5, item6);
        public static Tuple<T1, T2, T3, T4, T5, T6, T7> Create<T1, T2, T3, T4, T5, T6, T7>(T1 item1, T2 item2, T3 item3, T4 item4, T5 item5, T6 item6, T7 item7) => new(item1, item2, item3, item4, item5, item6, item7);
        public static Tuple<T1, T2, T3, T4, T5, T6, T7, Tuple<T8>> Create<T1, T2, T3, T4, T5, T6, T7, T8>(T1 item1, T2 item2, T3 item3, T4 item4, T5 item5, T6 item6, T7 item7, T8 item8) => new(item1, item2, item3, item4, item5, item6, item7, new Tuple<T8>(item8));
    }

    public sealed class Tuple<T1> : Collections.IStructuralComparable, Collections.IStructuralEquatable, IComparable, Runtime.CompilerServices.ITuple
    {
        public Tuple(T1 item1) => Item1 = item1;
        public T1 Item1 { get; }
        int Runtime.CompilerServices.ITuple.Length => 1;
        object? Runtime.CompilerServices.ITuple.this[int index] => index == 0 ? Item1 : throw new IndexOutOfRangeException();
        public bool Equals(Tuple<T1>? other) => other is not null && TupleValue.Equal(Item1, other.Item1);
        public override bool Equals(object? value) => value is Tuple<T1> other && Equals(other);
        public override int GetHashCode() => TupleValue.Hash(Item1);
        public override string ToString() => "(" + TupleValue.Text(Item1) + ")";
        int Collections.IStructuralComparable.CompareTo(object? other, Collections.IComparer comparer) =>
            other is null ? 1 : other is Tuple<T1> tuple ? TupleValue.StructuralCompare(comparer, Item1, tuple.Item1) : throw new ArgumentException();
        bool Collections.IStructuralEquatable.Equals(object? other, Collections.IEqualityComparer comparer) =>
            other is Tuple<T1> tuple && TupleValue.StructuralEquals(comparer, Item1, tuple.Item1);
        int Collections.IStructuralEquatable.GetHashCode(Collections.IEqualityComparer comparer) =>
            TupleValue.StructuralHash(comparer, Item1);
        int IComparable.CompareTo(object? other) =>
            other is null ? 1 : other is Tuple<T1> tuple ? TupleValue.StructuralCompare(TupleValue.DefaultComparer, Item1, tuple.Item1) : throw new ArgumentException();
    }

    public sealed class Tuple<T1, T2> : Collections.IStructuralComparable, Collections.IStructuralEquatable, IComparable, Runtime.CompilerServices.ITuple
    {
        public Tuple(T1 item1, T2 item2) { Item1 = item1; Item2 = item2; }
        public T1 Item1 { get; }
        public T2 Item2 { get; }
        int Runtime.CompilerServices.ITuple.Length => 2;
        object? Runtime.CompilerServices.ITuple.this[int index] => index switch { 0 => Item1, 1 => Item2, _ => throw new IndexOutOfRangeException() };
        public bool Equals(Tuple<T1, T2>? other) => other is not null && TupleValue.Equal(Item1, other.Item1) && TupleValue.Equal(Item2, other.Item2);
        public override bool Equals(object? value) => value is Tuple<T1, T2> other && Equals(other);
        public override int GetHashCode() => TupleValue.Hash(Item1) * 31 + TupleValue.Hash(Item2);
        public override string ToString() => "(" + TupleValue.Text(Item1) + ", " + TupleValue.Text(Item2) + ")";
        int Collections.IStructuralComparable.CompareTo(object? other, Collections.IComparer comparer) =>
            other is null ? 1 : other is Tuple<T1, T2> tuple ? TupleValue.StructuralCompare(comparer, Item1, tuple.Item1, Item2, tuple.Item2) : throw new ArgumentException();
        bool Collections.IStructuralEquatable.Equals(object? other, Collections.IEqualityComparer comparer) =>
            other is Tuple<T1, T2> tuple && TupleValue.StructuralEquals(comparer, Item1, tuple.Item1, Item2, tuple.Item2);
        int Collections.IStructuralEquatable.GetHashCode(Collections.IEqualityComparer comparer) =>
            TupleValue.StructuralHash(comparer, Item1, Item2);
        int IComparable.CompareTo(object? other) =>
            other is null ? 1 : other is Tuple<T1, T2> tuple ? TupleValue.StructuralCompare(TupleValue.DefaultComparer, Item1, tuple.Item1, Item2, tuple.Item2) : throw new ArgumentException();
    }

    public sealed class Tuple<T1, T2, T3> : Collections.IStructuralComparable, Collections.IStructuralEquatable, IComparable, Runtime.CompilerServices.ITuple
    {
        public Tuple(T1 item1, T2 item2, T3 item3) { Item1 = item1; Item2 = item2; Item3 = item3; }
        public T1 Item1 { get; }
        public T2 Item2 { get; }
        public T3 Item3 { get; }
        int Runtime.CompilerServices.ITuple.Length => 3;
        object? Runtime.CompilerServices.ITuple.this[int index] => index switch { 0 => Item1, 1 => Item2, 2 => Item3, _ => throw new IndexOutOfRangeException() };
        public bool Equals(Tuple<T1, T2, T3>? other) => other is not null && TupleValue.Equal(Item1, other.Item1) && TupleValue.Equal(Item2, other.Item2) && TupleValue.Equal(Item3, other.Item3);
        public override bool Equals(object? value) => value is Tuple<T1, T2, T3> other && Equals(other);
        public override int GetHashCode() => (TupleValue.Hash(Item1) * 31 + TupleValue.Hash(Item2)) * 31 + TupleValue.Hash(Item3);
        public override string ToString() => "(" + TupleValue.Text(Item1) + ", " + TupleValue.Text(Item2) + ", " + TupleValue.Text(Item3) + ")";
        int Collections.IStructuralComparable.CompareTo(object? other, Collections.IComparer comparer) =>
            other is null ? 1 : other is Tuple<T1, T2, T3> tuple ? TupleValue.StructuralCompare(comparer, Item1, tuple.Item1, Item2, tuple.Item2, Item3, tuple.Item3) : throw new ArgumentException();
        bool Collections.IStructuralEquatable.Equals(object? other, Collections.IEqualityComparer comparer) =>
            other is Tuple<T1, T2, T3> tuple && TupleValue.StructuralEquals(comparer, Item1, tuple.Item1, Item2, tuple.Item2, Item3, tuple.Item3);
        int Collections.IStructuralEquatable.GetHashCode(Collections.IEqualityComparer comparer) =>
            TupleValue.StructuralHash(comparer, Item1, Item2, Item3);
        int IComparable.CompareTo(object? other) =>
            other is null ? 1 : other is Tuple<T1, T2, T3> tuple ? TupleValue.StructuralCompare(TupleValue.DefaultComparer, Item1, tuple.Item1, Item2, tuple.Item2, Item3, tuple.Item3) : throw new ArgumentException();
    }

    public sealed class Tuple<T1, T2, T3, T4> : Collections.IStructuralComparable, Collections.IStructuralEquatable, IComparable, Runtime.CompilerServices.ITuple
    {
        public Tuple(T1 item1, T2 item2, T3 item3, T4 item4) { Item1 = item1; Item2 = item2; Item3 = item3; Item4 = item4; }
        public T1 Item1 { get; }
        public T2 Item2 { get; }
        public T3 Item3 { get; }
        public T4 Item4 { get; }
        int Runtime.CompilerServices.ITuple.Length => 4;
        object? Runtime.CompilerServices.ITuple.this[int index] => index switch { 0 => Item1, 1 => Item2, 2 => Item3, 3 => Item4, _ => throw new IndexOutOfRangeException() };
        public bool Equals(Tuple<T1, T2, T3, T4>? other) => other is not null && TupleValue.Equal(Item1, other.Item1) && TupleValue.Equal(Item2, other.Item2) && TupleValue.Equal(Item3, other.Item3) && TupleValue.Equal(Item4, other.Item4);
        public override bool Equals(object? value) => value is Tuple<T1, T2, T3, T4> other && Equals(other);
        public override int GetHashCode() => ((TupleValue.Hash(Item1) * 31 + TupleValue.Hash(Item2)) * 31 + TupleValue.Hash(Item3)) * 31 + TupleValue.Hash(Item4);
        public override string ToString() => "(" + TupleValue.Text(Item1) + ", " + TupleValue.Text(Item2) + ", " + TupleValue.Text(Item3) + ", " + TupleValue.Text(Item4) + ")";
        int Collections.IStructuralComparable.CompareTo(object? other, Collections.IComparer comparer) =>
            other is null ? 1 : other is Tuple<T1, T2, T3, T4> tuple ? TupleValue.StructuralCompare(comparer, Item1, tuple.Item1, Item2, tuple.Item2, Item3, tuple.Item3, Item4, tuple.Item4) : throw new ArgumentException();
        bool Collections.IStructuralEquatable.Equals(object? other, Collections.IEqualityComparer comparer) =>
            other is Tuple<T1, T2, T3, T4> tuple && TupleValue.StructuralEquals(comparer, Item1, tuple.Item1, Item2, tuple.Item2, Item3, tuple.Item3, Item4, tuple.Item4);
        int Collections.IStructuralEquatable.GetHashCode(Collections.IEqualityComparer comparer) =>
            TupleValue.StructuralHash(comparer, Item1, Item2, Item3, Item4);
        int IComparable.CompareTo(object? other) =>
            other is null ? 1 : other is Tuple<T1, T2, T3, T4> tuple ? TupleValue.StructuralCompare(TupleValue.DefaultComparer, Item1, tuple.Item1, Item2, tuple.Item2, Item3, tuple.Item3, Item4, tuple.Item4) : throw new ArgumentException();
    }

    public sealed class Tuple<T1, T2, T3, T4, T5> : Collections.IStructuralComparable, Collections.IStructuralEquatable, IComparable, Runtime.CompilerServices.ITuple
    {
        public Tuple(T1 item1, T2 item2, T3 item3, T4 item4, T5 item5) { Item1 = item1; Item2 = item2; Item3 = item3; Item4 = item4; Item5 = item5; }
        public T1 Item1 { get; }
        public T2 Item2 { get; }
        public T3 Item3 { get; }
        public T4 Item4 { get; }
        public T5 Item5 { get; }
        int Runtime.CompilerServices.ITuple.Length => 5;
        object? Runtime.CompilerServices.ITuple.this[int index] => index switch { 0 => Item1, 1 => Item2, 2 => Item3, 3 => Item4, 4 => Item5, _ => throw new IndexOutOfRangeException() };
        public bool Equals(Tuple<T1, T2, T3, T4, T5>? other) => other is not null && TupleValue.Equal(Item1, other.Item1) && TupleValue.Equal(Item2, other.Item2) && TupleValue.Equal(Item3, other.Item3) && TupleValue.Equal(Item4, other.Item4) && TupleValue.Equal(Item5, other.Item5);
        public override bool Equals(object? value) => value is Tuple<T1, T2, T3, T4, T5> other && Equals(other);
        public override int GetHashCode() => (((TupleValue.Hash(Item1) * 31 + TupleValue.Hash(Item2)) * 31 + TupleValue.Hash(Item3)) * 31 + TupleValue.Hash(Item4)) * 31 + TupleValue.Hash(Item5);
        public override string ToString() => "(" + TupleValue.Text(Item1) + ", " + TupleValue.Text(Item2) + ", " + TupleValue.Text(Item3) + ", " + TupleValue.Text(Item4) + ", " + TupleValue.Text(Item5) + ")";
        int Collections.IStructuralComparable.CompareTo(object? other, Collections.IComparer comparer) =>
            other is null ? 1 : other is Tuple<T1, T2, T3, T4, T5> tuple ? TupleValue.StructuralCompare(comparer, Item1, tuple.Item1, Item2, tuple.Item2, Item3, tuple.Item3, Item4, tuple.Item4, Item5, tuple.Item5) : throw new ArgumentException();
        bool Collections.IStructuralEquatable.Equals(object? other, Collections.IEqualityComparer comparer) =>
            other is Tuple<T1, T2, T3, T4, T5> tuple && TupleValue.StructuralEquals(comparer, Item1, tuple.Item1, Item2, tuple.Item2, Item3, tuple.Item3, Item4, tuple.Item4, Item5, tuple.Item5);
        int Collections.IStructuralEquatable.GetHashCode(Collections.IEqualityComparer comparer) =>
            TupleValue.StructuralHash(comparer, Item1, Item2, Item3, Item4, Item5);
        int IComparable.CompareTo(object? other) =>
            other is null ? 1 : other is Tuple<T1, T2, T3, T4, T5> tuple ? TupleValue.StructuralCompare(TupleValue.DefaultComparer, Item1, tuple.Item1, Item2, tuple.Item2, Item3, tuple.Item3, Item4, tuple.Item4, Item5, tuple.Item5) : throw new ArgumentException();
    }

    public sealed class Tuple<T1, T2, T3, T4, T5, T6> : Collections.IStructuralComparable, Collections.IStructuralEquatable, IComparable, Runtime.CompilerServices.ITuple
    {
        public Tuple(T1 item1, T2 item2, T3 item3, T4 item4, T5 item5, T6 item6) { Item1 = item1; Item2 = item2; Item3 = item3; Item4 = item4; Item5 = item5; Item6 = item6; }
        public T1 Item1 { get; }
        public T2 Item2 { get; }
        public T3 Item3 { get; }
        public T4 Item4 { get; }
        public T5 Item5 { get; }
        public T6 Item6 { get; }
        int Runtime.CompilerServices.ITuple.Length => 6;
        object? Runtime.CompilerServices.ITuple.this[int index] => index switch { 0 => Item1, 1 => Item2, 2 => Item3, 3 => Item4, 4 => Item5, 5 => Item6, _ => throw new IndexOutOfRangeException() };
        public bool Equals(Tuple<T1, T2, T3, T4, T5, T6>? other) => other is not null && TupleValue.Equal(Item1, other.Item1) && TupleValue.Equal(Item2, other.Item2) && TupleValue.Equal(Item3, other.Item3) && TupleValue.Equal(Item4, other.Item4) && TupleValue.Equal(Item5, other.Item5) && TupleValue.Equal(Item6, other.Item6);
        public override bool Equals(object? value) => value is Tuple<T1, T2, T3, T4, T5, T6> other && Equals(other);
        public override int GetHashCode() => ((((TupleValue.Hash(Item1) * 31 + TupleValue.Hash(Item2)) * 31 + TupleValue.Hash(Item3)) * 31 + TupleValue.Hash(Item4)) * 31 + TupleValue.Hash(Item5)) * 31 + TupleValue.Hash(Item6);
        public override string ToString() => "(" + TupleValue.Text(Item1) + ", " + TupleValue.Text(Item2) + ", " + TupleValue.Text(Item3) + ", " + TupleValue.Text(Item4) + ", " + TupleValue.Text(Item5) + ", " + TupleValue.Text(Item6) + ")";
        int Collections.IStructuralComparable.CompareTo(object? other, Collections.IComparer comparer) =>
            other is null ? 1 : other is Tuple<T1, T2, T3, T4, T5, T6> tuple ? TupleValue.StructuralCompare(comparer, Item1, tuple.Item1, Item2, tuple.Item2, Item3, tuple.Item3, Item4, tuple.Item4, Item5, tuple.Item5, Item6, tuple.Item6) : throw new ArgumentException();
        bool Collections.IStructuralEquatable.Equals(object? other, Collections.IEqualityComparer comparer) =>
            other is Tuple<T1, T2, T3, T4, T5, T6> tuple && TupleValue.StructuralEquals(comparer, Item1, tuple.Item1, Item2, tuple.Item2, Item3, tuple.Item3, Item4, tuple.Item4, Item5, tuple.Item5, Item6, tuple.Item6);
        int Collections.IStructuralEquatable.GetHashCode(Collections.IEqualityComparer comparer) =>
            TupleValue.StructuralHash(comparer, Item1, Item2, Item3, Item4, Item5, Item6);
        int IComparable.CompareTo(object? other) =>
            other is null ? 1 : other is Tuple<T1, T2, T3, T4, T5, T6> tuple ? TupleValue.StructuralCompare(TupleValue.DefaultComparer, Item1, tuple.Item1, Item2, tuple.Item2, Item3, tuple.Item3, Item4, tuple.Item4, Item5, tuple.Item5, Item6, tuple.Item6) : throw new ArgumentException();
    }

    public sealed class Tuple<T1, T2, T3, T4, T5, T6, T7> : Collections.IStructuralComparable, Collections.IStructuralEquatable, IComparable, Runtime.CompilerServices.ITuple
    {
        public Tuple(T1 item1, T2 item2, T3 item3, T4 item4, T5 item5, T6 item6, T7 item7) { Item1 = item1; Item2 = item2; Item3 = item3; Item4 = item4; Item5 = item5; Item6 = item6; Item7 = item7; }
        public T1 Item1 { get; }
        public T2 Item2 { get; }
        public T3 Item3 { get; }
        public T4 Item4 { get; }
        public T5 Item5 { get; }
        public T6 Item6 { get; }
        public T7 Item7 { get; }
        int Runtime.CompilerServices.ITuple.Length => 7;
        object? Runtime.CompilerServices.ITuple.this[int index] => index switch { 0 => Item1, 1 => Item2, 2 => Item3, 3 => Item4, 4 => Item5, 5 => Item6, 6 => Item7, _ => throw new IndexOutOfRangeException() };
        public bool Equals(Tuple<T1, T2, T3, T4, T5, T6, T7>? other) => other is not null && TupleValue.Equal(Item1, other.Item1) && TupleValue.Equal(Item2, other.Item2) && TupleValue.Equal(Item3, other.Item3) && TupleValue.Equal(Item4, other.Item4) && TupleValue.Equal(Item5, other.Item5) && TupleValue.Equal(Item6, other.Item6) && TupleValue.Equal(Item7, other.Item7);
        public override bool Equals(object? value) => value is Tuple<T1, T2, T3, T4, T5, T6, T7> other && Equals(other);
        public override int GetHashCode() => (((((TupleValue.Hash(Item1) * 31 + TupleValue.Hash(Item2)) * 31 + TupleValue.Hash(Item3)) * 31 + TupleValue.Hash(Item4)) * 31 + TupleValue.Hash(Item5)) * 31 + TupleValue.Hash(Item6)) * 31 + TupleValue.Hash(Item7);
        public override string ToString() => "(" + TupleValue.Text(Item1) + ", " + TupleValue.Text(Item2) + ", " + TupleValue.Text(Item3) + ", " + TupleValue.Text(Item4) + ", " + TupleValue.Text(Item5) + ", " + TupleValue.Text(Item6) + ", " + TupleValue.Text(Item7) + ")";
        int Collections.IStructuralComparable.CompareTo(object? other, Collections.IComparer comparer) =>
            other is null ? 1 : other is Tuple<T1, T2, T3, T4, T5, T6, T7> tuple ? TupleValue.StructuralCompare(comparer, Item1, tuple.Item1, Item2, tuple.Item2, Item3, tuple.Item3, Item4, tuple.Item4, Item5, tuple.Item5, Item6, tuple.Item6, Item7, tuple.Item7) : throw new ArgumentException();
        bool Collections.IStructuralEquatable.Equals(object? other, Collections.IEqualityComparer comparer) =>
            other is Tuple<T1, T2, T3, T4, T5, T6, T7> tuple && TupleValue.StructuralEquals(comparer, Item1, tuple.Item1, Item2, tuple.Item2, Item3, tuple.Item3, Item4, tuple.Item4, Item5, tuple.Item5, Item6, tuple.Item6, Item7, tuple.Item7);
        int Collections.IStructuralEquatable.GetHashCode(Collections.IEqualityComparer comparer) =>
            TupleValue.StructuralHash(comparer, Item1, Item2, Item3, Item4, Item5, Item6, Item7);
        int IComparable.CompareTo(object? other) =>
            other is null ? 1 : other is Tuple<T1, T2, T3, T4, T5, T6, T7> tuple ? TupleValue.StructuralCompare(TupleValue.DefaultComparer, Item1, tuple.Item1, Item2, tuple.Item2, Item3, tuple.Item3, Item4, tuple.Item4, Item5, tuple.Item5, Item6, tuple.Item6, Item7, tuple.Item7) : throw new ArgumentException();
    }

    public sealed class Tuple<T1, T2, T3, T4, T5, T6, T7, TRest> : Collections.IStructuralComparable, Collections.IStructuralEquatable, IComparable, Runtime.CompilerServices.ITuple where TRest : notnull
    {
        public Tuple(T1 item1, T2 item2, T3 item3, T4 item4, T5 item5, T6 item6, T7 item7, TRest rest) { Item1 = item1; Item2 = item2; Item3 = item3; Item4 = item4; Item5 = item5; Item6 = item6; Item7 = item7; Rest = rest; }
        public T1 Item1 { get; }
        public T2 Item2 { get; }
        public T3 Item3 { get; }
        public T4 Item4 { get; }
        public T5 Item5 { get; }
        public T6 Item6 { get; }
        public T7 Item7 { get; }
        public TRest Rest { get; }
        int Runtime.CompilerServices.ITuple.Length => 7 + (Rest is Runtime.CompilerServices.ITuple tuple ? tuple.Length : 1);
        object? Runtime.CompilerServices.ITuple.this[int index] => index switch { 0 => Item1, 1 => Item2, 2 => Item3, 3 => Item4, 4 => Item5, 5 => Item6, 6 => Item7, _ => Rest is Runtime.CompilerServices.ITuple tuple ? tuple[index - 7] : throw new IndexOutOfRangeException() };
        public bool Equals(Tuple<T1, T2, T3, T4, T5, T6, T7, TRest>? other) => other is not null && TupleValue.Equal(Item1, other.Item1) && TupleValue.Equal(Item2, other.Item2) && TupleValue.Equal(Item3, other.Item3) && TupleValue.Equal(Item4, other.Item4) && TupleValue.Equal(Item5, other.Item5) && TupleValue.Equal(Item6, other.Item6) && TupleValue.Equal(Item7, other.Item7) && TupleValue.Equal(Rest, other.Rest);
        public override bool Equals(object? value) => value is Tuple<T1, T2, T3, T4, T5, T6, T7, TRest> other && Equals(other);
        public override int GetHashCode() => ((((((TupleValue.Hash(Item1) * 31 + TupleValue.Hash(Item2)) * 31 + TupleValue.Hash(Item3)) * 31 + TupleValue.Hash(Item4)) * 31 + TupleValue.Hash(Item5)) * 31 + TupleValue.Hash(Item6)) * 31 + TupleValue.Hash(Item7)) * 31 + TupleValue.Hash(Rest);
        public override string ToString() => "(" + TupleValue.Text(Item1) + ", " + TupleValue.Text(Item2) + ", " + TupleValue.Text(Item3) + ", " + TupleValue.Text(Item4) + ", " + TupleValue.Text(Item5) + ", " + TupleValue.Text(Item6) + ", " + TupleValue.Text(Item7) + ", " + TupleValue.Text(Rest) + ")";
        int Collections.IStructuralComparable.CompareTo(object? other, Collections.IComparer comparer) =>
            other is null ? 1 : other is Tuple<T1, T2, T3, T4, T5, T6, T7, TRest> tuple ? TupleValue.StructuralCompare(comparer, Item1, tuple.Item1, Item2, tuple.Item2, Item3, tuple.Item3, Item4, tuple.Item4, Item5, tuple.Item5, Item6, tuple.Item6, Item7, tuple.Item7, Rest, tuple.Rest) : throw new ArgumentException();
        bool Collections.IStructuralEquatable.Equals(object? other, Collections.IEqualityComparer comparer) =>
            other is Tuple<T1, T2, T3, T4, T5, T6, T7, TRest> tuple && TupleValue.StructuralEquals(comparer, Item1, tuple.Item1, Item2, tuple.Item2, Item3, tuple.Item3, Item4, tuple.Item4, Item5, tuple.Item5, Item6, tuple.Item6, Item7, tuple.Item7, Rest, tuple.Rest);
        int Collections.IStructuralEquatable.GetHashCode(Collections.IEqualityComparer comparer) =>
            TupleValue.StructuralHash(comparer, Item1, Item2, Item3, Item4, Item5, Item6, Item7, Rest);
        int IComparable.CompareTo(object? other) =>
            other is null ? 1 : other is Tuple<T1, T2, T3, T4, T5, T6, T7, TRest> tuple ? TupleValue.StructuralCompare(TupleValue.DefaultComparer, Item1, tuple.Item1, Item2, tuple.Item2, Item3, tuple.Item3, Item4, tuple.Item4, Item5, tuple.Item5, Item6, tuple.Item6, Item7, tuple.Item7, Rest, tuple.Rest) : throw new ArgumentException();
    }

    public static partial class TupleExtensions
    {
        public static void Deconstruct<T1>(this Tuple<T1> value, out T1 item1) { item1 = value.Item1; }
        public static void Deconstruct<T1, T2>(this Tuple<T1, T2> value, out T1 item1, out T2 item2) { item1 = value.Item1; item2 = value.Item2; }
        public static void Deconstruct<T1, T2, T3>(this Tuple<T1, T2, T3> value, out T1 item1, out T2 item2, out T3 item3) { item1 = value.Item1; item2 = value.Item2; item3 = value.Item3; }
        public static void Deconstruct<T1, T2, T3, T4>(this Tuple<T1, T2, T3, T4> value, out T1 item1, out T2 item2, out T3 item3, out T4 item4) { item1 = value.Item1; item2 = value.Item2; item3 = value.Item3; item4 = value.Item4; }
        public static void Deconstruct<T1, T2, T3, T4, T5>(this Tuple<T1, T2, T3, T4, T5> value, out T1 item1, out T2 item2, out T3 item3, out T4 item4, out T5 item5) { item1 = value.Item1; item2 = value.Item2; item3 = value.Item3; item4 = value.Item4; item5 = value.Item5; }
        public static void Deconstruct<T1, T2, T3, T4, T5, T6>(this Tuple<T1, T2, T3, T4, T5, T6> value, out T1 item1, out T2 item2, out T3 item3, out T4 item4, out T5 item5, out T6 item6) { item1 = value.Item1; item2 = value.Item2; item3 = value.Item3; item4 = value.Item4; item5 = value.Item5; item6 = value.Item6; }
        public static void Deconstruct<T1, T2, T3, T4, T5, T6, T7>(this Tuple<T1, T2, T3, T4, T5, T6, T7> value, out T1 item1, out T2 item2, out T3 item3, out T4 item4, out T5 item5, out T6 item6, out T7 item7) { item1 = value.Item1; item2 = value.Item2; item3 = value.Item3; item4 = value.Item4; item5 = value.Item5; item6 = value.Item6; item7 = value.Item7; }

        public static Tuple<T1> ToTuple<T1>(this ValueTuple<T1> value) => new(value.Item1);
        public static Tuple<T1, T2> ToTuple<T1, T2>(this ValueTuple<T1, T2> value) => new(value.Item1, value.Item2);
        public static Tuple<T1, T2, T3> ToTuple<T1, T2, T3>(this ValueTuple<T1, T2, T3> value) => new(value.Item1, value.Item2, value.Item3);
        public static Tuple<T1, T2, T3, T4> ToTuple<T1, T2, T3, T4>(this ValueTuple<T1, T2, T3, T4> value) => new(value.Item1, value.Item2, value.Item3, value.Item4);
        public static Tuple<T1, T2, T3, T4, T5> ToTuple<T1, T2, T3, T4, T5>(this ValueTuple<T1, T2, T3, T4, T5> value) => new(value.Item1, value.Item2, value.Item3, value.Item4, value.Item5);
        public static Tuple<T1, T2, T3, T4, T5, T6> ToTuple<T1, T2, T3, T4, T5, T6>(this ValueTuple<T1, T2, T3, T4, T5, T6> value) => new(value.Item1, value.Item2, value.Item3, value.Item4, value.Item5, value.Item6);
        public static Tuple<T1, T2, T3, T4, T5, T6, T7> ToTuple<T1, T2, T3, T4, T5, T6, T7>(this ValueTuple<T1, T2, T3, T4, T5, T6, T7> value) => new(value.Item1, value.Item2, value.Item3, value.Item4, value.Item5, value.Item6, value.Item7);
        public static ValueTuple<T1> ToValueTuple<T1>(this Tuple<T1> value) => new(value.Item1);
        public static ValueTuple<T1, T2> ToValueTuple<T1, T2>(this Tuple<T1, T2> value) => new(value.Item1, value.Item2);
        public static ValueTuple<T1, T2, T3> ToValueTuple<T1, T2, T3>(this Tuple<T1, T2, T3> value) => new(value.Item1, value.Item2, value.Item3);
        public static ValueTuple<T1, T2, T3, T4> ToValueTuple<T1, T2, T3, T4>(this Tuple<T1, T2, T3, T4> value) => new(value.Item1, value.Item2, value.Item3, value.Item4);
        public static ValueTuple<T1, T2, T3, T4, T5> ToValueTuple<T1, T2, T3, T4, T5>(this Tuple<T1, T2, T3, T4, T5> value) => new(value.Item1, value.Item2, value.Item3, value.Item4, value.Item5);
        public static ValueTuple<T1, T2, T3, T4, T5, T6> ToValueTuple<T1, T2, T3, T4, T5, T6>(this Tuple<T1, T2, T3, T4, T5, T6> value) => new(value.Item1, value.Item2, value.Item3, value.Item4, value.Item5, value.Item6);
        public static ValueTuple<T1, T2, T3, T4, T5, T6, T7> ToValueTuple<T1, T2, T3, T4, T5, T6, T7>(this Tuple<T1, T2, T3, T4, T5, T6, T7> value) => new(value.Item1, value.Item2, value.Item3, value.Item4, value.Item5, value.Item6, value.Item7);
    }
}
