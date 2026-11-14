// From the compiler harness's TypeGenericParameterContractCompilationTests.cs: the same program, compiled once with the suite. On desktop .NET 11 it
// builds with DESKTOP_ORACLE defined.
#nullable disable
#pragma warning disable
using System;
namespace TypeGenericParameterFixture;
public static class EntryPoint
{
    public static int Run(int input)
    {
        var parameters = typeof(Pair<,>).GetGenericArguments();
        if (parameters.Length != 2 || parameters[0] == parameters[1]) return 1;
        foreach (var parameter in parameters)
            if (!parameter.IsGenericParameter || !parameter.IsGenericTypeParameter || parameter.IsGenericMethodParameter || !parameter.ContainsGenericParameters) return 2;
        Type[] others = { typeof(int), typeof(string), typeof(Pair<,>), typeof(Pair<int, string>), typeof(int[]) };
        foreach (var type in others)
            if (type.IsGenericParameter || type.IsGenericTypeParameter || type.IsGenericMethodParameter) return 3;
        foreach (var argument in typeof(Pair<int, string>).GetGenericArguments())
            if (argument.IsGenericParameter) return 4;
        return input + 1;
    }
    private class Pair<T, U> { }
}
