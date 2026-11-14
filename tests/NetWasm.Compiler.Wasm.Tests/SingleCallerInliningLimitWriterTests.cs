using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Encoding;
using NetWasm.Compiler.Wasm.ModuleEncoding;
using NetWasm.Compiler.Wasm.Units;

namespace NetWasm.Compiler.Wasm.Tests;

public sealed class SingleCallerInliningLimitWriterTests
{
    private const int Imports = 3;

    [Fact]
    public void RejectsMissingCollaborators()
    {
        var scanner = new WasmIndexOperandScanner();
        Assert.Throws<ArgumentNullException>("inner", () => new SingleCallerInliningLimitWriter(null!, scanner));
        Assert.Throws<ArgumentNullException>("scanner", () =>
            new SingleCallerInliningLimitWriter(new RecordingWriter(), null!));
    }

    [Fact]
    public void ACallerOfSingleCallerFunctionsKeepsItsLargestOnesAsCallsUntilTheRestFit()
    {
        // Function 0 calls 1 to 4 once each; together they exceed the limit by less than the largest.
        var functions = Functions(
            Body(100, calls: [1, 2, 3, 4]),
            Body(150 * 1024), Body(140 * 1024), Body(130 * 1024), Body(120 * 1024));

        var written = Write(functions);

        Assert.Equal([false, true, false, false, false], written.Select(function => function.IsNoInlining));
    }

    [Fact]
    public void FunctionsWithSeveralCallersOrTakenAsValuesAreNotCounted()
    {
        // 1 has two callers and 2 is a ref.func, so Binaryen inlines neither by its one caller; 3 and 4 fit.
        var functions = Functions(
            Body(100, calls: [1, 2, 3, 4], references: [2]),
            Body(300 * 1024), Body(300 * 1024), Body(150 * 1024), Body(100, calls: [1]));

        Assert.All(Write(functions), function => Assert.False(function.IsNoInlining));
    }

    [Fact]
    public void ASingleCallerChainCountsEverythingItAbsorbs()
    {
        // 0 calls 1 and 2; 1 calls 3. 1 weighs 1 and 3 together, the largest, so it stays a call; 3 still inlines into 1.
        var functions = Functions(
            Body(100, calls: [1, 2]),
            Body(100, calls: [3]), Body(200 * 1024), Body(300 * 1024));

        Assert.Equal([false, true, false, false], Write(functions).Select(function => function.IsNoInlining));
    }

    [Fact]
    public void AModuleWhoseCodeFitsIsWrittenAsItIs()
    {
        var functions = Functions(Body(100, calls: [1]), Body(1000));
        var inner = new RecordingWriter();
        new SingleCallerInliningLimitWriter(inner, new WasmIndexOperandScanner())
            .Write(new WasmBinaryWriter(new WasmBinaryBuffer()), functions, Imports);

        Assert.Same(functions, inner.Functions);
    }

    [Fact]
    public void ExistingHintsAreKept()
    {
        var functions = Functions(Body(100, calls: [1]), Body(500 * 1024));
        functions[0] = functions[0] with { IsAggressiveInlining = true };

        var written = Write(functions);

        Assert.True(written[0].IsAggressiveInlining);
        Assert.True(written[1].IsNoInlining);
    }

    private static List<WasmFunctionDefinition> Functions(params byte[][] bodies) =>
        [.. bodies.Select((body, index) => new WasmFunctionDefinition($"f{index}", WasmFunctionType.Create(CliValueKind.Void), body))];

    private static IReadOnlyList<WasmFunctionDefinition> Write(IReadOnlyList<WasmFunctionDefinition> functions)
    {
        var inner = new RecordingWriter();
        new SingleCallerInliningLimitWriter(inner, new WasmIndexOperandScanner())
            .Write(new WasmBinaryWriter(new WasmBinaryBuffer()), functions, Imports);
        return inner.Functions!;
    }

    // No locals, a call to each defined function in calls, a ref.func of each in references, nops to the size, end.
    private static byte[] Body(int size, int[]? calls = null, int[]? references = null)
    {
        var body = new List<byte> { 0x00 };
        foreach (var callee in calls ?? [])
        {
            body.Add(0x10);
            body.AddRange(Leb(Imports + callee));
        }
        foreach (var callee in references ?? [])
        {
            body.Add(0xD2);
            body.AddRange(Leb(Imports + callee));
            body.Add(0x1A);
        }
        while (body.Count < size - 1) body.Add(0x01);
        body.Add(0x0B);
        return [.. body];
    }

    private static IEnumerable<byte> Leb(int value)
    {
        do
        {
            var next = (byte)(value & 0x7F);
            value >>= 7;
            yield return value == 0 ? next : (byte)(next | 0x80);
        }
        while (value != 0);
    }

    private sealed class RecordingWriter : IFunctionInlineAnnotationWriter
    {
        public IReadOnlyList<WasmFunctionDefinition>? Functions { get; private set; }

        public void Write(IWasmBinaryWriter output, IReadOnlyList<WasmFunctionDefinition> functions, int functionImportCount) =>
            Functions = functions;
    }
}
