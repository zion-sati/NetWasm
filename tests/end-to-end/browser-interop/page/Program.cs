using System;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.JavaScript;
using System.Threading;
using System.Threading.Tasks;

public static partial class PageExports
{
    private static int _counter;
    private static int _heard;
    private static JSSubscription? _listener;

    [JSImport("stamp", "app.bundled")]
    private static extern string Stamp(string value);

    // Nothing calls this, so trimming removes it and its module is left out.
    [JSImport("optionalFeature", "app.optional")]
    internal static extern string OptionalFeature();

    [JSImport("show", "app.host")]
    private static extern void Show(string text);

    [JSImport("shout", "app.host")]
    private static extern Task<string> Shout(string text);

    [JSImport("never", "app.host")]
    private static extern Task<int> Never();

    [JSImport("title", "app.host")]
    private static extern string Title(JSObject element);

    [JSImport("listen", "app.host")]
    private static extern JSSubscription Listen(Action<int> heard);

    // A callback takes and returns what a synchronous export does.
    [JSImport("label", "app.host")]
    private static extern JSSubscription OnLabel(Func<string, string, JSObject, string> label);

    [JSImport("reenter", "app.host")]
    private static extern int Reenter(int depth);

    [JSImport("fill", "app.host")]
    private static extern int Fill(Span<byte> pixels, byte value);

    [JSImport("sum", "app.host")]
    private static extern double Sum(ReadOnlySpan<double> values);

    [JSImport("count", "app.host")]
    private static extern int Count(Span<int> values);

    [JSImport("nextDay", "app.host")]
    private static extern DateTime NextDay(DateTime value);

    [JSImport("nextHour", "app.host")]
    private static extern DateTimeOffset NextHour(DateTimeOffset value, string label);

    [JSImport("dateLater", "app.host")]
    private static extern Task<DateTime> DateLater(DateTime value);

    [JSImport("shift", "app.host")]
    private static extern JSSubscription OnShift(Func<DateTime, DateTimeOffset> shift);

    [JSImport("half", "app.host")]
    private static extern double? Half(int? value);

    [JSImport("maybeLater", "app.host")]
    private static extern Task<long?> MaybeLater(bool? present);

    [JSImport("pick", "app.host")]
    private static extern JSSubscription OnPick(Func<int?, DateTime?, bool?> pick);

    [JSImport("nothing", "app.host")]
    private static extern int Nothing();

    // The form .NET's browser runtime declares imports in; the SDK implements it.
    [JSImport("echo", "app.host")]
    private static partial object? Echo(object? value);

    [JSImport("words", "app.host")]
    private static extern string?[]? Words(string?[]? values);

    [JSImport("raise", "app.host")]
    private static extern Exception? Raise(string message);

    [JSImport("collect", "app.host")]
    private static extern Task<object?[]?> Collect(JSObject?[]? elements);

    [JSImport("inspect", "app.host")]
    private static extern JSSubscription OnInspect(Func<object?, string?[]?, object?> inspect);

    [JSImport("bump", "app.host")]
    [return: JSMarshalAs<JSType.Number>]
    internal static partial long Bump([JSMarshalAs<JSType.Number>] long value);

    [JSExport("add")]
    public static int Add(int value) => _counter += value;

    [JSExport("echo")]
    public static string? Echo(string? value)
    {
        Show(value ?? "<null>");
        return value is null ? null : Stamp(value);
    }

    [JSExport("scale")]
    public static double[] Scale(double[] values, float factor)
    {
        var scaled = new double[values.Length];
        for (var index = 0; index < values.Length; index++)
        {
            scaled[index] = values[index] * factor;
        }
        return scaled;
    }

    // A span reaches JavaScript as a view over linear memory, so the page's
    // writes are the managed buffer's, wherever that buffer lives.
    [JSExport("fillPixels")]
    public static unsafe int FillPixels(int length, int value)
    {
        var pixels = new byte[length];
        var filled = Fill(pixels, (byte)value);
        foreach (var pixel in pixels)
        {
            if (pixel != value)
            {
                return -1;
            }
        }
        Span<byte> stack = stackalloc byte[4];
        var native = (byte*)NativeMemory.Alloc(8);
        try
        {
            filled += Fill(stack, 1) + Fill(new Span<byte>(native, 8), 2) + Fill(Span<byte>.Empty, 3);
            return stack[3] == 1 && native[7] == 2 ? filled : -2;
        }
        finally
        {
            NativeMemory.Free(native);
        }
    }

    [JSExport("sumMiddle")]
    public static double SumMiddle(double[] values) => Sum(values.AsSpan(1, values.Length - 2));

    // A typed array cannot start between elements, so a misaligned span fails the call.
    [JSExport("countInts")]
    public static int CountInts()
    {
        var aligned = Count(new int[3]);
        try
        {
            return aligned * 10 + Count(MemoryMarshal.Cast<byte, int>(new byte[16].AsSpan(1, 8)));
        }
        catch (JSException)
        {
            return aligned * 10 - 1;
        }
    }

    [JSExport("describe")]
    public static string Describe(JSObject element)
    {
        using (element)
        {
            return Title(element);
        }
    }

    [JSExport("same")]
    public static JSObject? Same(JSObject? value) => value;

    // The async import settles with a string.
    [JSExport("later")]
    public static async Task<int> Later(string text) => (await Shout(text) == text.ToUpperInvariant() ? 1000 : 0) + text.Length;

    // Issue #127: an async export settles with what a synchronous one returns.
    [JSExport("shoutLater")]
    public static async Task<string> ShoutLater(string text) => await Shout(text) + "!";

    [JSExport("scaleLater")]
    public static async ValueTask<double[]?> ScaleLater(double[]? values)
    {
        await Task.Yield();
        return values is null ? null : Scale(values, 2);
    }

    // Issue #126: C# stops waiting for a JavaScript promise with WaitAsync, as
    // JavaScript stops waiting for an export with { signal }.
    [JSExport("waitOrTimeOut")]
    public static async Task<int> WaitOrTimeOut(int milliseconds)
    {
        using var timeout = new CancellationTokenSource(milliseconds);
        try
        {
            return await Never().WaitAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            return -1;
        }
    }

    [JSExport("pendingLater")]
    public static Task<int> PendingLater() => new TaskCompletionSource<int>().Task;

    [JSExport("sameLater")]
    public static async Task<JSObject?> SameLater(JSObject? value)
    {
        await Task.Yield();
        return value;
    }

    [JSExport("startListening")]
    public static void StartListening() => _listener = Listen(value => _heard += value);

    [JSExport("heard")]
    public static int Heard() => _heard;

    [JSExport("labelThroughCallback")]
    public static string LabelThroughCallback()
    {
        string? seen = null;
        using (OnLabel((name, title, element) =>
        {
            using (element)
            {
                return seen = name + "/" + title + "/" + Title(element);
            }
        }))
        {
        }
        return seen ?? "not called";
    }

    [JSExport("stopListening")]
    public static void StopListening()
    {
        _listener?.Dispose();
        _listener = null;
    }

    // Each level calls back into this export through the page; past the
    // session's nesting limit the import throws and the caller sees -1.
    [JSExport("nest")]
    public static int Nest(int depth)
    {
        if (depth == 0)
        {
            return 0;
        }
        try
        {
            var inner = Reenter(depth - 1);
            return inner < 0 ? inner : inner + 1;
        }
        catch (JSException)
        {
            return -1;
        }
    }

    [JSExport("fail")]
    public static int Fail() => throw new FormatException("page-probe");

    [JSExport("failLater")]
    public static async Task<int> FailLater()
    {
        await Task.Yield();
        throw new FormatException("page-later");
    }

    [JSExport("now")]
    public static long Now() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

    // DateTime and DateTimeOffset cross as a JavaScript Date, in Unix milliseconds.
    [JSExport("addHours")]
    public static DateTime AddHours(DateTime value, int hours) => value.AddHours(hours);

    [JSExport("tomorrow")]
    public static DateTime Tomorrow(DateTime value) => NextDay(value);

    [JSExport("hourLater")]
    public static string HourLater(string label, DateTimeOffset value) =>
        NextHour(value, label).ToUnixTimeMilliseconds() + label;

    [JSExport("utcKind")]
    public static bool UtcKind(DateTime value) => value.Kind == DateTimeKind.Utc;

    // A DateTime's ticks cross as UTC whatever its Kind, in whole milliseconds.
    [JSExport("localNoon")]
    public static DateTime LocalNoon() => new DateTime(1970, 1, 2, 12, 0, 0, DateTimeKind.Local).AddTicks(9_999);

    [JSExport("laterDate")]
    public static async Task<DateTime> LaterDate(DateTime value) => (await DateLater(value)).AddMilliseconds(1);

    [JSExport("offsetLater")]
    public static async ValueTask<DateTimeOffset> OffsetLater(DateTimeOffset value)
    {
        await Task.Yield();
        return value.AddDays(1);
    }

    // A nullable scalar or date crosses as the value or null.
    [JSExport("maybeAdd")]
    public static int? MaybeAdd(int? left, int right) => left + right;

    [JSExport("halfOf")]
    public static double? HalfOf(int? value) => Half(value);

    [JSExport("maybeDay")]
    public static DateTime? MaybeDay(DateTime? value) => value?.AddDays(1);

    [JSExport("maybeLaterSum")]
    public static async Task<long?> MaybeLaterSum(bool? present) => await MaybeLater(present) + 1;

    [JSExport("maybeOffsetLater")]
    public static async ValueTask<DateTimeOffset?> MaybeOffsetLater(DateTimeOffset? value)
    {
        await Task.Yield();
        return value;
    }

    [JSExport("pickThroughCallback")]
    public static int PickThroughCallback()
    {
        var calls = 0;
        using (OnPick((value, date) =>
        {
            calls++;
            return value is null ? null : value > 3 && date is not null;
        }))
        {
        }
        return calls;
    }

    // An object crosses as any JavaScript value, as the type it has there.
    [JSExport("describeValue")]
    public static string DescribeValue(object? value) => value switch
    {
        null => "null",
        bool flag => "bool:" + flag,
        double number => "double:" + number,
        long number => "long:" + number,
        string text => "string:" + text,
        DateTime date => "date:" + date.Year,
        JSException error => "error:" + error.Message,
        object?[] values => "array:" + values.Length,
        byte[] bytes => "bytes:" + bytes.Length,
        JSObject => "object",
        _ => "other",
    };

    [JSExport("valueOf")]
    public static object? ValueOf(int kind) => kind switch
    {
        1 => true,
        2 => 2.5,
        3 => "text",
        4 => new DateTime(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc),
        5 => new object?[] { 1, "two", null },
        6 => new byte[] { 1, 2 },
        7 => new InvalidOperationException("managed"),
        8 => new Marker(),
        9 => 42L,
        10 => long.MaxValue,
        _ => null,
    };

    // A managed object JavaScript has no counterpart for comes back as itself.
    [JSExport("sameManaged")]
    public static bool SameManaged(object? value) => value is Marker;

    [JSExport("echoThroughHost")]
    public static object? EchoThroughHost(object? value) => Echo(value);

    [JSExport("upperWords")]
    public static string?[]? UpperWords(string?[]? values) => Words(values);

    [JSExport("wrapError")]
    public static string WrapError(string message) => Raise(message) is JSException error ? "js:" + error.Message : "none";

    // A JavaScript error comes back as itself.
    [JSExport("errorOf")]
    public static Exception? ErrorOf(Exception? error) => error;

    [JSExport("managedError")]
    public static Exception ManagedError() => new FormatException("managed-format");

    [JSExport("collectLater")]
    public static async Task<int> CollectLater(JSObject?[]? elements) => (await Collect(elements))?.Length ?? -1;

    [JSExport("elementsOf")]
    public static JSObject?[]? ElementsOf(JSObject?[]? elements) => elements;

    [JSExport("inspectThroughCallback")]
    public static int InspectThroughCallback()
    {
        var calls = 0;
        using (OnInspect((value, words) =>
        {
            calls++;
            return words is null ? value : words.Length;
        }))
        {
        }
        return calls;
    }

    // A long marked JSType.Number crosses as a number, exact within 2^53 - 1.
    [JSExport("bumpNumber")]
    [return: JSMarshalAs<JSType.Number>]
    public static long BumpNumber([JSMarshalAs<JSType.Number>] long value) => Bump(value);

    [JSExport("bigAsNumber")]
    [return: JSMarshalAs<JSType.Number>]
    public static long BigAsNumber() => long.MaxValue;

    [JSExport("bumpLater")]
    [return: JSMarshalAs<JSType.Promise<JSType.Number>>]
    public static async Task<long> BumpLater([JSMarshalAs<JSType.Number>] long? value)
    {
        await Task.Yield();
        return (value ?? -1) + 1;
    }

    private sealed class Marker;

    // A null where C# expects a plain scalar is its default.
    [JSExport("nothingAsZero")]
    public static int NothingAsZero() => Nothing();

    [JSExport("shiftThroughCallback")]
    public static DateTime ShiftThroughCallback()
    {
        var seen = DateTime.MinValue;
        using (OnShift(value =>
        {
            seen = value;
            return new DateTimeOffset(value.AddSeconds(1));
        }))
        {
        }
        return seen;
    }

    [JSExport("variable")]
    public static string? Variable(string name) => Environment.GetEnvironmentVariable(name);

    [JSExport("print")]
    public static void Print(string text) => Console.WriteLine(text);
}
