// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using System.Diagnostics;

public static class Program
{
    private static int _evaluations;

    public static int Main()
    {
        Console.WriteLine(true);
        Console.WriteLine(false);
        Debug.WriteLine(Evaluate());
        Debug.WriteLine((object?)null);
        Debug.WriteLine(new Value(), "category");
        Debug.WriteLine("message", "category");
        Debug.WriteLine("message", (string?)null);
        Debug.WriteLine((object)new Value(), (string?)null);
        Debug.WriteLine("{0}/{1}", [42, true]);
        Debug.WriteLine((string?)null);
        Debug.WriteLine("λ");
#if DEBUG
        if (_evaluations != 1) return 1;
        try
        {
            Debug.WriteLine("{1}", [42]);
            return 2;
        }
        catch (FormatException) { }
        try
        {
            Debug.WriteLine(null!, [42]);
            return 3;
        }
        catch (ArgumentNullException) { }
#else
        if (_evaluations != 0) return 4;
#endif
        Console.WriteLine(42);
        return 0;
    }

    private static string Evaluate()
    {
        _evaluations++;
        return "debug";
    }

    private sealed class Value
    {
        public override string ToString() => "value";
    }
}
