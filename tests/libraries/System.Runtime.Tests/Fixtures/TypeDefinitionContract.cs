// From the compiler harness's TypeDefinitionContractCompilationTests.cs: the same program, compiled once with the suite. On desktop .NET 11 it
// builds with DESKTOP_ORACLE defined.
#nullable disable
#pragma warning disable
using System;
namespace TypeDefinitionFixture;
public static class EntryPoint
{
    public static unsafe int Run(int input)
    {
        foreach (var type in new Type[] { typeof(void), typeof(int), typeof(string), typeof(object), typeof(Box<>), typeof(EntryPoint), typeof(IDisposable), typeof(DayOfWeek) })
            if (!type.IsTypeDefinition) return 1;
        foreach (var type in new Type[] { typeof(Box<int>), typeof(int[]), typeof(int[,]), typeof(int*), typeof(Box<>).GetGenericArguments()[0] })
            if (type.IsTypeDefinition) return 2;
        return input + 1;
    }
    private class Box<T> { }
}
