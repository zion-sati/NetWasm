using System;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.JavaScript;
using System.Threading.Tasks;

public static class PageExports
{
    private static int _counter;
    private static int _heard;
    private static JSSubscription? _listener;

    [JSImport("stamp", "app.bundled")]
    private static extern string Stamp(string value);

    [JSImport("show", "app.host")]
    private static extern void Show(string text);

    [JSImport("shout", "app.host")]
    private static extern Task<string> Shout(string text);

    [JSImport("title", "app.host")]
    private static extern string Title(JSObject element);

    [JSImport("listen", "app.host")]
    private static extern JSSubscription Listen(Action<int> heard);

    [JSImport("reenter", "app.host")]
    private static extern int Reenter(int depth);

    [JSImport("fill", "app.host")]
    private static extern int Fill(Span<byte> pixels, byte value);

    [JSImport("sum", "app.host")]
    private static extern double Sum(ReadOnlySpan<double> values);

    [JSImport("count", "app.host")]
    private static extern int Count(Span<int> values);

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

    // Async export results stay scalar; the async import settles with a string.
    [JSExport("later")]
    public static async Task<int> Later(string text) => (await Shout(text) == text.ToUpperInvariant() ? 1000 : 0) + text.Length;

    [JSExport("startListening")]
    public static void StartListening() => _listener = Listen(value => _heard += value);

    [JSExport("heard")]
    public static int Heard() => _heard;

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

    [JSExport("variable")]
    public static string? Variable(string name) => Environment.GetEnvironmentVariable(name);

    [JSExport("print")]
    public static void Print(string text) => Console.WriteLine(text);
}
