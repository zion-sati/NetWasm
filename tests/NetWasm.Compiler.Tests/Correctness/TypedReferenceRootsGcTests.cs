namespace NetWasm.Compiler.Tests.Correctness;

// A TypedReference holds an interior pointer: to an array element, or to a field of an object, that nothing else may
// keep alive. With a collection before every allocation, the reference must keep that object alive while code
// allocates, and its reads, writes and ToObject must still see it.
public sealed class TypedReferenceRootsGcTests(CorrectnessTestRunner runner)
{
    [Fact]
    public void TypedReferencesKeepWhatTheyReferenceAlive() =>
        runner.Run(new("TypedReferenceRoots", "TypedReferenceRoots", Source, [0, 1, 2])
        {
            ReportAllMismatches = true,
            RequiredRuntimeCapabilities = OracleRuntimeCapabilities.GarbageCollection,
            CollectsAtEveryAllocation = true,
            Matrix = new(CorpusMatrixProfile.Extended, Backend: CorpusExecutionBackend.Linked),
        });

    private const string Source = """
        using System;
        using System.Runtime.CompilerServices;
        namespace TypedReferenceRoots;

        public sealed class Box
        {
            public string Text = "box";
            public int[] Numbers = new int[3];
        }

        public static class EntryPoint
        {
            public static int Trace() => 0;

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static string[] Make(int n)
            {
                var items = new string[3];
                for (var i = 0; i < items.Length; i++) items[i] = "item" + (n + i);
                return items;
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static string Churn(int n)
            {
                var text = "";
                for (var i = 0; i < 48; i++) text = text + (char)('a' + (i + n) % 26);
                return text;
            }

            public static int Run(int input)
            {
                string observed;
                switch (input)
                {
                    case 0:
                    {
                        TypedReference element = __makeref(Make(4)[1]);
                        var churn = Churn(1);
                        observed = __refvalue(element, string) + churn.Length;
                        __refvalue(element, string) = Churn(2);
                        observed += (string)TypedReference.ToObject(element);
                        break;
                    }
                    case 1:
                    {
                        TypedReference field = __makeref(new Box().Text);
                        observed = Churn(3) + __refvalue(field, string);
                        break;
                    }
                    default:
                    {
                        TypedReference number = __makeref(new Box().Numbers[2]);
                        __refvalue(number, int) = 7;
                        observed = Churn(4) + (int)TypedReference.ToObject(number) + (__reftype(number) == typeof(int));
                        break;
                    }
                }
                var hash = 17;
                foreach (var c in observed)
                {
                    hash = unchecked(hash * 31 + c);
                }
                return hash & 0x7fffffff;
            }
        }
        """;
}
