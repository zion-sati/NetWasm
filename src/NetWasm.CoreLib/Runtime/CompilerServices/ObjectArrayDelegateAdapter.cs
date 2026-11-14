namespace System.Runtime.CompilerServices
{
    internal abstract class ObjectArrayDelegateTarget
    {
        internal abstract object? InvokeCore(object?[] arguments);

        // A compile-time-known delegate type whose Invoke takes N parameters binds to InvokeN (or InvokeVoidN for a
        // void result) instantiated with its parameter and result types; the compiler builds the delegate.

        internal TResult Invoke0<TResult>() => (TResult)InvokeCore(Array.Empty<object?>())!;

        internal void InvokeVoid0() => InvokeCore(Array.Empty<object?>());

        internal TResult Invoke1<T0, TResult>(T0 argument0) => (TResult)InvokeCore(new object?[] { argument0 })!;

        internal void InvokeVoid1<T0>(T0 argument0) => InvokeCore(new object?[] { argument0 });

        internal TResult Invoke2<T0, T1, TResult>(T0 argument0, T1 argument1) => (TResult)InvokeCore(new object?[] { argument0, argument1 })!;

        internal void InvokeVoid2<T0, T1>(T0 argument0, T1 argument1) => InvokeCore(new object?[] { argument0, argument1 });

        internal TResult Invoke3<T0, T1, T2, TResult>(T0 argument0, T1 argument1, T2 argument2) => (TResult)InvokeCore(new object?[] { argument0, argument1, argument2 })!;

        internal void InvokeVoid3<T0, T1, T2>(T0 argument0, T1 argument1, T2 argument2) => InvokeCore(new object?[] { argument0, argument1, argument2 });

        internal TResult Invoke4<T0, T1, T2, T3, TResult>(T0 argument0, T1 argument1, T2 argument2, T3 argument3) => (TResult)InvokeCore(new object?[] { argument0, argument1, argument2, argument3 })!;

        internal void InvokeVoid4<T0, T1, T2, T3>(T0 argument0, T1 argument1, T2 argument2, T3 argument3) => InvokeCore(new object?[] { argument0, argument1, argument2, argument3 });

        internal TResult Invoke5<T0, T1, T2, T3, T4, TResult>(T0 argument0, T1 argument1, T2 argument2, T3 argument3, T4 argument4) => (TResult)InvokeCore(new object?[] { argument0, argument1, argument2, argument3, argument4 })!;

        internal void InvokeVoid5<T0, T1, T2, T3, T4>(T0 argument0, T1 argument1, T2 argument2, T3 argument3, T4 argument4) => InvokeCore(new object?[] { argument0, argument1, argument2, argument3, argument4 });

        internal TResult Invoke6<T0, T1, T2, T3, T4, T5, TResult>(T0 argument0, T1 argument1, T2 argument2, T3 argument3, T4 argument4, T5 argument5) => (TResult)InvokeCore(new object?[] { argument0, argument1, argument2, argument3, argument4, argument5 })!;

        internal void InvokeVoid6<T0, T1, T2, T3, T4, T5>(T0 argument0, T1 argument1, T2 argument2, T3 argument3, T4 argument4, T5 argument5) => InvokeCore(new object?[] { argument0, argument1, argument2, argument3, argument4, argument5 });

        internal TResult Invoke7<T0, T1, T2, T3, T4, T5, T6, TResult>(T0 argument0, T1 argument1, T2 argument2, T3 argument3, T4 argument4, T5 argument5, T6 argument6) => (TResult)InvokeCore(new object?[] { argument0, argument1, argument2, argument3, argument4, argument5, argument6 })!;

        internal void InvokeVoid7<T0, T1, T2, T3, T4, T5, T6>(T0 argument0, T1 argument1, T2 argument2, T3 argument3, T4 argument4, T5 argument5, T6 argument6) => InvokeCore(new object?[] { argument0, argument1, argument2, argument3, argument4, argument5, argument6 });

        internal TResult Invoke8<T0, T1, T2, T3, T4, T5, T6, T7, TResult>(T0 argument0, T1 argument1, T2 argument2, T3 argument3, T4 argument4, T5 argument5, T6 argument6, T7 argument7) => (TResult)InvokeCore(new object?[] { argument0, argument1, argument2, argument3, argument4, argument5, argument6, argument7 })!;

        internal void InvokeVoid8<T0, T1, T2, T3, T4, T5, T6, T7>(T0 argument0, T1 argument1, T2 argument2, T3 argument3, T4 argument4, T5 argument5, T6 argument6, T7 argument7) => InvokeCore(new object?[] { argument0, argument1, argument2, argument3, argument4, argument5, argument6, argument7 });

        internal TResult Invoke9<T0, T1, T2, T3, T4, T5, T6, T7, T8, TResult>(T0 argument0, T1 argument1, T2 argument2, T3 argument3, T4 argument4, T5 argument5, T6 argument6, T7 argument7, T8 argument8) => (TResult)InvokeCore(new object?[] { argument0, argument1, argument2, argument3, argument4, argument5, argument6, argument7, argument8 })!;

        internal void InvokeVoid9<T0, T1, T2, T3, T4, T5, T6, T7, T8>(T0 argument0, T1 argument1, T2 argument2, T3 argument3, T4 argument4, T5 argument5, T6 argument6, T7 argument7, T8 argument8) => InvokeCore(new object?[] { argument0, argument1, argument2, argument3, argument4, argument5, argument6, argument7, argument8 });

        internal TResult Invoke10<T0, T1, T2, T3, T4, T5, T6, T7, T8, T9, TResult>(T0 argument0, T1 argument1, T2 argument2, T3 argument3, T4 argument4, T5 argument5, T6 argument6, T7 argument7, T8 argument8, T9 argument9) => (TResult)InvokeCore(new object?[] { argument0, argument1, argument2, argument3, argument4, argument5, argument6, argument7, argument8, argument9 })!;

        internal void InvokeVoid10<T0, T1, T2, T3, T4, T5, T6, T7, T8, T9>(T0 argument0, T1 argument1, T2 argument2, T3 argument3, T4 argument4, T5 argument5, T6 argument6, T7 argument7, T8 argument8, T9 argument9) => InvokeCore(new object?[] { argument0, argument1, argument2, argument3, argument4, argument5, argument6, argument7, argument8, argument9 });

        internal TResult Invoke11<T0, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, TResult>(T0 argument0, T1 argument1, T2 argument2, T3 argument3, T4 argument4, T5 argument5, T6 argument6, T7 argument7, T8 argument8, T9 argument9, T10 argument10) => (TResult)InvokeCore(new object?[] { argument0, argument1, argument2, argument3, argument4, argument5, argument6, argument7, argument8, argument9, argument10 })!;

        internal void InvokeVoid11<T0, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10>(T0 argument0, T1 argument1, T2 argument2, T3 argument3, T4 argument4, T5 argument5, T6 argument6, T7 argument7, T8 argument8, T9 argument9, T10 argument10) => InvokeCore(new object?[] { argument0, argument1, argument2, argument3, argument4, argument5, argument6, argument7, argument8, argument9, argument10 });

        internal TResult Invoke12<T0, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, TResult>(T0 argument0, T1 argument1, T2 argument2, T3 argument3, T4 argument4, T5 argument5, T6 argument6, T7 argument7, T8 argument8, T9 argument9, T10 argument10, T11 argument11) => (TResult)InvokeCore(new object?[] { argument0, argument1, argument2, argument3, argument4, argument5, argument6, argument7, argument8, argument9, argument10, argument11 })!;

        internal void InvokeVoid12<T0, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11>(T0 argument0, T1 argument1, T2 argument2, T3 argument3, T4 argument4, T5 argument5, T6 argument6, T7 argument7, T8 argument8, T9 argument9, T10 argument10, T11 argument11) => InvokeCore(new object?[] { argument0, argument1, argument2, argument3, argument4, argument5, argument6, argument7, argument8, argument9, argument10, argument11 });

        internal TResult Invoke13<T0, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, TResult>(T0 argument0, T1 argument1, T2 argument2, T3 argument3, T4 argument4, T5 argument5, T6 argument6, T7 argument7, T8 argument8, T9 argument9, T10 argument10, T11 argument11, T12 argument12) => (TResult)InvokeCore(new object?[] { argument0, argument1, argument2, argument3, argument4, argument5, argument6, argument7, argument8, argument9, argument10, argument11, argument12 })!;

        internal void InvokeVoid13<T0, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12>(T0 argument0, T1 argument1, T2 argument2, T3 argument3, T4 argument4, T5 argument5, T6 argument6, T7 argument7, T8 argument8, T9 argument9, T10 argument10, T11 argument11, T12 argument12) => InvokeCore(new object?[] { argument0, argument1, argument2, argument3, argument4, argument5, argument6, argument7, argument8, argument9, argument10, argument11, argument12 });

        internal TResult Invoke14<T0, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, TResult>(T0 argument0, T1 argument1, T2 argument2, T3 argument3, T4 argument4, T5 argument5, T6 argument6, T7 argument7, T8 argument8, T9 argument9, T10 argument10, T11 argument11, T12 argument12, T13 argument13) => (TResult)InvokeCore(new object?[] { argument0, argument1, argument2, argument3, argument4, argument5, argument6, argument7, argument8, argument9, argument10, argument11, argument12, argument13 })!;

        internal void InvokeVoid14<T0, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13>(T0 argument0, T1 argument1, T2 argument2, T3 argument3, T4 argument4, T5 argument5, T6 argument6, T7 argument7, T8 argument8, T9 argument9, T10 argument10, T11 argument11, T12 argument12, T13 argument13) => InvokeCore(new object?[] { argument0, argument1, argument2, argument3, argument4, argument5, argument6, argument7, argument8, argument9, argument10, argument11, argument12, argument13 });

        internal TResult Invoke15<T0, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, TResult>(T0 argument0, T1 argument1, T2 argument2, T3 argument3, T4 argument4, T5 argument5, T6 argument6, T7 argument7, T8 argument8, T9 argument9, T10 argument10, T11 argument11, T12 argument12, T13 argument13, T14 argument14) => (TResult)InvokeCore(new object?[] { argument0, argument1, argument2, argument3, argument4, argument5, argument6, argument7, argument8, argument9, argument10, argument11, argument12, argument13, argument14 })!;

        internal void InvokeVoid15<T0, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14>(T0 argument0, T1 argument1, T2 argument2, T3 argument3, T4 argument4, T5 argument5, T6 argument6, T7 argument7, T8 argument8, T9 argument9, T10 argument10, T11 argument11, T12 argument12, T13 argument13, T14 argument14) => InvokeCore(new object?[] { argument0, argument1, argument2, argument3, argument4, argument5, argument6, argument7, argument8, argument9, argument10, argument11, argument12, argument13, argument14 });

        internal TResult Invoke16<T0, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15, TResult>(T0 argument0, T1 argument1, T2 argument2, T3 argument3, T4 argument4, T5 argument5, T6 argument6, T7 argument7, T8 argument8, T9 argument9, T10 argument10, T11 argument11, T12 argument12, T13 argument13, T14 argument14, T15 argument15) => (TResult)InvokeCore(new object?[] { argument0, argument1, argument2, argument3, argument4, argument5, argument6, argument7, argument8, argument9, argument10, argument11, argument12, argument13, argument14, argument15 })!;

        internal void InvokeVoid16<T0, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15>(T0 argument0, T1 argument1, T2 argument2, T3 argument3, T4 argument4, T5 argument5, T6 argument6, T7 argument7, T8 argument8, T9 argument9, T10 argument10, T11 argument11, T12 argument12, T13 argument13, T14 argument14, T15 argument15) => InvokeCore(new object?[] { argument0, argument1, argument2, argument3, argument4, argument5, argument6, argument7, argument8, argument9, argument10, argument11, argument12, argument13, argument14, argument15 });
    }

    internal static class ObjectArrayDelegateAdapter
    {
        internal static TDelegate Create<TDelegate>(ObjectArrayDelegateTarget target) =>
            throw new InvalidOperationException(
                "The NetWasm compiler did not lower the object-array delegate adapter.");

        internal static object ThrowUnsupported() =>
            throw new NotSupportedException(
                "Only delegates of up to 16 parameters without by-reference parameters or result are executable.");

        internal static object ThrowInvalidDelegate() =>
            throw new ArgumentException("The lambda type must be a delegate type.");
    }
}
