using System;
using System.Diagnostics;
using System.IO;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm;
using NetWasm.Compiler.Wasm.Encoding;
using NetWasm.Compiler.Wasm.Emission.GeneratedFunctions;
using NetWasm.Compiler.Wasm.ModuleEncoding;

namespace NetWasm.Compiler.Wasm.Tests;

public sealed class ReferenceComparisonEmitterTests
{
    [Theory]
    [InlineData(false, WasmOpcodes.I32EqualZero, WasmOpcodes.I32Equal)]
    [InlineData(true, WasmOpcodes.I64EqualZero, WasmOpcodes.I64Equal)]
    public void EmitsReferenceComparisonsAtTargetWidth(
        bool memory64,
        byte equalZero,
        byte equal)
    {
        var layouts = new RecordingLayoutProvider(
            memory64 ? WasmTargetLayout.Wasm64 : WasmTargetLayout.Wasm32);
        var comparisons = EmitterTestSupport.CreateReferenceComparisons(layouts);
        var code = new GeneratedFunctionWriterFactory().Create();

        comparisons.Emit(code.Instructions, ReferenceComparison.EqualZero);
        comparisons.Emit(code.Instructions, ReferenceComparison.Equal);

        var bytes = code.Snapshots.Read();
        Assert.Contains(equalZero, bytes);
        Assert.Contains(equal, bytes);
    }

    [Fact]
    public void RejectsAnUnknownComparison()
    {
        var layouts = new RecordingLayoutProvider();
        IReferenceComparisonEmitter comparisons =
            EmitterTestSupport.CreateReferenceComparisons(layouts);
        var code = new GeneratedFunctionWriterFactory().Create();

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            comparisons.Emit(code.Instructions, (ReferenceComparison)99));
    }

    [Theory]
    [InlineData(WasmTarget.Wasm32, CliValueKind.ManagedReference)]
    [InlineData(WasmTarget.Wasm64, CliValueKind.ManagedReference)]
    [InlineData(WasmTarget.Wasm64, CliValueKind.I4)]
    public void EncodesAValidWasmComparisonForTheOperandWidth(
        WasmTarget target,
        CliValueKind operandType)
    {
        var layouts = new RecordingLayoutProvider(WasmTargetLayout.For(target));
        var comparisons = EmitterTestSupport.CreateReferenceComparisons(layouts);
        var code = new GeneratedFunctionWriterFactory().Create();
        code.Bytes.Write([0]);
        code.Instructions.Write(WasmInstruction.WithOperand(
            WasmOpcodes.LocalGet,
            WasmInstructionOperand.Unsigned(0)));
        comparisons.Emit(
            code.Instructions,
            ReferenceComparison.EqualZero,
            operandType);
        code.Instructions.Write(WasmInstruction.NoOperand(WasmOpcodes.End));

        var module = WasmModuleBuilderFactory.Create().Build(
            [],
            "runtime",
            "memory",
            [new WasmFunctionDefinition(
                "check",
                WasmFunctionType.Create(CliValueKind.I4, operandType),
                code.Snapshots.Read())],
            [new WasmExport("check", 0)],
            [],
            target: target);

        ValidateWithNode(module);
    }

    private static void ValidateWithNode(byte[] module)
    {
        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllBytes(path, module);
            var start = new ProcessStartInfo("node")
            {
                RedirectStandardError = true,
                RedirectStandardOutput = true,
            };
            start.ArgumentList.Add("-e");
            start.ArgumentList.Add(
                "const fs=require('node:fs');new WebAssembly.Module(fs.readFileSync(process.argv[1]));");
            start.ArgumentList.Add(path);
            using var process = Process.Start(start);
            Assert.NotNull(process);
            var error = process.StandardError.ReadToEnd();
            process.WaitForExit();
            Assert.True(process.ExitCode == 0, error);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
