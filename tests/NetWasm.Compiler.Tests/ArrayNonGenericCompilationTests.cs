using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

public sealed class ArrayNonGenericCompilationTests
{
    [Theory]
    [InlineData(false, WasmTarget.Wasm32)]
    [InlineData(false, WasmTarget.Wasm64)]
    [InlineData(true, WasmTarget.Wasm32)]
    [InlineData(true, WasmTarget.Wasm64)]
    public void NonGenericCopyAndClearExecuteForEveryProfile(
        bool optimize,
        WasmTarget target)
    {
        using var assets = TestAssets.Create();
        ICompilationScenarioExecutor executor = new CompilationScenarioExecutor(assets);
        var observed = executor.Execute(new CompilationScenario(
            "ArrayNonGenericFixture",
            Source,
            "ArrayNonGenericFixture.EntryPoint",
            optimize,
            target,
            0,
            []));

        Assert.Equal(0, observed);
    }

    private const string Source = """
        using System;

        namespace ArrayNonGenericFixture;

        public sealed class Payload
        {
            public Payload(int value) => Value = value;
            public int Value { get; }
        }

        public struct Pair
        {
            public Payload? Payload;
            public int Number;
        }

        public static class EntryPoint
        {
            public static int Run(int input)
            {
                Array numbers = new[] { 1, 2, 3, 4 };
                Array.Copy(numbers, 0, numbers, 1, 3);
                if (((int[])numbers)[1] != 1 || ((int[])numbers)[3] != 3) return 1;
                Array.Clear(numbers, 2, 2);
                if (((int[])numbers)[1] != 1 || ((int[])numbers)[2] != 0 ||
                    ((int[])numbers)[3] != 0) return 2;

                Array source = new Pair[]
                {
                    new() { Payload = new Payload(7), Number = 11 },
                    new() { Payload = new Payload(13), Number = 17 },
                };
                Array destination = new Pair[2];
                Array.Copy(source, destination, 2);
                for (var allocation = 0; allocation < 128; allocation++)
                {
                    _ = new Payload(allocation);
                }
                var pairs = (Pair[])destination;
                if (pairs[0].Payload!.Value != 7 || pairs[1].Number != 17) return 3;
                Array.Clear(destination);
                if (pairs[0].Payload is not null || pairs[1].Number != 0) return 4;

                Array strings = new string[] { "a", "bb" };
                Array objects = new object[2];
                Array.Copy(strings, objects, 2);
                if (((object[])objects)[1] is not string text || text.Length != 2) return 5;

                var rejected = false;
                try
                {
                    Array.Copy(new object[] { "ok", new Payload(1) }, new string[2], 2);
                }
                catch (InvalidCastException)
                {
                    rejected = true;
                }
                if (!rejected) return 6;

                Array matrixSource = new int[,] { { 1, 2 }, { 3, 4 } };
                Array matrixDestination = new int[2, 2];
                Array.Copy(matrixSource, matrixDestination, 4);
                var matrix = (int[,])matrixDestination;
                if (matrix[1, 0] != 3) return 7;
                Array.Clear(matrixDestination, 1, 2);
                if (matrix[0, 0] != 1 || matrix[0, 1] != 0 ||
                    matrix[1, 0] != 0 || matrix[1, 1] != 4) return 8;

                return 0;
            }
        }
        """;
}
