using System;
using System.Runtime.InteropServices.JavaScript;

// A worker the page references, so the page publishes it into its own
// deployment and both load one shared runtime (issue #128).
public static partial class PageWorkerExports
{
    [JSExport("square")]
    public static int Square(int value) => value * value;

    [JSExport("dayAfter")]
    public static DateTime DayAfter(DateTime value) => value.AddDays(1);

    [JSExport("maybeSquare")]
    public static int? MaybeSquare(int? value) => value * value;

    [JSExport("countWords")]
    public static int CountWords(string?[]? words, object? extra) => (words?.Length ?? -1) + (extra is string ? 1 : 0);
}
