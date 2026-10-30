// Adapted from dotnet/runtime System.Private.CoreLib delegate contracts.
// The upstream implementation is licensed under MIT.
// Copyright (c) .NET Foundation and Contributors.
namespace System
{
    public delegate void Action();
    public delegate void Action<in T>(T obj)
        where T : allows ref struct;
    public delegate void Action<in T1, in T2>(T1 arg1, T2 arg2)
        where T1 : allows ref struct
        where T2 : allows ref struct;
    public delegate void Action<in T1, in T2, in T3>(T1 arg1, T2 arg2, T3 arg3)
        where T1 : allows ref struct
        where T2 : allows ref struct
        where T3 : allows ref struct;
    public delegate void Action<in T1, in T2, in T3, in T4>(T1 arg1, T2 arg2, T3 arg3, T4 arg4)
        where T1 : allows ref struct
        where T2 : allows ref struct
        where T3 : allows ref struct
        where T4 : allows ref struct;
    public delegate void Action<in T1, in T2, in T3, in T4, in T5>(T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5)
        where T1 : allows ref struct
        where T2 : allows ref struct
        where T3 : allows ref struct
        where T4 : allows ref struct
        where T5 : allows ref struct;
    public delegate void Action<in T1, in T2, in T3, in T4, in T5, in T6>(T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6)
        where T1 : allows ref struct
        where T2 : allows ref struct
        where T3 : allows ref struct
        where T4 : allows ref struct
        where T5 : allows ref struct
        where T6 : allows ref struct;
    public delegate void Action<in T1, in T2, in T3, in T4, in T5, in T6, in T7>(T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6, T7 arg7)
        where T1 : allows ref struct
        where T2 : allows ref struct
        where T3 : allows ref struct
        where T4 : allows ref struct
        where T5 : allows ref struct
        where T6 : allows ref struct
        where T7 : allows ref struct;
    public delegate void Action<in T1, in T2, in T3, in T4, in T5, in T6, in T7, in T8>(T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6, T7 arg7, T8 arg8)
        where T1 : allows ref struct
        where T2 : allows ref struct
        where T3 : allows ref struct
        where T4 : allows ref struct
        where T5 : allows ref struct
        where T6 : allows ref struct
        where T7 : allows ref struct
        where T8 : allows ref struct;
    public delegate void Action<in T1, in T2, in T3, in T4, in T5, in T6, in T7, in T8, in T9>(T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6, T7 arg7, T8 arg8, T9 arg9)
        where T1 : allows ref struct
        where T2 : allows ref struct
        where T3 : allows ref struct
        where T4 : allows ref struct
        where T5 : allows ref struct
        where T6 : allows ref struct
        where T7 : allows ref struct
        where T8 : allows ref struct
        where T9 : allows ref struct;
    public delegate void Action<in T1, in T2, in T3, in T4, in T5, in T6, in T7, in T8, in T9, in T10>(T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6, T7 arg7, T8 arg8, T9 arg9, T10 arg10)
        where T1 : allows ref struct
        where T2 : allows ref struct
        where T3 : allows ref struct
        where T4 : allows ref struct
        where T5 : allows ref struct
        where T6 : allows ref struct
        where T7 : allows ref struct
        where T8 : allows ref struct
        where T9 : allows ref struct
        where T10 : allows ref struct;
    public delegate void Action<in T1, in T2, in T3, in T4, in T5, in T6, in T7, in T8, in T9, in T10, in T11>(T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6, T7 arg7, T8 arg8, T9 arg9, T10 arg10, T11 arg11)
        where T1 : allows ref struct
        where T2 : allows ref struct
        where T3 : allows ref struct
        where T4 : allows ref struct
        where T5 : allows ref struct
        where T6 : allows ref struct
        where T7 : allows ref struct
        where T8 : allows ref struct
        where T9 : allows ref struct
        where T10 : allows ref struct
        where T11 : allows ref struct;
    public delegate void Action<in T1, in T2, in T3, in T4, in T5, in T6, in T7, in T8, in T9, in T10, in T11, in T12>(T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6, T7 arg7, T8 arg8, T9 arg9, T10 arg10, T11 arg11, T12 arg12)
        where T1 : allows ref struct
        where T2 : allows ref struct
        where T3 : allows ref struct
        where T4 : allows ref struct
        where T5 : allows ref struct
        where T6 : allows ref struct
        where T7 : allows ref struct
        where T8 : allows ref struct
        where T9 : allows ref struct
        where T10 : allows ref struct
        where T11 : allows ref struct
        where T12 : allows ref struct;
    public delegate void Action<in T1, in T2, in T3, in T4, in T5, in T6, in T7, in T8, in T9, in T10, in T11, in T12, in T13>(T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6, T7 arg7, T8 arg8, T9 arg9, T10 arg10, T11 arg11, T12 arg12, T13 arg13)
        where T1 : allows ref struct
        where T2 : allows ref struct
        where T3 : allows ref struct
        where T4 : allows ref struct
        where T5 : allows ref struct
        where T6 : allows ref struct
        where T7 : allows ref struct
        where T8 : allows ref struct
        where T9 : allows ref struct
        where T10 : allows ref struct
        where T11 : allows ref struct
        where T12 : allows ref struct
        where T13 : allows ref struct;
    public delegate void Action<in T1, in T2, in T3, in T4, in T5, in T6, in T7, in T8, in T9, in T10, in T11, in T12, in T13, in T14>(T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6, T7 arg7, T8 arg8, T9 arg9, T10 arg10, T11 arg11, T12 arg12, T13 arg13, T14 arg14)
        where T1 : allows ref struct
        where T2 : allows ref struct
        where T3 : allows ref struct
        where T4 : allows ref struct
        where T5 : allows ref struct
        where T6 : allows ref struct
        where T7 : allows ref struct
        where T8 : allows ref struct
        where T9 : allows ref struct
        where T10 : allows ref struct
        where T11 : allows ref struct
        where T12 : allows ref struct
        where T13 : allows ref struct
        where T14 : allows ref struct;
    public delegate void Action<in T1, in T2, in T3, in T4, in T5, in T6, in T7, in T8, in T9, in T10, in T11, in T12, in T13, in T14, in T15>(T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6, T7 arg7, T8 arg8, T9 arg9, T10 arg10, T11 arg11, T12 arg12, T13 arg13, T14 arg14, T15 arg15)
        where T1 : allows ref struct
        where T2 : allows ref struct
        where T3 : allows ref struct
        where T4 : allows ref struct
        where T5 : allows ref struct
        where T6 : allows ref struct
        where T7 : allows ref struct
        where T8 : allows ref struct
        where T9 : allows ref struct
        where T10 : allows ref struct
        where T11 : allows ref struct
        where T12 : allows ref struct
        where T13 : allows ref struct
        where T14 : allows ref struct
        where T15 : allows ref struct;
    public delegate void Action<in T1, in T2, in T3, in T4, in T5, in T6, in T7, in T8, in T9, in T10, in T11, in T12, in T13, in T14, in T15, in T16>(T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6, T7 arg7, T8 arg8, T9 arg9, T10 arg10, T11 arg11, T12 arg12, T13 arg13, T14 arg14, T15 arg15, T16 arg16)
        where T1 : allows ref struct
        where T2 : allows ref struct
        where T3 : allows ref struct
        where T4 : allows ref struct
        where T5 : allows ref struct
        where T6 : allows ref struct
        where T7 : allows ref struct
        where T8 : allows ref struct
        where T9 : allows ref struct
        where T10 : allows ref struct
        where T11 : allows ref struct
        where T12 : allows ref struct
        where T13 : allows ref struct
        where T14 : allows ref struct
        where T15 : allows ref struct
        where T16 : allows ref struct;

    public delegate TResult Func<out TResult>()
        where TResult : allows ref struct;
    public delegate TResult Func<in T, out TResult>(T arg)
        where T : allows ref struct
        where TResult : allows ref struct;
    public delegate TResult Func<in T1, in T2, out TResult>(T1 arg1, T2 arg2)
        where T1 : allows ref struct
        where T2 : allows ref struct
        where TResult : allows ref struct;
    public delegate TResult Func<in T1, in T2, in T3, out TResult>(T1 arg1, T2 arg2, T3 arg3)
        where T1 : allows ref struct
        where T2 : allows ref struct
        where T3 : allows ref struct
        where TResult : allows ref struct;
    public delegate TResult Func<in T1, in T2, in T3, in T4, out TResult>(T1 arg1, T2 arg2, T3 arg3, T4 arg4)
        where T1 : allows ref struct
        where T2 : allows ref struct
        where T3 : allows ref struct
        where T4 : allows ref struct
        where TResult : allows ref struct;
    public delegate TResult Func<in T1, in T2, in T3, in T4, in T5, out TResult>(T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5)
        where T1 : allows ref struct
        where T2 : allows ref struct
        where T3 : allows ref struct
        where T4 : allows ref struct
        where T5 : allows ref struct
        where TResult : allows ref struct;
    public delegate TResult Func<in T1, in T2, in T3, in T4, in T5, in T6, out TResult>(T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6)
        where T1 : allows ref struct
        where T2 : allows ref struct
        where T3 : allows ref struct
        where T4 : allows ref struct
        where T5 : allows ref struct
        where T6 : allows ref struct
        where TResult : allows ref struct;
    public delegate TResult Func<in T1, in T2, in T3, in T4, in T5, in T6, in T7, out TResult>(T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6, T7 arg7)
        where T1 : allows ref struct
        where T2 : allows ref struct
        where T3 : allows ref struct
        where T4 : allows ref struct
        where T5 : allows ref struct
        where T6 : allows ref struct
        where T7 : allows ref struct
        where TResult : allows ref struct;
    public delegate TResult Func<in T1, in T2, in T3, in T4, in T5, in T6, in T7, in T8, out TResult>(T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6, T7 arg7, T8 arg8)
        where T1 : allows ref struct
        where T2 : allows ref struct
        where T3 : allows ref struct
        where T4 : allows ref struct
        where T5 : allows ref struct
        where T6 : allows ref struct
        where T7 : allows ref struct
        where T8 : allows ref struct
        where TResult : allows ref struct;
    public delegate TResult Func<in T1, in T2, in T3, in T4, in T5, in T6, in T7, in T8, in T9, out TResult>(T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6, T7 arg7, T8 arg8, T9 arg9)
        where T1 : allows ref struct
        where T2 : allows ref struct
        where T3 : allows ref struct
        where T4 : allows ref struct
        where T5 : allows ref struct
        where T6 : allows ref struct
        where T7 : allows ref struct
        where T8 : allows ref struct
        where T9 : allows ref struct
        where TResult : allows ref struct;
    public delegate TResult Func<in T1, in T2, in T3, in T4, in T5, in T6, in T7, in T8, in T9, in T10, out TResult>(T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6, T7 arg7, T8 arg8, T9 arg9, T10 arg10)
        where T1 : allows ref struct
        where T2 : allows ref struct
        where T3 : allows ref struct
        where T4 : allows ref struct
        where T5 : allows ref struct
        where T6 : allows ref struct
        where T7 : allows ref struct
        where T8 : allows ref struct
        where T9 : allows ref struct
        where T10 : allows ref struct
        where TResult : allows ref struct;
    public delegate TResult Func<in T1, in T2, in T3, in T4, in T5, in T6, in T7, in T8, in T9, in T10, in T11, out TResult>(T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6, T7 arg7, T8 arg8, T9 arg9, T10 arg10, T11 arg11)
        where T1 : allows ref struct
        where T2 : allows ref struct
        where T3 : allows ref struct
        where T4 : allows ref struct
        where T5 : allows ref struct
        where T6 : allows ref struct
        where T7 : allows ref struct
        where T8 : allows ref struct
        where T9 : allows ref struct
        where T10 : allows ref struct
        where T11 : allows ref struct
        where TResult : allows ref struct;
    public delegate TResult Func<in T1, in T2, in T3, in T4, in T5, in T6, in T7, in T8, in T9, in T10, in T11, in T12, out TResult>(T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6, T7 arg7, T8 arg8, T9 arg9, T10 arg10, T11 arg11, T12 arg12)
        where T1 : allows ref struct
        where T2 : allows ref struct
        where T3 : allows ref struct
        where T4 : allows ref struct
        where T5 : allows ref struct
        where T6 : allows ref struct
        where T7 : allows ref struct
        where T8 : allows ref struct
        where T9 : allows ref struct
        where T10 : allows ref struct
        where T11 : allows ref struct
        where T12 : allows ref struct
        where TResult : allows ref struct;
    public delegate TResult Func<in T1, in T2, in T3, in T4, in T5, in T6, in T7, in T8, in T9, in T10, in T11, in T12, in T13, out TResult>(T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6, T7 arg7, T8 arg8, T9 arg9, T10 arg10, T11 arg11, T12 arg12, T13 arg13)
        where T1 : allows ref struct
        where T2 : allows ref struct
        where T3 : allows ref struct
        where T4 : allows ref struct
        where T5 : allows ref struct
        where T6 : allows ref struct
        where T7 : allows ref struct
        where T8 : allows ref struct
        where T9 : allows ref struct
        where T10 : allows ref struct
        where T11 : allows ref struct
        where T12 : allows ref struct
        where T13 : allows ref struct
        where TResult : allows ref struct;
    public delegate TResult Func<in T1, in T2, in T3, in T4, in T5, in T6, in T7, in T8, in T9, in T10, in T11, in T12, in T13, in T14, out TResult>(T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6, T7 arg7, T8 arg8, T9 arg9, T10 arg10, T11 arg11, T12 arg12, T13 arg13, T14 arg14)
        where T1 : allows ref struct
        where T2 : allows ref struct
        where T3 : allows ref struct
        where T4 : allows ref struct
        where T5 : allows ref struct
        where T6 : allows ref struct
        where T7 : allows ref struct
        where T8 : allows ref struct
        where T9 : allows ref struct
        where T10 : allows ref struct
        where T11 : allows ref struct
        where T12 : allows ref struct
        where T13 : allows ref struct
        where T14 : allows ref struct
        where TResult : allows ref struct;
    public delegate TResult Func<in T1, in T2, in T3, in T4, in T5, in T6, in T7, in T8, in T9, in T10, in T11, in T12, in T13, in T14, in T15, out TResult>(T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6, T7 arg7, T8 arg8, T9 arg9, T10 arg10, T11 arg11, T12 arg12, T13 arg13, T14 arg14, T15 arg15)
        where T1 : allows ref struct
        where T2 : allows ref struct
        where T3 : allows ref struct
        where T4 : allows ref struct
        where T5 : allows ref struct
        where T6 : allows ref struct
        where T7 : allows ref struct
        where T8 : allows ref struct
        where T9 : allows ref struct
        where T10 : allows ref struct
        where T11 : allows ref struct
        where T12 : allows ref struct
        where T13 : allows ref struct
        where T14 : allows ref struct
        where T15 : allows ref struct
        where TResult : allows ref struct;
    public delegate TResult Func<in T1, in T2, in T3, in T4, in T5, in T6, in T7, in T8, in T9, in T10, in T11, in T12, in T13, in T14, in T15, in T16, out TResult>(T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6, T7 arg7, T8 arg8, T9 arg9, T10 arg10, T11 arg11, T12 arg12, T13 arg13, T14 arg14, T15 arg15, T16 arg16)
        where T1 : allows ref struct
        where T2 : allows ref struct
        where T3 : allows ref struct
        where T4 : allows ref struct
        where T5 : allows ref struct
        where T6 : allows ref struct
        where T7 : allows ref struct
        where T8 : allows ref struct
        where T9 : allows ref struct
        where T10 : allows ref struct
        where T11 : allows ref struct
        where T12 : allows ref struct
        where T13 : allows ref struct
        where T14 : allows ref struct
        where T15 : allows ref struct
        where T16 : allows ref struct
        where TResult : allows ref struct;

    public delegate bool Predicate<in T>(T obj)
        where T : allows ref struct;
    public delegate int Comparison<in T>(T x, T y)
        where T : allows ref struct;
    public delegate void EventHandler(object? sender, EventArgs e);
    // EventHandler<TEventArgs> intentionally has no EventArgs base-type
    // constraint, matching the current runtime contract.
    public delegate void EventHandler<in TEventArgs>(object? sender, TEventArgs e)
        where TEventArgs : allows ref struct;
    public delegate void EventHandler<in TSender, in TEventArgs>(TSender sender, TEventArgs e)
        where TSender : allows ref struct
        where TEventArgs : allows ref struct;

    public abstract class Delegate : ICloneable
    {
        // These fields are part of the compiler/runtime delegate ABI. A leaf
        // stores its target and method ID. A multicast delegate stores a flat
        // leaf array and the active prefix length.
        private object? _target = null;
        internal int MethodId = 0;
        private Delegate?[]? _invocationList = null;
        private int _invocationCount = 0;

        public object? Target
        {
            get => _invocationList is null
                ? _target
                : _invocationList[_invocationCount - 1]!.Target;
        }

        public virtual object Clone() =>
            DelegateRuntime.Clone(this) ?? throw new OutOfMemoryException();

        protected virtual object? DynamicInvokeImpl(object?[]? args) =>
            Runtime.CompilerServices.RuntimeMemberExecution.InvokeDelegate(
                GetType().GetDelegateInvokeMethod(),
                this,
                args);

        public object? DynamicInvoke(params object?[]? args) => DynamicInvokeImpl(args);

        public static Delegate? Combine(Delegate? left, Delegate? right)
        {
            if (left is null) return right;
            return left.CombineImpl(right);
        }

        public static Delegate? Combine(params Delegate?[]? delegates) =>
            Combine((ReadOnlySpan<Delegate?>)delegates);

        public static Delegate? Combine(params ReadOnlySpan<Delegate?> delegates)
        {
            Delegate? result = delegates.IsEmpty ? null : delegates[0];
            for (var index = 1; index < delegates.Length; index++)
            {
                result = Combine(result, delegates[index]);
            }
            return result;
        }

        protected virtual Delegate CombineImpl(Delegate? value)
        {
            if (value is null)
            {
                return this;
            }
            if (GetType() != value.GetType())
            {
                throw new ArgumentException();
            }

            var valueList = value._invocationList;
            var valueCount = valueList is null ? 1 : value._invocationCount;
            if (_invocationList is null)
            {
                var result = new Delegate?[1 + valueCount];
                result[0] = this;
                CopyInvocationList(value, result, 1);
                return NewMulticastDelegate(result, result.Length);
            }

            var resultCount = _invocationCount + valueCount;
            var resultList = _invocationList;
            if (resultCount <= resultList.Length &&
                TrySetInvocationList(resultList, _invocationCount, value))
            {
                return NewMulticastDelegate(resultList, resultCount);
            }

            var capacity = resultList.Length;
            while (capacity < resultCount)
            {
                capacity = checked(capacity * 2);
            }
            var copy = new Delegate?[capacity];
            Array.Copy(resultList, copy, _invocationCount);
            CopyInvocationList(value, copy, _invocationCount);
            return NewMulticastDelegate(copy, resultCount);
        }

        public static Delegate? Remove(Delegate? source, Delegate? value)
        {
            if (source is null) return null;
            if (value is null) return source;
            if (source.GetType() != value.GetType())
            {
                throw new ArgumentException();
            }
            return source.RemoveImpl(value);
        }

        protected virtual Delegate? RemoveImpl(Delegate? value)
        {
            if (value is null)
            {
                return this;
            }

            var valueList = value._invocationList;
            if (valueList is null)
            {
                if (_invocationList is null)
                {
                    return Equals(value) ? null : this;
                }

                for (var index = _invocationCount - 1; index >= 0; index--)
                {
                    if (!value.Equals(_invocationList[index]))
                    {
                        continue;
                    }
                    if (_invocationCount == 2)
                    {
                        return _invocationList[1 - index];
                    }
                    return NewMulticastDelegate(
                        DeleteFromInvocationList(_invocationList, index, 1),
                        _invocationCount - 1);
                }
                return this;
            }

            if (_invocationList is null)
            {
                return this;
            }

            var valueCount = value._invocationCount;
            for (var index = _invocationCount - valueCount; index >= 0; index--)
            {
                if (!EqualInvocationLists(_invocationList, valueList, index, valueCount))
                {
                    continue;
                }

                var remaining = _invocationCount - valueCount;
                if (remaining == 0)
                {
                    return null;
                }
                if (remaining == 1)
                {
                    return _invocationList[index != 0 ? 0 : _invocationCount - 1];
                }
                return NewMulticastDelegate(
                    DeleteFromInvocationList(_invocationList, index, valueCount),
                    remaining);
            }
            return this;
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
            if (_invocationList is null)
            {
                return [this];
            }

            var result = new Delegate[_invocationCount];
            for (var index = 0; index < result.Length; index++)
            {
                result[index] = _invocationList[index]!;
            }
            return result;
        }

        public bool HasSingleTarget
        {
            get => _invocationList is null;
        }

        internal object GetTargetForSingleCastInstanceDelegate() => _target!;

        public static bool operator ==(Delegate? left, Delegate? right)
        {
            if (right is null) return left is null;
            return ReferenceEquals(left, right) || right.Equals(left);
        }

        public static bool operator !=(Delegate? left, Delegate? right) => !(left == right);

        public override bool Equals(object? value)
        {
            if (value is not Delegate other || GetType() != other.GetType()) return false;
            if (ReferenceEquals(this, other)) return true;

            if (_invocationList is null)
            {
                return other._invocationList is null &&
                    MethodId == other.MethodId &&
                    ReferenceEquals(_target, other._target);
            }
            if (other._invocationList is null ||
                _invocationCount != other._invocationCount)
            {
                return false;
            }
            for (var index = 0; index < _invocationCount; index++)
            {
                if (!_invocationList[index]!.Equals(other._invocationList[index]))
                {
                    return false;
                }
            }
            return true;
        }

        public override int GetHashCode()
        {
            if (_invocationList is null)
            {
                return MethodId * 13 +
                    Runtime.CompilerServices.RuntimeHelpers.GetHashCode(_target) * 17;
            }

            var hash = 0;
            for (var index = 0; index < _invocationCount; index++)
            {
                hash = hash * 33 + _invocationList[index]!.GetHashCode();
            }
            return hash;
        }

        internal Delegate? TryGetAt(int index)
        {
            if (_invocationList is null) return index == 0 ? this : null;
            return (uint)index < (uint)_invocationCount
                ? _invocationList[index]
                : null;
        }

        public struct InvocationListEnumerator<TDelegate> where TDelegate : Delegate
        {
            private readonly Delegate? _delegate;
            private int _index;
            private TDelegate? _current;

            internal InvocationListEnumerator(TDelegate? value)
            {
                _delegate = value;
                _index = -1;
            }

            public TDelegate Current
            {
                get => _current!;
            }
            public bool MoveNext()
            {
                var index = _index + 1;
                _current = (TDelegate?)_delegate?.TryGetAt(index);
                if (_current is null) return false;
                _index = index;
                return true;
            }
            public InvocationListEnumerator<TDelegate> GetEnumerator() => this;
        }

        public static InvocationListEnumerator<TDelegate> EnumerateInvocationList<TDelegate>(TDelegate? value)
            where TDelegate : Delegate => new(value);

        private Delegate NewMulticastDelegate(Delegate?[] invocationList, int invocationCount)
        {
            var result = DelegateRuntime.Clone(this) ?? throw new OutOfMemoryException();
            result._target = null;
            result.MethodId = 0;
            result._invocationList = invocationList;
            result._invocationCount = invocationCount;
            return result;
        }

        private static void CopyInvocationList(
            Delegate value,
            Delegate?[] destination,
            int destinationIndex)
        {
            if (value._invocationList is null)
            {
                destination[destinationIndex] = value;
                return;
            }
            // One copy of the whole list: an element store into a Delegate[]
            // that is not known to be exactly that would test each element.
            Array.Copy(value._invocationList, 0, destination, destinationIndex, value._invocationCount);
        }

        private static bool TrySetInvocationList(
            Delegate?[] destination,
            int destinationIndex,
            Delegate value)
        {
            if (value._invocationList is null)
            {
                return TrySetSlot(destination, destinationIndex, value);
            }
            for (var index = 0; index < value._invocationCount; index++)
            {
                if (!TrySetSlot(
                    destination,
                    destinationIndex + index,
                    value._invocationList[index]!))
                {
                    return false;
                }
            }
            return true;
        }

        private static bool TrySetSlot(Delegate?[] destination, int index, Delegate value)
        {
            if (destination[index] is null)
            {
                destination[index] = value;
                return true;
            }
            return destination[index]!.Equals(value);
        }

        private Delegate?[] DeleteFromInvocationList(
            Delegate?[] invocationList,
            int deleteIndex,
            int deleteCount)
        {
            var capacity = invocationList.Length;
            while (capacity / 2 >= _invocationCount - deleteCount)
            {
                capacity /= 2;
            }
            var result = new Delegate?[capacity];
            for (var index = 0; index < deleteIndex; index++)
            {
                result[index] = invocationList[index];
            }
            for (var index = deleteIndex + deleteCount;
                 index < _invocationCount;
                 index++)
            {
                result[index - deleteCount] = invocationList[index];
            }
            return result;
        }

        private static bool EqualInvocationLists(
            Delegate?[] left,
            Delegate?[] right,
            int start,
            int count)
        {
            for (var index = 0; index < count; index++)
            {
                if (!left[start + index]!.Equals(right[index])) return false;
            }
            return true;
        }
    }

    internal static class DelegateRuntime
    {
        [Runtime.CompilerServices.MethodImpl(
            Runtime.CompilerServices.MethodImplOptions.InternalCall)]
        internal static extern Delegate? Clone(Delegate value);
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

    internal delegate TResult SpanFunc<TSpan, in T1, in T2, in T3, out TResult>(Span<TSpan> span, T1 arg1, T2 arg2, T3 arg3);
}
