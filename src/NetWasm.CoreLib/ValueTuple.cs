// Adapted from dotnet/runtime System.Private.CoreLib ValueTuple contracts.
// The upstream implementation is licensed under MIT.
// Copyright (c) .NET Foundation and Contributors.
namespace System
{
    internal static class TupleContract
    {
        internal static bool Equals<T>(T left, T right) =>
            Collections.Generic.EqualityComparer<T>.Default.Equals(left, right);

        internal static int Hash<T>(T value) =>
            Collections.Generic.EqualityComparer<T>.Default.GetHashCode(value!);

        internal static int Compare<T>(T left, T right)
        {
            if (left is null) return right is null ? 0 : -1;
            if (right is null) return 1;
            if (left is IComparable<T> comparable) return comparable.CompareTo(right);
            if (left is IComparable nonGeneric) return nonGeneric.CompareTo(right);
            throw new ArgumentException();
        }

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

        internal static string Text(object? value) => value?.ToString() ?? "";
    }

    public struct ValueTuple : Collections.IStructuralComparable, Collections.IStructuralEquatable, IComparable, IComparable<ValueTuple>, IEquatable<ValueTuple>,
        Runtime.CompilerServices.ITuple
    {
        public static ValueTuple Create() => default;
        public bool Equals(ValueTuple other) => true;
        public override bool Equals(object? value) => value is ValueTuple;
        public override int GetHashCode() => 0;
        public int CompareTo(ValueTuple other) => 0;
        public int CompareTo(object? value) => value is null ? 1 : value is ValueTuple ? 0 : throw new ArgumentException();
        int Runtime.CompilerServices.ITuple.Length => 0;
        object? Runtime.CompilerServices.ITuple.this[int index] => throw new IndexOutOfRangeException();
        public override string ToString() => "()";
        int Collections.IStructuralComparable.CompareTo(object? other, Collections.IComparer comparer) =>
            other is null ? 1 : other is ValueTuple ? 0 : throw new ArgumentException();
        bool Collections.IStructuralEquatable.Equals(object? other, Collections.IEqualityComparer comparer) => other is ValueTuple;
        int Collections.IStructuralEquatable.GetHashCode(Collections.IEqualityComparer comparer) => 0;
        public static ValueTuple<T1> Create<T1>(T1 item1) => new(item1);
        public static ValueTuple<T1, T2> Create<T1, T2>(T1 item1, T2 item2) => new(item1, item2);
        public static ValueTuple<T1, T2, T3> Create<T1, T2, T3>(T1 item1, T2 item2, T3 item3) => new(item1, item2, item3);
        public static ValueTuple<T1, T2, T3, T4> Create<T1, T2, T3, T4>(T1 item1, T2 item2, T3 item3, T4 item4) => new(item1, item2, item3, item4);
        public static ValueTuple<T1, T2, T3, T4, T5> Create<T1, T2, T3, T4, T5>(T1 item1, T2 item2, T3 item3, T4 item4, T5 item5) => new(item1, item2, item3, item4, item5);
        public static ValueTuple<T1, T2, T3, T4, T5, T6> Create<T1, T2, T3, T4, T5, T6>(T1 item1, T2 item2, T3 item3, T4 item4, T5 item5, T6 item6) => new(item1, item2, item3, item4, item5, item6);
        public static ValueTuple<T1, T2, T3, T4, T5, T6, T7> Create<T1, T2, T3, T4, T5, T6, T7>(T1 item1, T2 item2, T3 item3, T4 item4, T5 item5, T6 item6, T7 item7) => new(item1, item2, item3, item4, item5, item6, item7);
        public static ValueTuple<T1, T2, T3, T4, T5, T6, T7, ValueTuple<T8>> Create<T1, T2, T3, T4, T5, T6, T7, T8>(T1 item1, T2 item2, T3 item3, T4 item4, T5 item5, T6 item6, T7 item7, T8 item8) => new(item1, item2, item3, item4, item5, item6, item7, new(item8));
    }

    public struct ValueTuple<T1> : Collections.IStructuralComparable, Collections.IStructuralEquatable, IComparable, IComparable<ValueTuple<T1>>, IEquatable<ValueTuple<T1>>,
        Runtime.CompilerServices.ITuple
    {
        public T1 Item1;
        public ValueTuple(T1 item1) => Item1 = item1;
        public bool Equals(ValueTuple<T1> other) => TupleContract.Equals(Item1, other.Item1);
        public override bool Equals(object? value) => value is ValueTuple<T1> other && Equals(other);
        public override int GetHashCode() => TupleContract.Hash(Item1);
        public int CompareTo(ValueTuple<T1> other) => TupleContract.Compare(Item1, other.Item1);
        public int CompareTo(object? value) => value is null ? 1 : value is ValueTuple<T1> other ? CompareTo(other) : throw new ArgumentException();
        int Runtime.CompilerServices.ITuple.Length => 1;
        object? Runtime.CompilerServices.ITuple.this[int index] => index == 0 ? Item1 : throw new IndexOutOfRangeException();
        public override string ToString() => "(" + TupleContract.Text(Item1) + ")";
        int Collections.IStructuralComparable.CompareTo(object? other, Collections.IComparer comparer) =>
            other is null ? 1 : other is ValueTuple<T1> tuple ? TupleContract.StructuralCompare(comparer, Item1, tuple.Item1) : throw new ArgumentException();
        bool Collections.IStructuralEquatable.Equals(object? other, Collections.IEqualityComparer comparer) =>
            other is ValueTuple<T1> tuple && TupleContract.StructuralEquals(comparer, Item1, tuple.Item1);
        int Collections.IStructuralEquatable.GetHashCode(Collections.IEqualityComparer comparer) =>
            TupleContract.StructuralHash(comparer, Item1);
    }

    public struct ValueTuple<T1, T2> : Collections.IStructuralComparable, Collections.IStructuralEquatable, IComparable, IComparable<ValueTuple<T1, T2>>, IEquatable<ValueTuple<T1, T2>>,
        Runtime.CompilerServices.ITuple
    {
        public T1 Item1; public T2 Item2;
        public ValueTuple(T1 item1, T2 item2) { Item1 = item1; Item2 = item2; }
        public bool Equals(ValueTuple<T1, T2> other) => TupleContract.Equals(Item1, other.Item1) && TupleContract.Equals(Item2, other.Item2);
        public override bool Equals(object? value) => value is ValueTuple<T1, T2> other && Equals(other);
        public override int GetHashCode() => TupleContract.Hash(Item1) * 31 + TupleContract.Hash(Item2);
        public int CompareTo(ValueTuple<T1, T2> other) { var result = TupleContract.Compare(Item1, other.Item1); return result != 0 ? result : TupleContract.Compare(Item2, other.Item2); }
        public int CompareTo(object? value) => value is null ? 1 : value is ValueTuple<T1, T2> other ? CompareTo(other) : throw new ArgumentException();
        int Runtime.CompilerServices.ITuple.Length => 2;
        object? Runtime.CompilerServices.ITuple.this[int index] => index switch { 0 => Item1, 1 => Item2, _ => throw new IndexOutOfRangeException() };
        public override string ToString() => "(" + TupleContract.Text(Item1) + ", " + TupleContract.Text(Item2) + ")";
        public static bool operator ==(ValueTuple<T1, T2> left, ValueTuple<T1, T2> right) => left.Equals(right);
        public static bool operator !=(ValueTuple<T1, T2> left, ValueTuple<T1, T2> right) => !left.Equals(right);
        int Collections.IStructuralComparable.CompareTo(object? other, Collections.IComparer comparer) =>
            other is null ? 1 : other is ValueTuple<T1, T2> tuple ? TupleContract.StructuralCompare(comparer, Item1, tuple.Item1, Item2, tuple.Item2) : throw new ArgumentException();
        bool Collections.IStructuralEquatable.Equals(object? other, Collections.IEqualityComparer comparer) =>
            other is ValueTuple<T1, T2> tuple && TupleContract.StructuralEquals(comparer, Item1, tuple.Item1, Item2, tuple.Item2);
        int Collections.IStructuralEquatable.GetHashCode(Collections.IEqualityComparer comparer) =>
            TupleContract.StructuralHash(comparer, Item1, Item2);
    }

    public struct ValueTuple<T1, T2, T3> : Collections.IStructuralComparable, Collections.IStructuralEquatable, IComparable, IComparable<ValueTuple<T1, T2, T3>>, IEquatable<ValueTuple<T1, T2, T3>>,
        Runtime.CompilerServices.ITuple
    {
        public T1 Item1; public T2 Item2; public T3 Item3;
        public ValueTuple(T1 item1, T2 item2, T3 item3) { Item1 = item1; Item2 = item2; Item3 = item3; }
        public bool Equals(ValueTuple<T1, T2, T3> other) => TupleContract.Equals(Item1, other.Item1) && TupleContract.Equals(Item2, other.Item2) && TupleContract.Equals(Item3, other.Item3);
        public override bool Equals(object? value) => value is ValueTuple<T1, T2, T3> other && Equals(other);
        public override int GetHashCode() => (TupleContract.Hash(Item1) * 31 + TupleContract.Hash(Item2)) * 31 + TupleContract.Hash(Item3);
        public int CompareTo(ValueTuple<T1, T2, T3> other) { var result = TupleContract.Compare(Item1, other.Item1); if (result != 0) return result; result = TupleContract.Compare(Item2, other.Item2); return result != 0 ? result : TupleContract.Compare(Item3, other.Item3); }
        public int CompareTo(object? value) => value is null ? 1 : value is ValueTuple<T1, T2, T3> other ? CompareTo(other) : throw new ArgumentException();
        int Runtime.CompilerServices.ITuple.Length => 3;
        object? Runtime.CompilerServices.ITuple.this[int index] => index switch { 0 => Item1, 1 => Item2, 2 => Item3, _ => throw new IndexOutOfRangeException() };
        public override string ToString() => "(" + TupleContract.Text(Item1) + ", " + TupleContract.Text(Item2) + ", " + TupleContract.Text(Item3) + ")";
        int Collections.IStructuralComparable.CompareTo(object? other, Collections.IComparer comparer) =>
            other is null ? 1 : other is ValueTuple<T1, T2, T3> tuple ? TupleContract.StructuralCompare(comparer, Item1, tuple.Item1, Item2, tuple.Item2, Item3, tuple.Item3) : throw new ArgumentException();
        bool Collections.IStructuralEquatable.Equals(object? other, Collections.IEqualityComparer comparer) =>
            other is ValueTuple<T1, T2, T3> tuple && TupleContract.StructuralEquals(comparer, Item1, tuple.Item1, Item2, tuple.Item2, Item3, tuple.Item3);
        int Collections.IStructuralEquatable.GetHashCode(Collections.IEqualityComparer comparer) =>
            TupleContract.StructuralHash(comparer, Item1, Item2, Item3);
    }

    public struct ValueTuple<T1, T2, T3, T4> : Collections.IStructuralComparable, Collections.IStructuralEquatable, IComparable, IComparable<ValueTuple<T1, T2, T3, T4>>, IEquatable<ValueTuple<T1, T2, T3, T4>>,
        Runtime.CompilerServices.ITuple
    {
        public T1 Item1; public T2 Item2; public T3 Item3; public T4 Item4;
        public ValueTuple(T1 item1, T2 item2, T3 item3, T4 item4) { Item1 = item1; Item2 = item2; Item3 = item3; Item4 = item4; }
        public bool Equals(ValueTuple<T1, T2, T3, T4> other) => TupleContract.Equals(Item1, other.Item1) && TupleContract.Equals(Item2, other.Item2) && TupleContract.Equals(Item3, other.Item3) && TupleContract.Equals(Item4, other.Item4);
        public override bool Equals(object? value) => value is ValueTuple<T1, T2, T3, T4> other && Equals(other);
        public override int GetHashCode() => ((TupleContract.Hash(Item1) * 31 + TupleContract.Hash(Item2)) * 31 + TupleContract.Hash(Item3)) * 31 + TupleContract.Hash(Item4);
        public int CompareTo(ValueTuple<T1, T2, T3, T4> other) { var result = TupleContract.Compare(Item1, other.Item1); if (result != 0) return result; result = TupleContract.Compare(Item2, other.Item2); if (result != 0) return result; result = TupleContract.Compare(Item3, other.Item3); return result != 0 ? result : TupleContract.Compare(Item4, other.Item4); }
        public int CompareTo(object? value) => value is null ? 1 : value is ValueTuple<T1, T2, T3, T4> other ? CompareTo(other) : throw new ArgumentException();
        int Runtime.CompilerServices.ITuple.Length => 4;
        object? Runtime.CompilerServices.ITuple.this[int index] => index switch { 0 => Item1, 1 => Item2, 2 => Item3, 3 => Item4, _ => throw new IndexOutOfRangeException() };
        public override string ToString() => "(" + TupleContract.Text(Item1) + ", " + TupleContract.Text(Item2) + ", " + TupleContract.Text(Item3) + ", " + TupleContract.Text(Item4) + ")";
        int Collections.IStructuralComparable.CompareTo(object? other, Collections.IComparer comparer) =>
            other is null ? 1 : other is ValueTuple<T1, T2, T3, T4> tuple ? TupleContract.StructuralCompare(comparer, Item1, tuple.Item1, Item2, tuple.Item2, Item3, tuple.Item3, Item4, tuple.Item4) : throw new ArgumentException();
        bool Collections.IStructuralEquatable.Equals(object? other, Collections.IEqualityComparer comparer) =>
            other is ValueTuple<T1, T2, T3, T4> tuple && TupleContract.StructuralEquals(comparer, Item1, tuple.Item1, Item2, tuple.Item2, Item3, tuple.Item3, Item4, tuple.Item4);
        int Collections.IStructuralEquatable.GetHashCode(Collections.IEqualityComparer comparer) =>
            TupleContract.StructuralHash(comparer, Item1, Item2, Item3, Item4);
    }

    public struct ValueTuple<T1, T2, T3, T4, T5> : Collections.IStructuralComparable, Collections.IStructuralEquatable, IComparable, IComparable<ValueTuple<T1, T2, T3, T4, T5>>, IEquatable<ValueTuple<T1, T2, T3, T4, T5>>,
        Runtime.CompilerServices.ITuple
    {
        public T1 Item1; public T2 Item2; public T3 Item3; public T4 Item4; public T5 Item5;
        public ValueTuple(T1 item1, T2 item2, T3 item3, T4 item4, T5 item5) { Item1 = item1; Item2 = item2; Item3 = item3; Item4 = item4; Item5 = item5; }
        public bool Equals(ValueTuple<T1, T2, T3, T4, T5> other) => TupleContract.Equals(Item1, other.Item1) && TupleContract.Equals(Item2, other.Item2) && TupleContract.Equals(Item3, other.Item3) && TupleContract.Equals(Item4, other.Item4) && TupleContract.Equals(Item5, other.Item5);
        public override bool Equals(object? value) => value is ValueTuple<T1, T2, T3, T4, T5> other && Equals(other);
        public override int GetHashCode() => (((TupleContract.Hash(Item1) * 31 + TupleContract.Hash(Item2)) * 31 + TupleContract.Hash(Item3)) * 31 + TupleContract.Hash(Item4)) * 31 + TupleContract.Hash(Item5);
        public int CompareTo(ValueTuple<T1, T2, T3, T4, T5> other) { var result = TupleContract.Compare(Item1, other.Item1); if (result != 0) return result; result = TupleContract.Compare(Item2, other.Item2); if (result != 0) return result; result = TupleContract.Compare(Item3, other.Item3); if (result != 0) return result; result = TupleContract.Compare(Item4, other.Item4); return result != 0 ? result : TupleContract.Compare(Item5, other.Item5); }
        public int CompareTo(object? value) => value is null ? 1 : value is ValueTuple<T1, T2, T3, T4, T5> other ? CompareTo(other) : throw new ArgumentException();
        int Runtime.CompilerServices.ITuple.Length => 5;
        object? Runtime.CompilerServices.ITuple.this[int index] => index switch { 0 => Item1, 1 => Item2, 2 => Item3, 3 => Item4, 4 => Item5, _ => throw new IndexOutOfRangeException() };
        public override string ToString() => "(" + TupleContract.Text(Item1) + ", " + TupleContract.Text(Item2) + ", " + TupleContract.Text(Item3) + ", " + TupleContract.Text(Item4) + ", " + TupleContract.Text(Item5) + ")";
        int Collections.IStructuralComparable.CompareTo(object? other, Collections.IComparer comparer) =>
            other is null ? 1 : other is ValueTuple<T1, T2, T3, T4, T5> tuple ? TupleContract.StructuralCompare(comparer, Item1, tuple.Item1, Item2, tuple.Item2, Item3, tuple.Item3, Item4, tuple.Item4, Item5, tuple.Item5) : throw new ArgumentException();
        bool Collections.IStructuralEquatable.Equals(object? other, Collections.IEqualityComparer comparer) =>
            other is ValueTuple<T1, T2, T3, T4, T5> tuple && TupleContract.StructuralEquals(comparer, Item1, tuple.Item1, Item2, tuple.Item2, Item3, tuple.Item3, Item4, tuple.Item4, Item5, tuple.Item5);
        int Collections.IStructuralEquatable.GetHashCode(Collections.IEqualityComparer comparer) =>
            TupleContract.StructuralHash(comparer, Item1, Item2, Item3, Item4, Item5);
    }

    public struct ValueTuple<T1, T2, T3, T4, T5, T6> : Collections.IStructuralComparable, Collections.IStructuralEquatable, IComparable, IComparable<ValueTuple<T1, T2, T3, T4, T5, T6>>, IEquatable<ValueTuple<T1, T2, T3, T4, T5, T6>>,
        Runtime.CompilerServices.ITuple
    {
        public T1 Item1; public T2 Item2; public T3 Item3; public T4 Item4; public T5 Item5; public T6 Item6;
        public ValueTuple(T1 item1, T2 item2, T3 item3, T4 item4, T5 item5, T6 item6) { Item1 = item1; Item2 = item2; Item3 = item3; Item4 = item4; Item5 = item5; Item6 = item6; }
        public bool Equals(ValueTuple<T1, T2, T3, T4, T5, T6> other) => TupleContract.Equals(Item1, other.Item1) && TupleContract.Equals(Item2, other.Item2) && TupleContract.Equals(Item3, other.Item3) && TupleContract.Equals(Item4, other.Item4) && TupleContract.Equals(Item5, other.Item5) && TupleContract.Equals(Item6, other.Item6);
        public override bool Equals(object? value) => value is ValueTuple<T1, T2, T3, T4, T5, T6> other && Equals(other);
        public override int GetHashCode() => ((((TupleContract.Hash(Item1) * 31 + TupleContract.Hash(Item2)) * 31 + TupleContract.Hash(Item3)) * 31 + TupleContract.Hash(Item4)) * 31 + TupleContract.Hash(Item5)) * 31 + TupleContract.Hash(Item6);
        public int CompareTo(ValueTuple<T1, T2, T3, T4, T5, T6> other) { var result = TupleContract.Compare(Item1, other.Item1); if (result != 0) return result; result = TupleContract.Compare(Item2, other.Item2); if (result != 0) return result; result = TupleContract.Compare(Item3, other.Item3); if (result != 0) return result; result = TupleContract.Compare(Item4, other.Item4); if (result != 0) return result; result = TupleContract.Compare(Item5, other.Item5); return result != 0 ? result : TupleContract.Compare(Item6, other.Item6); }
        public int CompareTo(object? value) => value is null ? 1 : value is ValueTuple<T1, T2, T3, T4, T5, T6> other ? CompareTo(other) : throw new ArgumentException();
        int Runtime.CompilerServices.ITuple.Length => 6;
        object? Runtime.CompilerServices.ITuple.this[int index] => index switch { 0 => Item1, 1 => Item2, 2 => Item3, 3 => Item4, 4 => Item5, 5 => Item6, _ => throw new IndexOutOfRangeException() };
        public override string ToString() => "(" + TupleContract.Text(Item1) + ", " + TupleContract.Text(Item2) + ", " + TupleContract.Text(Item3) + ", " + TupleContract.Text(Item4) + ", " + TupleContract.Text(Item5) + ", " + TupleContract.Text(Item6) + ")";
        int Collections.IStructuralComparable.CompareTo(object? other, Collections.IComparer comparer) =>
            other is null ? 1 : other is ValueTuple<T1, T2, T3, T4, T5, T6> tuple ? TupleContract.StructuralCompare(comparer, Item1, tuple.Item1, Item2, tuple.Item2, Item3, tuple.Item3, Item4, tuple.Item4, Item5, tuple.Item5, Item6, tuple.Item6) : throw new ArgumentException();
        bool Collections.IStructuralEquatable.Equals(object? other, Collections.IEqualityComparer comparer) =>
            other is ValueTuple<T1, T2, T3, T4, T5, T6> tuple && TupleContract.StructuralEquals(comparer, Item1, tuple.Item1, Item2, tuple.Item2, Item3, tuple.Item3, Item4, tuple.Item4, Item5, tuple.Item5, Item6, tuple.Item6);
        int Collections.IStructuralEquatable.GetHashCode(Collections.IEqualityComparer comparer) =>
            TupleContract.StructuralHash(comparer, Item1, Item2, Item3, Item4, Item5, Item6);
    }

    public struct ValueTuple<T1, T2, T3, T4, T5, T6, T7> : Collections.IStructuralComparable, Collections.IStructuralEquatable, IComparable, IComparable<ValueTuple<T1, T2, T3, T4, T5, T6, T7>>, IEquatable<ValueTuple<T1, T2, T3, T4, T5, T6, T7>>,
        Runtime.CompilerServices.ITuple
    {
        public T1 Item1; public T2 Item2; public T3 Item3; public T4 Item4; public T5 Item5; public T6 Item6; public T7 Item7;
        public ValueTuple(T1 item1, T2 item2, T3 item3, T4 item4, T5 item5, T6 item6, T7 item7) { Item1 = item1; Item2 = item2; Item3 = item3; Item4 = item4; Item5 = item5; Item6 = item6; Item7 = item7; }
        public bool Equals(ValueTuple<T1, T2, T3, T4, T5, T6, T7> other) => TupleContract.Equals(Item1, other.Item1) && TupleContract.Equals(Item2, other.Item2) && TupleContract.Equals(Item3, other.Item3) && TupleContract.Equals(Item4, other.Item4) && TupleContract.Equals(Item5, other.Item5) && TupleContract.Equals(Item6, other.Item6) && TupleContract.Equals(Item7, other.Item7);
        public override bool Equals(object? value) => value is ValueTuple<T1, T2, T3, T4, T5, T6, T7> other && Equals(other);
        public override int GetHashCode() => (((((TupleContract.Hash(Item1) * 31 + TupleContract.Hash(Item2)) * 31 + TupleContract.Hash(Item3)) * 31 + TupleContract.Hash(Item4)) * 31 + TupleContract.Hash(Item5)) * 31 + TupleContract.Hash(Item6)) * 31 + TupleContract.Hash(Item7);
        public int CompareTo(ValueTuple<T1, T2, T3, T4, T5, T6, T7> other) { var result = TupleContract.Compare(Item1, other.Item1); if (result != 0) return result; result = TupleContract.Compare(Item2, other.Item2); if (result != 0) return result; result = TupleContract.Compare(Item3, other.Item3); if (result != 0) return result; result = TupleContract.Compare(Item4, other.Item4); if (result != 0) return result; result = TupleContract.Compare(Item5, other.Item5); if (result != 0) return result; result = TupleContract.Compare(Item6, other.Item6); return result != 0 ? result : TupleContract.Compare(Item7, other.Item7); }
        public int CompareTo(object? value) => value is null ? 1 : value is ValueTuple<T1, T2, T3, T4, T5, T6, T7> other ? CompareTo(other) : throw new ArgumentException();
        int Runtime.CompilerServices.ITuple.Length => 7;
        object? Runtime.CompilerServices.ITuple.this[int index] => index switch { 0 => Item1, 1 => Item2, 2 => Item3, 3 => Item4, 4 => Item5, 5 => Item6, 6 => Item7, _ => throw new IndexOutOfRangeException() };
        public override string ToString() => "(" + TupleContract.Text(Item1) + ", " + TupleContract.Text(Item2) + ", " + TupleContract.Text(Item3) + ", " + TupleContract.Text(Item4) + ", " + TupleContract.Text(Item5) + ", " + TupleContract.Text(Item6) + ", " + TupleContract.Text(Item7) + ")";
        int Collections.IStructuralComparable.CompareTo(object? other, Collections.IComparer comparer) =>
            other is null ? 1 : other is ValueTuple<T1, T2, T3, T4, T5, T6, T7> tuple ? TupleContract.StructuralCompare(comparer, Item1, tuple.Item1, Item2, tuple.Item2, Item3, tuple.Item3, Item4, tuple.Item4, Item5, tuple.Item5, Item6, tuple.Item6, Item7, tuple.Item7) : throw new ArgumentException();
        bool Collections.IStructuralEquatable.Equals(object? other, Collections.IEqualityComparer comparer) =>
            other is ValueTuple<T1, T2, T3, T4, T5, T6, T7> tuple && TupleContract.StructuralEquals(comparer, Item1, tuple.Item1, Item2, tuple.Item2, Item3, tuple.Item3, Item4, tuple.Item4, Item5, tuple.Item5, Item6, tuple.Item6, Item7, tuple.Item7);
        int Collections.IStructuralEquatable.GetHashCode(Collections.IEqualityComparer comparer) =>
            TupleContract.StructuralHash(comparer, Item1, Item2, Item3, Item4, Item5, Item6, Item7);
    }

    public struct ValueTuple<T1, T2, T3, T4, T5, T6, T7, TRest> : Collections.IStructuralComparable, Collections.IStructuralEquatable, IComparable, IComparable<ValueTuple<T1, T2, T3, T4, T5, T6, T7, TRest>>, IEquatable<ValueTuple<T1, T2, T3, T4, T5, T6, T7, TRest>>,
        Runtime.CompilerServices.ITuple where TRest : struct
    {
        public T1 Item1; public T2 Item2; public T3 Item3; public T4 Item4; public T5 Item5; public T6 Item6; public T7 Item7; public TRest Rest;
        public ValueTuple(T1 item1, T2 item2, T3 item3, T4 item4, T5 item5, T6 item6, T7 item7, TRest rest) { Item1 = item1; Item2 = item2; Item3 = item3; Item4 = item4; Item5 = item5; Item6 = item6; Item7 = item7; Rest = rest; }
        public bool Equals(ValueTuple<T1, T2, T3, T4, T5, T6, T7, TRest> other) => TupleContract.Equals(Item1, other.Item1) && TupleContract.Equals(Item2, other.Item2) && TupleContract.Equals(Item3, other.Item3) && TupleContract.Equals(Item4, other.Item4) && TupleContract.Equals(Item5, other.Item5) && TupleContract.Equals(Item6, other.Item6) && TupleContract.Equals(Item7, other.Item7) && TupleContract.Equals(Rest, other.Rest);
        public override bool Equals(object? value) => value is ValueTuple<T1, T2, T3, T4, T5, T6, T7, TRest> other && Equals(other);
        public override int GetHashCode() => ((((((TupleContract.Hash(Item1) * 31 + TupleContract.Hash(Item2)) * 31 + TupleContract.Hash(Item3)) * 31 + TupleContract.Hash(Item4)) * 31 + TupleContract.Hash(Item5)) * 31 + TupleContract.Hash(Item6)) * 31 + TupleContract.Hash(Item7)) * 31 + TupleContract.Hash(Rest);
        public int CompareTo(ValueTuple<T1, T2, T3, T4, T5, T6, T7, TRest> other) { var result = TupleContract.Compare(Item1, other.Item1); if (result != 0) return result; result = TupleContract.Compare(Item2, other.Item2); if (result != 0) return result; result = TupleContract.Compare(Item3, other.Item3); if (result != 0) return result; result = TupleContract.Compare(Item4, other.Item4); if (result != 0) return result; result = TupleContract.Compare(Item5, other.Item5); if (result != 0) return result; result = TupleContract.Compare(Item6, other.Item6); if (result != 0) return result; result = TupleContract.Compare(Item7, other.Item7); return result != 0 ? result : TupleContract.Compare(Rest, other.Rest); }
        public int CompareTo(object? value) => value is null ? 1 : value is ValueTuple<T1, T2, T3, T4, T5, T6, T7, TRest> other ? CompareTo(other) : throw new ArgumentException();
        int Runtime.CompilerServices.ITuple.Length => 7 + (Rest is Runtime.CompilerServices.ITuple tuple ? tuple.Length : 1);
        object? Runtime.CompilerServices.ITuple.this[int index] => index switch { 0 => Item1, 1 => Item2, 2 => Item3, 3 => Item4, 4 => Item5, 5 => Item6, 6 => Item7, _ => Rest is Runtime.CompilerServices.ITuple tuple ? tuple[index - 7] : throw new IndexOutOfRangeException() };
        public override string ToString() => "(" + TupleContract.Text(Item1) + ", " + TupleContract.Text(Item2) + ", " + TupleContract.Text(Item3) + ", " + TupleContract.Text(Item4) + ", " + TupleContract.Text(Item5) + ", " + TupleContract.Text(Item6) + ", " + TupleContract.Text(Item7) + ", " + TupleContract.Text(Rest) + ")";
        int Collections.IStructuralComparable.CompareTo(object? other, Collections.IComparer comparer) =>
            other is null ? 1 : other is ValueTuple<T1, T2, T3, T4, T5, T6, T7, TRest> tuple ? TupleContract.StructuralCompare(comparer, Item1, tuple.Item1, Item2, tuple.Item2, Item3, tuple.Item3, Item4, tuple.Item4, Item5, tuple.Item5, Item6, tuple.Item6, Item7, tuple.Item7, Rest, tuple.Rest) : throw new ArgumentException();
        bool Collections.IStructuralEquatable.Equals(object? other, Collections.IEqualityComparer comparer) =>
            other is ValueTuple<T1, T2, T3, T4, T5, T6, T7, TRest> tuple && TupleContract.StructuralEquals(comparer, Item1, tuple.Item1, Item2, tuple.Item2, Item3, tuple.Item3, Item4, tuple.Item4, Item5, tuple.Item5, Item6, tuple.Item6, Item7, tuple.Item7, Rest, tuple.Rest);
        int Collections.IStructuralEquatable.GetHashCode(Collections.IEqualityComparer comparer) =>
            TupleContract.StructuralHash(comparer, Item1, Item2, Item3, Item4, Item5, Item6, Item7, Rest);
    }
}
