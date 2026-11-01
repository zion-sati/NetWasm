namespace NetWasm.Compiler.Tests.Correctness;

public sealed class AttributeMaterializationGcTests(CorrectnessTestRunner runner)
{
    [Fact]
    public void MaterializationPreservesCallerAndAttributeRootsAcrossCollections() =>
        runner.Run(new(
            "AttributeRoots", "AttributeRoots", Source, [42])
        {
            ExpectedReturnValue = 42,
            RequiredRuntimeCapabilities = OracleRuntimeCapabilities.GarbageCollection |
                OracleRuntimeCapabilities.Finalization,
            Matrix = new(CorpusMatrixProfile.Extended, Backend: CorpusExecutionBackend.Linked),
        });

    private const string Source = """
        using System;
        using System.Reflection;

        namespace AttributeRoots;

        public static class Probe
        {
            public static int Finalized;

            public static void Collect()
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
            }
        }

        public sealed class Witness
        {
            public int Value;

            ~Witness() => Probe.Finalized++;
        }

        public enum Small : short { Answer = 47 }

        [AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
        public sealed class MarkerAttribute : Attribute
        {
            public MarkerAttribute(int value, object payload)
            {
                Value = value;
                Payload = payload;
                Probe.Collect();
            }

            public int Value { get; }
            public object Payload { get; }
            public int Label
            {
                get => label;
                set
                {
                    label = value;
                    Probe.Collect();
                }
            }

            private int label;

            ~MarkerAttribute() => Probe.Finalized++;
        }

        [Marker(7, new object[] { (byte)31, typeof(Witness), new int[] { 37, 41 } }, Label = 11)]
        public sealed class One;

        [Marker(13, 43L, Label = 17)]
        [Marker(19, Small.Answer, Label = 23)]
        public sealed class Many;

        public static class EntryPoint
        {
            public static int Trace() => 0;

            [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
            private static void CreateUnrootedWitness() => _ = new Witness();

            public static int Run(int input)
            {
                CreateUnrootedWitness();
                Probe.Collect();
                if (Probe.Finalized != 1)
                    return 4;
                Probe.Finalized = 0;
                var caller = new Witness { Value = input };
                var one = typeof(One).GetCustomAttribute<MarkerAttribute>();
                var many = (MarkerAttribute[])typeof(Many)
                    .GetCustomAttributes<MarkerAttribute>();
                Probe.Collect();
                if (Probe.Finalized != 0)
                    return 1;
                if (one is null || one.Value != 7 || one.Label != 11)
                    return 2;
                if (many.Length != 2 || many[0].Value != 13 ||
                    many[0].Label != 17 || many[1].Value != 19 ||
                    many[1].Label != 23)
                    return 3;
                var payload = (object[])one.Payload;
                if (payload.Length != 3 || (byte)payload[0] != 31 || (Type)payload[1] != typeof(Witness))
                    return 5;
                var numbers = (int[])payload[2];
                if (numbers.Length != 2 || numbers[0] != 37 || numbers[1] != 41 ||
                    (long)many[0].Payload != 43 || (Small)many[1].Payload != Small.Answer)
                    return 6;
                return caller.Value;
            }
        }
        """;
}
