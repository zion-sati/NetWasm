// From the compiler harness's ConsoleValueContractCompilationTests.cs: the same program, compiled once with the suite. On desktop .NET 11 it
// builds with DESKTOP_ORACLE defined.
#nullable disable
#pragma warning disable
using System;
namespace ConsoleValueFixture;
public static class EntryPoint
{
    public static int Run(int input)
    {
        for (var bits = 0; bits < 8; bits++)
        {
            var key = new ConsoleKeyInfo('X', ConsoleKey.X, (bits & 1) != 0, (bits & 2) != 0, (bits & 4) != 0);
            var expected = ((bits & 1) != 0 ? ConsoleModifiers.Shift : 0) |
                ((bits & 2) != 0 ? ConsoleModifiers.Alt : 0) | ((bits & 4) != 0 ? ConsoleModifiers.Control : 0);
            if (key.KeyChar != 'X' || key.Key != ConsoleKey.X || key.Modifiers != expected) return 1;
            var copy = key;
            if (!key.Equals(copy) || !key.Equals((object)copy) || key != copy || !(key == copy) || key.GetHashCode() != copy.GetHashCode()) return 2;
            if (key.Equals(null) || key.Equals("X") || key == new ConsoleKeyInfo('Y', ConsoleKey.X, false, false, false)) return 3;
        }
        for (var value = 0; value <= 255; value++)
            if ((int)new ConsoleKeyInfo('\0', (ConsoleKey)value, false, false, false).Key != value) return 4;
        var caught = 0;
        try { new ConsoleKeyInfo('x', (ConsoleKey)(-1), false, false, false); } catch (ArgumentOutOfRangeException e) { if (e.ParamName == "key") caught++; }
        try { new ConsoleKeyInfo('x', (ConsoleKey)256, false, false, false); } catch (ArgumentOutOfRangeException e) { if (e.ParamName == "key") caught++; }
        var empty = default(ConsoleKeyInfo);
        if (empty.KeyChar != '\0' || empty.Modifiers != 0 || (int)empty.Key != 0) return 5;
        if ((int)ConsoleColor.White != 15 || (int)ConsoleSpecialKey.ControlBreak != 1) return 6;
        ConsoleCancelEventHandler handler = (_, args) => { if (args == null) caught++; };
        handler(null, null);
        return caught == 3 ? input + 1 : 7;
    }
}
