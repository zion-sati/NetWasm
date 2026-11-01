namespace NetWasm.Compiler.Tests.Correctness;

public sealed class NullableMemberGcTests(CorrectnessTestRunner runner)
{
    [Fact]
    public void NullableMemberResultsAndExpressionReceiversSurviveCollections() =>
        runner.Run(new("NullableMemberRoots", "NullableMemberRoots", Source, [42])
        {
            ExpectedReturnValue = 42,
            RequiredRuntimeCapabilities = OracleRuntimeCapabilities.GarbageCollection |
                OracleRuntimeCapabilities.Finalization,
            Matrix = new(CorpusMatrixProfile.Extended, Backend: CorpusExecutionBackend.Linked),
        });

    private const string Source = """
        using System;
        using System.Linq.Expressions;
        using System.Runtime.CompilerServices;
        namespace NullableMemberRoots;

        public sealed class Witness
        {
            public static int Finalized;
            ~Witness() => Finalized++;
        }

        public enum Code : long { High = 1L << 40 }
        public class Model
        {
            public int? Field;
            public long? Stored;
            public Code? Enum;
            public virtual long? Number => 0;
            public Code? EnumValue
            {
                get { Collect(); return Enum; }
            }
            public static void Collect()
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
            }
        }

        public sealed class Derived : Model
        {
            public override long? Number
            {
                get { Collect(); return Stored; }
            }
        }

        public static class EntryPoint
        {
            private static int stage;
            public static int Trace() => stage;

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static void CreateGarbage() => _ = new Witness();

            public static int Run(int input)
            {
                stage = 1;
                CreateGarbage();
                Model.Collect();
                if (Witness.Finalized != 1) return 1;
                stage = 2;
                var model = new Derived();
                Expression<Func<Model, int?>> fieldExpression = value => value.Field;
                Expression<Func<Model, long?>> getterExpression = value => value.Number;
                Expression<Func<Model, Code?>> enumExpression = value => value.EnumValue;
                stage = 3;
                var field = fieldExpression.Compile();
                var getter = getterExpression.Compile();
                var enumGetter = enumExpression.Compile();
                stage = 4;
                if (field(model) != null || getter(model) != null || enumGetter(model) != null) return 2;
                model.Field = input;
                model.Stored = 1L << 40;
                model.Enum = Code.High;
                stage = 5;
                // Keep all three boxed nullable results live across real collection.
                object? first = field(model);
                object? second = getter(model);
                object? third = enumGetter(model);
                stage = 6;
                Model.Collect();
                if (first is null || second is null || third is null) return 5;
                if ((int)first != input || (long)second != 1L << 40 || (Code)third != Code.High) return 3;
                model.Field = null;
                model.Stored = null;
                model.Enum = null;
                stage = 7;
                Model.Collect();
                if (field(model) != null || getter(model) != null || enumGetter(model) != null) return 4;
                return input;
            }
        }
        """;
}
