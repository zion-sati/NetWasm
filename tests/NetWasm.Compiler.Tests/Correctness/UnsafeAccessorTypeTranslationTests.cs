namespace NetWasm.Compiler.Tests.Correctness;

// UnsafeAccessorTypeAttribute: an accessor reaches a type it cannot name by declaring the position object and
// naming the type in the attribute, as CoreLib's AppDomain reaches System.Security.Claims' GenericPrincipal.
public sealed class UnsafeAccessorTypeTranslationTests(CorrectnessTestRunner runner)
{
    [Fact]
    public void TranslatedPositionsBindTheNamedTypeAndAMissingTypeFailsToLoad() =>
        runner.Run(new("UnsafeAccessorTranslation", "UnsafeAccessorTranslation", Source, [0, 1, 2, 3, 4])
        {
            ExpectedReturnValue = 42,
            Matrix = new(CorpusMatrixProfile.Extended, Backend: CorpusExecutionBackend.Simulated),
        });

    internal const string Source = """
        using System;
        using System.Runtime.CompilerServices;
        namespace UnsafeAccessorTranslation;

        internal sealed class Hidden
        {
            private readonly int value;
            private Hidden(int value) => this.value = value;
            private int Read(int offset) => value + offset;
            private static string Describe(Hidden hidden) => "hidden" + hidden.value;
            private int count = 41;
            public int Total => count + value;
        }
        public static class EntryPoint
        {
            public static int Trace() => 0;
            public static int Run(int input)
            {
                try
                {
                    object hidden = Create(40);
                    switch (input)
                    {
                        case 0: return Read(hidden, 2);
                        case 1: return Describe(null, hidden) == "hidden40" ? 42 : 1;
                        case 2: return Count(hidden) + 1;
                        case 3: return Missing(null, hidden) == null ? 2 : 3;
                        case 4: return Create(2) is { } other && Read(other, 40) == 42 ? 42 : 4;
                    }
                }
                catch (Exception exception)
                {
                    return input == 3 && exception is TypeLoadException ? 42 : -1;
                }
                return -2;
            }
            [UnsafeAccessor(UnsafeAccessorKind.Constructor)]
            [return: UnsafeAccessorType("UnsafeAccessorTranslation.Hidden")]
            private static extern object Create(int value);
            [UnsafeAccessor(UnsafeAccessorKind.Method, Name = "Read")]
            private static extern int Read([UnsafeAccessorType("UnsafeAccessorTranslation.Hidden")] object hidden, int offset);
            [UnsafeAccessor(UnsafeAccessorKind.StaticMethod, Name = "Describe")]
            private static extern string Describe(
                [UnsafeAccessorType("UnsafeAccessorTranslation.Hidden")] object? owner,
                [UnsafeAccessorType("UnsafeAccessorTranslation.Hidden")] object hidden);
            [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "count")]
            private static extern ref int Count([UnsafeAccessorType("UnsafeAccessorTranslation.Hidden")] object hidden);
            [UnsafeAccessor(UnsafeAccessorKind.StaticMethod, Name = "Describe")]
            private static extern string Missing(
                [UnsafeAccessorType("UnsafeAccessorTranslation.Missing")] object? owner,
                [UnsafeAccessorType("UnsafeAccessorTranslation.Hidden")] object hidden);
        }
        """;
}
