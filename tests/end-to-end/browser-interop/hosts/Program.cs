using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices.JavaScript;
using System.Threading.Tasks;

public static class HostExports
{
    [JSImport("label", "app.labels")]
    private static extern string Label(int value);

    [JSExport("twice")]
    public static int Twice(int value) => value * 2;

    [JSExport("label")]
    public static string LabelValue(int value) => Label(value);

    [JSExport("sum")]
    public static long Sum(int[] values)
    {
        long total = 0;
        foreach (var value in values)
        {
            total += value;
        }
        return total;
    }

    [JSExport("later")]
    public static async Task<int> Later(string text)
    {
        await Task.Yield();
        return text.Length + DateTime.UtcNow.Year.ToString().Length;
    }

    // Issue #216: allocation paces collections, so calls that allocate nothing never collect,
    // even in a module whose objects have finalizers.
    [JSExport("collections")]
    public static int Collections() => GC.CollectionCount(0);

    [JSExport("finalizes")]
    public static int Finalizes()
    {
        Abandon();
        GC.Collect();
        GC.WaitForPendingFinalizers();
        return Finalizable.Finalized;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void Abandon() => _ = new Finalizable();

    private sealed class Finalizable
    {
        internal static int Finalized;

        ~Finalizable() => Finalized++;
    }
}
