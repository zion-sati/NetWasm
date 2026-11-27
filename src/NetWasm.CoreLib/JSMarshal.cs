// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: MIT

namespace System.Runtime.InteropServices.JavaScript
{
    // The conversions a JavaScript boundary applies to the values it cannot pass
    // as they are, as Mono's marshaller does. The compiler calls each one, named
    // To<Type> into managed code and From<Type> out of it, only for a boundary that
    // passes its type that way.
    //
    // A date crosses as whole Unix milliseconds, a DateTime's ticks taken as UTC
    // whatever its Kind, and a DateTime from JavaScript is UTC. An object, an
    // exception or an array of references crosses as a host handle to its
    // JavaScript value, 0 for null; whoever receives a handle releases it.
    //
    // A conversion that takes a struct takes it by reference, and one that gives one writes it through a reference its
    // first parameter names: the boundaries hold such a value at an address, so each conversion's Wasm signature is that
    // address however managed calls pass a struct.
    internal static class JSMarshal
    {
        private const long MaxSafeInteger = 9007199254740991;

        internal static double FromDateTime(in DateTime value) =>
            new DateTimeOffset(value.Ticks, TimeSpan.Zero).ToUnixTimeMilliseconds();

        internal static void ToDateTime(out DateTime value, double milliseconds) => value = Date(milliseconds);

        private static DateTime Date(double milliseconds) =>
            DateTimeOffset.FromUnixTimeMilliseconds((long)milliseconds).UtcDateTime;

        internal static double FromDateTimeOffset(in DateTimeOffset value) =>
            value.ToUnixTimeMilliseconds();

        // An ArraySegment crosses as .NET's MemoryView: JavaScript reads and writes the array through the view until it
        // disposes or collects it, and the view keeps the array alive until then. The kind is the element type, in the
        // order the host's value_from_memory_view lists them; a segment without an array is null.
        internal static int FromByteSegment(in ArraySegment<byte> segment) => FromSegment(segment, 0);

        internal static int FromSByteSegment(in ArraySegment<sbyte> segment) => FromSegment(segment, 1);

        internal static int FromInt16Segment(in ArraySegment<short> segment) => FromSegment(segment, 2);

        internal static int FromUInt16Segment(in ArraySegment<ushort> segment) => FromSegment(segment, 3);

        internal static int FromInt32Segment(in ArraySegment<int> segment) => FromSegment(segment, 4);

        internal static int FromUInt32Segment(in ArraySegment<uint> segment) => FromSegment(segment, 5);

        internal static int FromInt64Segment(in ArraySegment<long> segment) => FromSegment(segment, 6);

        internal static int FromUInt64Segment(in ArraySegment<ulong> segment) => FromSegment(segment, 7);

        internal static int FromSingleSegment(in ArraySegment<float> segment) => FromSegment(segment, 8);

        internal static int FromDoubleSegment(in ArraySegment<double> segment) => FromSegment(segment, 9);

        // The garbage collector does not move an array, so its elements' address holds while the view keeps it alive.
        private static unsafe int FromSegment<T>(ArraySegment<T> segment, int kind)
            where T : unmanaged
        {
            if (segment.Array is not { } array)
            {
                return 0;
            }
            var address = (nuint)System.Runtime.CompilerServices.Unsafe.AsPointer(
                ref System.Runtime.CompilerServices.Unsafe.Add(ref MemoryMarshal.GetArrayDataReference(array), segment.Offset));
            return JSHostValue.FromMemoryView(Pin(array), address, segment.Count, kind);
        }

        internal static void ToDateTimeOffset(out DateTimeOffset value, double milliseconds) =>
            value = DateTimeOffset.FromUnixTimeMilliseconds((long)milliseconds);

        // A value of a type JavaScript has no counterpart for crosses as an opaque
        // object that gives the same value back.
        internal static int FromObject(object? value) => value switch
        {
            null => 0,
            string text => JSHostValue.FromString(text),
            bool flag => JSHostValue.FromBoolean(flag),
            double number => JSHostValue.FromNumber(number),
            float number => JSHostValue.FromNumber(number),
            int number => JSHostValue.FromNumber(number),
            uint number => JSHostValue.FromNumber(number),
            short number => JSHostValue.FromNumber(number),
            ushort number => JSHostValue.FromNumber(number),
            byte number => JSHostValue.FromNumber(number),
            sbyte number => JSHostValue.FromNumber(number),
            nint number => JSHostValue.FromNumber(number),
            long number => JSHostValue.FromNumber(SafeInteger(number)),
            ulong number => JSHostValue.FromNumber(SafeInteger(number)),
            char character => JSHostValue.FromString(character.ToString()),
            DateTime date => JSHostValue.FromDate(FromDateTime(date)),
            DateTimeOffset date => JSHostValue.FromDate(FromDateTimeOffset(date)),
            JSObject jsObject => JSHostValue.FromObject(jsObject),
            Exception exception => FromException(exception),
            byte[] bytes => JSHostValue.FromBytes(bytes),
            int[] numbers => JSHostValue.FromInt32s(numbers),
            double[] numbers => JSHostValue.FromDoubles(numbers),
            object?[] values => FromArray(values),
            _ => JSHostValue.FromManaged(Pin(value)),
        };

        internal static object? ToObject(int handle)
        {
            if (handle == 0)
            {
                return null;
            }
            return JSHostValue.Kind(handle) switch
            {
                JSValueKind.Null => null,
                JSValueKind.Boolean => JSHostValue.Number(handle) != 0,
                JSValueKind.Number => JSHostValue.Number(handle),
                JSValueKind.BigInt => JSHostValue.BigInt(handle),
                JSValueKind.String => JSHostValue.String(handle),
                JSValueKind.Date => Date(JSHostValue.Number(handle)),
                JSValueKind.Error or JSValueKind.ManagedError => ToException(handle),
                JSValueKind.Array => ToArray(handle),
                JSValueKind.ManagedObject => GCHandle.FromIntPtr((nint)JSHostValue.Managed(handle)).Target,
                JSValueKind.Bytes => JSHostValue.Bytes(handle),
                JSValueKind.Int32s => JSHostValue.Int32s(handle),
                JSValueKind.Doubles => JSHostValue.Doubles(handle),
                _ => JSHostValue.Object(handle),
            };
        }

        // An export's catch, synchronous or async, gives its exception here: a JSException holding a JavaScript error offers it to the
        // host, which throws it instead of a managed failure (JavaScriptConversion.ExportFailure).
        internal static void OfferExportFailure(Exception exception) => JSHostError.OfferExportFailure(exception);

        // A JavaScript error comes back as itself; any other exception crosses as
        // an Error carrying its message, which gives the exception back.
        internal static int FromException(Exception? exception) => exception switch
        {
            null => 0,
            JSException { Error: not 0 } error => JSHostValue.Copy(error.Error),
            _ => JSHostValue.FromManagedError(exception.Message, Pin(exception)),
        };

        internal static Exception? ToException(int handle)
        {
            if (handle == 0)
            {
                return null;
            }
            return JSHostValue.Kind(handle) switch
            {
                JSValueKind.Null => null,
                JSValueKind.ManagedError => (Exception)GCHandle.FromIntPtr((nint)JSHostValue.Managed(handle)).Target!,
                _ => new JSException(JSHostValue.Message(handle), JSHostValue.Copy(handle)),
            };
        }

        internal static int FromStrings(string?[]? values)
        {
            if (values is null)
            {
                return 0;
            }
            var array = JSHostValue.NewArray(values.Length);
            for (var index = 0; index < values.Length; index++)
            {
                JSHostValue.SetElement(array, index, values[index] is { } value ? JSHostValue.FromString(value) : 0);
            }
            return array;
        }

        internal static string?[]? ToStrings(int handle)
        {
            if (handle == 0)
            {
                return null;
            }
            var values = new string?[JSHostValue.Length(handle)];
            for (var index = 0; index < values.Length; index++)
            {
                var element = JSHostValue.Element(handle, index);
                if (element != 0)
                {
                    values[index] = JSHostValue.String(element);
                    JSHostValue.Release(element);
                }
            }
            return values;
        }

        internal static int FromObjects(JSObject?[]? values)
        {
            if (values is null)
            {
                return 0;
            }
            var array = JSHostValue.NewArray(values.Length);
            for (var index = 0; index < values.Length; index++)
            {
                JSHostValue.SetElement(array, index, values[index] is { } value ? JSHostValue.FromObject(value) : 0);
            }
            return array;
        }

        internal static JSObject?[]? ToObjects(int handle)
        {
            if (handle == 0)
            {
                return null;
            }
            var values = new JSObject?[JSHostValue.Length(handle)];
            for (var index = 0; index < values.Length; index++)
            {
                var element = JSHostValue.Element(handle, index);
                if (element != 0)
                {
                    values[index] = JSHostValue.Object(element);
                    JSHostValue.Release(element);
                }
            }
            return values;
        }

        internal static int FromArray(object?[]? values)
        {
            if (values is null)
            {
                return 0;
            }
            var array = JSHostValue.NewArray(values.Length);
            for (var index = 0; index < values.Length; index++)
            {
                JSHostValue.SetElement(array, index, FromObject(values[index]));
            }
            return array;
        }

        internal static object?[]? ToArray(int handle)
        {
            if (handle == 0)
            {
                return null;
            }
            var values = new object?[JSHostValue.Length(handle)];
            for (var index = 0; index < values.Length; index++)
            {
                var element = JSHostValue.Element(handle, index);
                values[index] = ToObject(element);
                if (element != 0)
                {
                    JSHostValue.Release(element);
                }
            }
            return values;
        }

        // A JavaScript number holds an integer exactly up to 2^53 - 1, as Mono's
        // Int52 marshalling requires.
        private static double SafeInteger(long value) =>
            value is >= -MaxSafeInteger and <= MaxSafeInteger
                ? value
                : throw new OverflowException("The value is outside the range JavaScript numbers hold exactly.");

        private static double SafeInteger(ulong value) =>
            value <= MaxSafeInteger
                ? value
                : throw new OverflowException("The value is outside the range JavaScript numbers hold exactly.");

        // The handle keeps the value alive while JavaScript holds it; the runtime
        // reports each one JavaScript has collected, and it is freed here.
        private static long Pin(object value)
        {
            for (var collected = JSHostValue.Collected(); collected != 0; collected = JSHostValue.Collected())
            {
                GCHandle.FromIntPtr((nint)collected).Free();
            }
            return GCHandle.ToIntPtr(GCHandle.Alloc(value));
        }
    }

    // JavaScript values the runtime holds for managed code, each behind a host
    // handle, served by the runtime's own import module.
    internal static class JSHostValue
    {
        private const string Module = "netwasm.host.v1";

        [JSImport("value_kind", Module)]
        internal static extern int Kind(int handle);

        [JSImport("value_number", Module)]
        internal static extern double Number(int handle);

        [JSImport("value_bigint", Module)]
        internal static extern long BigInt(int handle);

        [JSImport("value_string", Module)]
        internal static extern string String(int handle);

        [JSImport("value_message", Module)]
        internal static extern string Message(int handle);

        [JSImport("value_bytes", Module)]
        internal static extern byte[] Bytes(int handle);

        [JSImport("value_int32s", Module)]
        internal static extern int[] Int32s(int handle);

        [JSImport("value_doubles", Module)]
        internal static extern double[] Doubles(int handle);

        [JSImport("value_object", Module)]
        internal static extern JSObject Object(int handle);

        [JSImport("value_length", Module)]
        internal static extern int Length(int handle);

        [JSImport("value_element", Module)]
        internal static extern int Element(int handle, int index);

        [JSImport("value_managed", Module)]
        internal static extern long Managed(int handle);

        [JSImport("value_copy", Module)]
        internal static extern int Copy(int handle);

        [JSImport("value_release", Module)]
        internal static extern void Release(int handle);

        [JSImport("value_from_number", Module)]
        internal static extern int FromNumber(double value);

        [JSImport("value_from_boolean", Module)]
        internal static extern int FromBoolean(bool value);

        [JSImport("value_from_date", Module)]
        internal static extern int FromDate(double milliseconds);

        [JSImport("value_from_string", Module)]
        internal static extern int FromString(string value);

        [JSImport("value_from_bytes", Module)]
        internal static extern int FromBytes(byte[] value);

        [JSImport("value_from_int32s", Module)]
        internal static extern int FromInt32s(int[] value);

        [JSImport("value_from_doubles", Module)]
        internal static extern int FromDoubles(double[] value);

        [JSImport("value_from_object", Module)]
        internal static extern int FromObject(JSObject value);

        [JSImport("value_new_array", Module)]
        internal static extern int NewArray(int length);

        // Takes the element's handle.
        [JSImport("value_set_element", Module)]
        internal static extern void SetElement(int array, int index, int element);

        [JSImport("value_from_managed", Module)]
        internal static extern int FromManaged(long handle);

        [JSImport("value_from_managed_error", Module)]
        internal static extern int FromManagedError(string message, long handle);

        [JSImport("value_from_memory_view", Module)]
        internal static extern int FromMemoryView(long handle, nuint address, int length, int kind);

        // A collected managed object's handle, or 0.
        [JSImport("value_collected", Module)]
        internal static extern long Collected();
    }

    // Owns a host handle and releases it once collected, or when its owner is done with it. The handle is released
    // once: after Release the finalizer finds none, since the host may already have given the number to another value.
    internal sealed class JSHandleOwner(int handle)
    {
        private int _handle = handle;

        internal int Handle => _handle;

        internal void Release()
        {
            var released = _handle;
            _handle = 0;
            JSHostValue.Release(released);
        }

        ~JSHandleOwner()
        {
            if (_handle != 0)
            {
                JSHostValue.Release(_handle);
            }
        }
    }

    // The JavaScript error a failed import threw or rejected with, which the host holds until the import's JSException
    // takes it. Served by the runtime's own import module.
    internal static class JSHostError
    {
        private const string Module = "netwasm.host.v1";

        // The pending error's message, or null when no JavaScript error is pending.
        [JSImport("error_pending_message", Module)]
        internal static extern string? PendingMessage();

        // The pending error's handle, which the caller owns; the host holds no error afterwards.
        [JSImport("error_take", Module)]
        internal static extern int Take();

        // The JavaScript stack of an error a JSException holds, or null where it has none.
        [JSImport("error_stack", Module)]
        internal static extern string? Stack(int handle);

        // The managed GC handle of the pending error when it is a managed exception that crossed to JavaScript, else 0.
        [JSImport("error_pending_managed", Module)]
        internal static extern long PendingManaged();

        // Whether two error handles hold the same JavaScript value.
        [JSImport("error_same", Module)]
        internal static extern bool Same(int first, int second);

        // Gives the host the JavaScript error an async export's Promise rejects with; the host takes the handle.
        [JSImport("error_offer_export", Module)]
        internal static extern void OfferExport(int error);

        // The exception a failed import raises, as .NET's browser runtime marshals what it threw or rejected with
        // (marshal_exception_to_cs): a managed exception that crossed to JavaScript comes back as itself; any other
        // value becomes a JSException whose message is the value's toString() and which keeps the value. A failure
        // with no JavaScript value behind it raises a plain JSException.
        internal static Exception TakeFailure()
        {
            if (PendingMessage() is not { } message)
            {
                return new JSException();
            }
            var managed = PendingManaged();
            var error = Take();
            if (managed == 0)
            {
                return new JSException(message, error);
            }
            JSHostValue.Release(error);
            return (Exception)GCHandle.FromIntPtr((nint)managed).Target!;
        }

        // As .NET's ToJS(Exception): a JSException holding a JavaScript error, alone or as an AggregateException's only
        // exception, gives that error back, so an async export's Promise rejects with it.
        internal static void OfferExportFailure(Exception exception)
        {
            if (exception is AggregateException { InnerExceptions.Count: 1 } aggregate)
            {
                exception = aggregate.InnerExceptions[0];
            }
            if (exception is JSException { Error: not 0 } error)
            {
                OfferExport(JSHostValue.Copy(error.Error));
            }
        }
    }

    // What value_kind reports a JavaScript value is.
    internal static class JSValueKind
    {
        internal const int Null = 0;
        internal const int Boolean = 1;
        internal const int Number = 2;
        internal const int BigInt = 3;
        internal const int String = 4;
        internal const int Date = 5;
        internal const int Error = 6;
        internal const int Array = 7;
        internal const int ManagedObject = 8;
        internal const int ManagedError = 9;
        internal const int Bytes = 10;
        internal const int Int32s = 11;
        internal const int Doubles = 12;
        internal const int Object = 13;
    }
}
