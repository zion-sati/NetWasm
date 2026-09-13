// Adapted from dotnet/runtime System.Private.CoreLib delegate contracts.
// The upstream implementation is licensed under MIT.
// Copyright (c) .NET Foundation and Contributors.
namespace System
{
    public delegate void Action();
    public delegate void Action<in T>(T argument);
    public delegate void Action<in T1, in T2>(T1 argument1, T2 argument2);
    public delegate void Action<in T1, in T2, in T3>(T1 argument1, T2 argument2, T3 argument3);
    public delegate void Action<in T1, in T2, in T3, in T4>(T1 argument1, T2 argument2, T3 argument3, T4 argument4);
    public delegate void Action<in T1, in T2, in T3, in T4, in T5>(T1 argument1, T2 argument2, T3 argument3, T4 argument4, T5 argument5);
    public delegate void Action<in T1, in T2, in T3, in T4, in T5, in T6>(T1 argument1, T2 argument2, T3 argument3, T4 argument4, T5 argument5, T6 argument6);
    public delegate void Action<in T1, in T2, in T3, in T4, in T5, in T6, in T7>(T1 argument1, T2 argument2, T3 argument3, T4 argument4, T5 argument5, T6 argument6, T7 argument7);
    public delegate void Action<in T1, in T2, in T3, in T4, in T5, in T6, in T7, in T8>(T1 argument1, T2 argument2, T3 argument3, T4 argument4, T5 argument5, T6 argument6, T7 argument7, T8 argument8);
    public delegate void Action<in T1, in T2, in T3, in T4, in T5, in T6, in T7, in T8, in T9>(T1 argument1, T2 argument2, T3 argument3, T4 argument4, T5 argument5, T6 argument6, T7 argument7, T8 argument8, T9 argument9);
    public delegate void Action<in T1, in T2, in T3, in T4, in T5, in T6, in T7, in T8, in T9, in T10>(T1 argument1, T2 argument2, T3 argument3, T4 argument4, T5 argument5, T6 argument6, T7 argument7, T8 argument8, T9 argument9, T10 argument10);
    public delegate void Action<in T1, in T2, in T3, in T4, in T5, in T6, in T7, in T8, in T9, in T10, in T11>(T1 argument1, T2 argument2, T3 argument3, T4 argument4, T5 argument5, T6 argument6, T7 argument7, T8 argument8, T9 argument9, T10 argument10, T11 argument11);
    public delegate void Action<in T1, in T2, in T3, in T4, in T5, in T6, in T7, in T8, in T9, in T10, in T11, in T12>(T1 argument1, T2 argument2, T3 argument3, T4 argument4, T5 argument5, T6 argument6, T7 argument7, T8 argument8, T9 argument9, T10 argument10, T11 argument11, T12 argument12);
    public delegate void Action<in T1, in T2, in T3, in T4, in T5, in T6, in T7, in T8, in T9, in T10, in T11, in T12, in T13>(T1 argument1, T2 argument2, T3 argument3, T4 argument4, T5 argument5, T6 argument6, T7 argument7, T8 argument8, T9 argument9, T10 argument10, T11 argument11, T12 argument12, T13 argument13);
    public delegate void Action<in T1, in T2, in T3, in T4, in T5, in T6, in T7, in T8, in T9, in T10, in T11, in T12, in T13, in T14>(T1 argument1, T2 argument2, T3 argument3, T4 argument4, T5 argument5, T6 argument6, T7 argument7, T8 argument8, T9 argument9, T10 argument10, T11 argument11, T12 argument12, T13 argument13, T14 argument14);
    public delegate void Action<in T1, in T2, in T3, in T4, in T5, in T6, in T7, in T8, in T9, in T10, in T11, in T12, in T13, in T14, in T15>(T1 argument1, T2 argument2, T3 argument3, T4 argument4, T5 argument5, T6 argument6, T7 argument7, T8 argument8, T9 argument9, T10 argument10, T11 argument11, T12 argument12, T13 argument13, T14 argument14, T15 argument15);
    public delegate void Action<in T1, in T2, in T3, in T4, in T5, in T6, in T7, in T8, in T9, in T10, in T11, in T12, in T13, in T14, in T15, in T16>(T1 argument1, T2 argument2, T3 argument3, T4 argument4, T5 argument5, T6 argument6, T7 argument7, T8 argument8, T9 argument9, T10 argument10, T11 argument11, T12 argument12, T13 argument13, T14 argument14, T15 argument15, T16 argument16);

    public delegate TResult Func<out TResult>();
    public delegate TResult Func<in T, out TResult>(T argument);
    public delegate TResult Func<in T1, in T2, out TResult>(T1 argument1, T2 argument2);
    public delegate TResult Func<in T1, in T2, in T3, out TResult>(T1 argument1, T2 argument2, T3 argument3);
    public delegate TResult Func<in T1, in T2, in T3, in T4, out TResult>(T1 argument1, T2 argument2, T3 argument3, T4 argument4);
    public delegate TResult Func<in T1, in T2, in T3, in T4, in T5, out TResult>(T1 argument1, T2 argument2, T3 argument3, T4 argument4, T5 argument5);
    public delegate TResult Func<in T1, in T2, in T3, in T4, in T5, in T6, out TResult>(T1 argument1, T2 argument2, T3 argument3, T4 argument4, T5 argument5, T6 argument6);
    public delegate TResult Func<in T1, in T2, in T3, in T4, in T5, in T6, in T7, out TResult>(T1 argument1, T2 argument2, T3 argument3, T4 argument4, T5 argument5, T6 argument6, T7 argument7);
    public delegate TResult Func<in T1, in T2, in T3, in T4, in T5, in T6, in T7, in T8, out TResult>(T1 argument1, T2 argument2, T3 argument3, T4 argument4, T5 argument5, T6 argument6, T7 argument7, T8 argument8);
    public delegate TResult Func<in T1, in T2, in T3, in T4, in T5, in T6, in T7, in T8, in T9, out TResult>(T1 argument1, T2 argument2, T3 argument3, T4 argument4, T5 argument5, T6 argument6, T7 argument7, T8 argument8, T9 argument9);
    public delegate TResult Func<in T1, in T2, in T3, in T4, in T5, in T6, in T7, in T8, in T9, in T10, out TResult>(T1 argument1, T2 argument2, T3 argument3, T4 argument4, T5 argument5, T6 argument6, T7 argument7, T8 argument8, T9 argument9, T10 argument10);
    public delegate TResult Func<in T1, in T2, in T3, in T4, in T5, in T6, in T7, in T8, in T9, in T10, in T11, out TResult>(T1 argument1, T2 argument2, T3 argument3, T4 argument4, T5 argument5, T6 argument6, T7 argument7, T8 argument8, T9 argument9, T10 argument10, T11 argument11);
    public delegate TResult Func<in T1, in T2, in T3, in T4, in T5, in T6, in T7, in T8, in T9, in T10, in T11, in T12, out TResult>(T1 argument1, T2 argument2, T3 argument3, T4 argument4, T5 argument5, T6 argument6, T7 argument7, T8 argument8, T9 argument9, T10 argument10, T11 argument11, T12 argument12);
    public delegate TResult Func<in T1, in T2, in T3, in T4, in T5, in T6, in T7, in T8, in T9, in T10, in T11, in T12, in T13, out TResult>(T1 argument1, T2 argument2, T3 argument3, T4 argument4, T5 argument5, T6 argument6, T7 argument7, T8 argument8, T9 argument9, T10 argument10, T11 argument11, T12 argument12, T13 argument13);
    public delegate TResult Func<in T1, in T2, in T3, in T4, in T5, in T6, in T7, in T8, in T9, in T10, in T11, in T12, in T13, in T14, out TResult>(T1 argument1, T2 argument2, T3 argument3, T4 argument4, T5 argument5, T6 argument6, T7 argument7, T8 argument8, T9 argument9, T10 argument10, T11 argument11, T12 argument12, T13 argument13, T14 argument14);
    public delegate TResult Func<in T1, in T2, in T3, in T4, in T5, in T6, in T7, in T8, in T9, in T10, in T11, in T12, in T13, in T14, in T15, out TResult>(T1 argument1, T2 argument2, T3 argument3, T4 argument4, T5 argument5, T6 argument6, T7 argument7, T8 argument8, T9 argument9, T10 argument10, T11 argument11, T12 argument12, T13 argument13, T14 argument14, T15 argument15);
    public delegate TResult Func<in T1, in T2, in T3, in T4, in T5, in T6, in T7, in T8, in T9, in T10, in T11, in T12, in T13, in T14, in T15, in T16, out TResult>(T1 argument1, T2 argument2, T3 argument3, T4 argument4, T5 argument5, T6 argument6, T7 argument7, T8 argument8, T9 argument9, T10 argument10, T11 argument11, T12 argument12, T13 argument13, T14 argument14, T15 argument15, T16 argument16);

    public delegate bool Predicate<in T>(T value);
    public delegate int Comparison<in T>(T left, T right);
    public delegate void EventHandler(object? sender, EventArgs e);
    // EventHandler<TEventArgs> is intentionally unconstrained, matching the
    // current runtime contract used by Progress<T> and other generic events.
    public delegate void EventHandler<in TEventArgs>(object? sender, TEventArgs e);
    public delegate void EventHandler<in TSender, in TEventArgs>(TSender sender, TEventArgs e) where TEventArgs : EventArgs;

    public abstract class Delegate
    {
        // These fields are part of the compiler/runtime delegate ABI. A leaf
        // delegate stores Target and MethodId; a combined delegate stores Left
        // and Right. No stack or reflection state is retained here.
        private object? _target = null;
        internal int MethodId = 0;
        internal Delegate? Left = null;
        internal Delegate? Right = null;

        public object? Target
        {
            get => _target;
        }

        public virtual object Clone() => this;

        public static Delegate? Combine(Delegate? left, Delegate? right)
        {
            if (left is null) return right;
            if (right is null) return left;
            // The compiler's DelegateCombine lowering owns composite delegate
            // allocation. Calls reaching this managed fallback have bypassed
            // that lowering and must fail rather than silently losing a target.
            throw new MulticastNotSupportedException();
        }

        public static Delegate? Combine(params Delegate?[]? delegates)
        {
            if (delegates is null) return null;
            Delegate? result = null;
            for (var index = 0; index < delegates.Length; index++) result = Combine(result, delegates[index]);
            return result;
        }

        public static Delegate? Combine(params ReadOnlySpan<Delegate?> delegates)
        {
            Delegate? result = null;
            for (var index = 0; index < delegates.Length; index++)
            {
                result = Combine(result, delegates[index]);
            }
            return result;
        }

        public static Delegate? Remove(Delegate? source, Delegate? value)
        {
            if (source is null || value is null) return source;
            var sourceCount = InvocationCount(source);
            var valueCount = InvocationCount(value);
            if (valueCount > sourceCount) return source;
            var sourceItems = new Delegate[sourceCount];
            var valueItems = new Delegate[valueCount];
            Flatten(source, sourceItems, 0);
            Flatten(value, valueItems, 0);
            for (var start = sourceCount - valueCount; start >= 0; start--)
            {
                var match = true;
                for (var offset = 0; offset < valueCount; offset++)
                    match &= sourceItems[start + offset] == valueItems[offset];
                if (!match) continue;
                Delegate? result = null;
                for (var index = 0; index < start; index++) result = Combine(result, sourceItems[index]);
                for (var index = start + valueCount; index < sourceCount; index++) result = Combine(result, sourceItems[index]);
                return result;
            }
            return source;
        }

        public static Delegate? RemoveAll(Delegate? source, Delegate? value)
        {
            Delegate? previous;
            do
            {
                previous = source;
                source = Remove(source, value);
            } while (source != previous);
            return source;
        }

        public Delegate[] GetInvocationList()
        {
            var result = new Delegate[InvocationCount(this)];
            Flatten(this, result, 0);
            return result;
        }

        public bool HasSingleTarget
        {
            get => Left is null && Right is null;
        }

        public static bool operator ==(Delegate? left, Delegate? right)
        {
            if (right is null) return left is null;
            return ReferenceEquals(left, right) || right.Equals(left);
        }

        public static bool operator !=(Delegate? left, Delegate? right) => !(left == right);

        public override bool Equals(object? value)
        {
            if (value is not Delegate other) return false;
            if (ReferenceEquals(this, other)) return true;
            var leftItems = GetInvocationList();
            var rightItems = other.GetInvocationList();
            if (leftItems.Length != rightItems.Length) return false;
            for (var index = 0; index < leftItems.Length; index++)
            {
                if (leftItems[index].MethodId != rightItems[index].MethodId ||
                    !object.Equals(leftItems[index]._target, rightItems[index]._target))
                {
                    return false;
                }
            }
            return true;
        }

        public override int GetHashCode()
        {
            if (Left is null) return MethodId * 31 + (_target?.GetHashCode() ?? 0);
            var hash = 17;
            var invocationList = GetInvocationList();
            for (var index = 0; index < invocationList.Length; index++)
                hash = hash * 31 + invocationList[index].GetHashCode();
            return hash;
        }

        internal static int InvocationCount(Delegate value) =>
            value.Left is null ? 1 : InvocationCount(value.Left) + InvocationCount(value.Right!);

        private static int Flatten(Delegate value, Delegate[] destination, int index)
        {
            if (value.Left is null)
            {
                destination[index] = value;
                return index + 1;
            }
            index = Flatten(value.Left, destination, index);
            return Flatten(value.Right!, destination, index);
        }

        public struct InvocationListEnumerator<TDelegate> where TDelegate : Delegate
        {
            private readonly Delegate[] _items;
            private int _index;

            internal InvocationListEnumerator(TDelegate? value)
            {
                _items = value is null ? Array.Empty<Delegate>() : value.GetInvocationList();
                _index = -1;
            }

            public TDelegate Current
            {
                get => (TDelegate)_items[_index];
            }
            public bool MoveNext() => ++_index < _items.Length;
            public InvocationListEnumerator<TDelegate> GetEnumerator() => this;
        }

        public static InvocationListEnumerator<TDelegate> EnumerateInvocationList<TDelegate>(TDelegate? value)
            where TDelegate : Delegate => new(value);
    }

    public abstract class MulticastDelegate : Delegate
    {
        public sealed override bool Equals(object? value) => base.Equals(value);
        public sealed override int GetHashCode() => base.GetHashCode();
        public static bool operator ==(MulticastDelegate? left, MulticastDelegate? right) =>
            (Delegate?)left == right;
        public static bool operator !=(MulticastDelegate? left, MulticastDelegate? right) =>
            (Delegate?)left != right;
    }
}

namespace System.Buffers
{
    public delegate void SpanAction<T, in TArg>(Span<T> span, TArg arg)
        where TArg : allows ref struct;
    public delegate void ReadOnlySpanAction<T, in TArg>(ReadOnlySpan<T> span, TArg arg)
        where TArg : allows ref struct;
}
