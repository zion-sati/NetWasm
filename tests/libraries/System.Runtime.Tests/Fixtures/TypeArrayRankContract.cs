// From the compiler harness's TypeArrayRankContractCompilationTests.cs: the same program, compiled once with the suite. On desktop .NET 11 it
// builds with DESKTOP_ORACLE defined.
#nullable disable
#pragma warning disable
using System;
namespace TypeArrayRankFixture;
public static class EntryPoint
{
    public static int Run(int input)
    {
        if (typeof(int[]).GetArrayRank() != 1 || typeof(int[,]).GetArrayRank() != 2 || typeof(string[,,]).GetArrayRank() != 3) return 1;
        if (typeof(int[][]).GetArrayRank() != 1 || typeof(Box<int>[,]).GetArrayRank() != 2) return 2;
        var caught = 0;
        foreach (var type in new Type[] { typeof(int), typeof(string), typeof(Box<>), typeof(Box<int>), typeof(Box<>).GetGenericArguments()[0] })
            try { type.GetArrayRank(); } catch (ArgumentException) { caught++; }
        return caught == 5 ? input + 1 : 3;
    }
    private class Box<T> { }
}
