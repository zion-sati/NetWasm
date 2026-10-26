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
    internal static class JSMarshal
    {
        private const long MaxSafeInteger = 9007199254740991;

        internal static double FromDateTime(DateTime value) =>
            new DateTimeOffset(value.Ticks, TimeSpan.Zero).ToUnixTimeMilliseconds();

        internal static DateTime ToDateTime(double milliseconds) =>
            DateTimeOffset.FromUnixTimeMilliseconds((long)milliseconds).UtcDateTime;

        internal static double FromDateTimeOffset(DateTimeOffset value) =>
            value.ToUnixTimeMilliseconds();

        internal static DateTimeOffset ToDateTimeOffset(double milliseconds) =>
            DateTimeOffset.FromUnixTimeMilliseconds((long)milliseconds);

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
                JSValueKind.Date => ToDateTime(JSHostValue.Number(handle)),
                JSValueKind.Error or JSValueKind.ManagedError => ToException(handle),
                JSValueKind.Array => ToArray(handle),
                JSValueKind.ManagedObject => GCHandle.FromIntPtr((nint)JSHostValue.Managed(handle)).Target,
                JSValueKind.Bytes => JSHostValue.Bytes(handle),
                JSValueKind.Int32s => JSHostValue.Int32s(handle),
                JSValueKind.Doubles => JSHostValue.Doubles(handle),
                _ => JSHostValue.Object(handle),
            };
        }

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

        // A collected managed object's handle, or 0.
        [JSImport("value_collected", Module)]
        internal static extern long Collected();
    }

    // Owns a host handle and releases it once collected.
    internal sealed class JSHandleOwner(int handle)
    {
        internal int Handle { get; } = handle;

        ~JSHandleOwner() => JSHostValue.Release(Handle);
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
