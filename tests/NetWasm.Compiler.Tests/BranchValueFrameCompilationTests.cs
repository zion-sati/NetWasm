using NetWasm.Compiler.ComponentModel;
using NetWasm.Compiler.ComponentModel.Optimization;
using NetWasm.Compiler.ControlFlow.Structured;
using NetWasm.Compiler.ControlFlow.Structuring;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Tests.Correctness;
using NetWasm.Compiler.Wasm.Emission.Methods;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

public sealed class BranchValueFrameCompilationTests
{
    [Theory]
    [InlineData(WasmTarget.Wasm32, false)]
    [InlineData(WasmTarget.Wasm64, false)]
    [InlineData(WasmTarget.Wasm32, true)]
    [InlineData(WasmTarget.Wasm64, true)]
    public void PreservesScalarAliasesAcrossColdCallsExceptionsAndSubsequentInvocations(WasmTarget target, bool richExceptions)
    {
        using var assets = TestAssets.Create();
        ICompilationScenarioExecutor executor = new CompilationScenarioExecutor(assets);
        var result = executor.Execute(new CompilationScenario(
            $"BranchValueFrame_{target}_{richExceptions}",
            """
            using System;
            using System.Runtime.CompilerServices;

            public static class EntryPoint
            {
                public static int Run(int input)
                {
                    for (int index = 0; index < 32; index++)
                    {
                        if (Evaluate(input, false, false) != 22) return 1;
                        if (Evaluate(input, true, false) != 52) return 2;
                        try
                        {
                            _ = Evaluate(input, true, true);
                            return 3;
                        }
                        catch (InvalidOperationException) { }
                        if (Evaluate(input, true, false) != 52) return 4;
                        if (Evaluate(input, false, false) != 22) return 5;
                    }
                    return 42;
                }

                [MethodImpl(MethodImplOptions.NoInlining)]
                private static long Evaluate(int input, bool cold, bool fail)
                {
                    long scalar = input + 7;
                    if (cold)
                        Mutate(ref scalar, fail);
                    else
                        scalar += 5;
                    return scalar;
                }

                [MethodImpl(MethodImplOptions.NoInlining)]
                private static void Mutate(ref long value, bool fail)
                {
                    value = value * 3 + 1;
                    if (fail) throw new InvalidOperationException();
                }
            }
            """, "EntryPoint", true, target, 10, [])
        {
            Optimization = FinalWasmOptimization.O3,
            EmitStackTrace = richExceptions,
        });

        Assert.Equal(42, result);
    }

    // The argument fixture's Inspect and Adjust hold their argument in the value frame on the arm
    // that takes its address, and only there. (Its Consume<T> inlines double.ToString, which copies
    // the argument into a local first.)
    [Theory]
    [InlineData("Inspect")]
    [InlineData("Adjust")]
    public void PlacesTheFrameHoldingAnArgumentOnTheArmThatAddressesIt(string name)
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileOptimizedSource("BranchFrameArguments_" + name, BranchValueFrameArgumentTests.Source);
        var compilation = NetWasmCompiler.Compile(new CompilerOptions(
            assembly, [assets.CoreLib], "BranchFrameArguments.EntryPoint", "Run", [],
            Optimization: FinalWasmOptimization.O3));
        var method = Assert.Single(compilation.Program.Methods.Values, method => method.Method.Definition.Name == name);
        var structured = new ValidatedStructuredMethodBuilderFactory()
            .Create(BranchPrefixDuplicationPolicy.Speed).Build(method.ControlFlow);

        var (layout, placed) = CompiledValueFrames.Place(assets, assembly, compilation.Layouts, structured,
            compilation.Program.RootMaps[method.Method.Definition.Key]);

        Assert.True(layout.ArgumentOffsets.ContainsKey(0));
        var plan = Assert.IsType<TerminalBranchValueFramePlan>(placed);
        var framed = plan.FramedWhenTrue ? plan.WhenTrue : plan.WhenFalse;
        var unframed = plan.FramedWhenTrue ? plan.WhenFalse : plan.WhenTrue;
        Assert.Contains(Instructions(structured, framed), instruction => instruction.Operation == CilOperation.LoadArgumentAddress);
        Assert.DoesNotContain(Instructions(structured, unframed).Concat(structured.Blocks[plan.Condition.Block].Instructions),
            instruction => instruction.Operation == CilOperation.LoadArgumentAddress);
    }

    private static IEnumerable<CilInstruction> Instructions(StructuredMethod method, StructuredRegion region) => region switch
    {
        StructuredCode code => method.Blocks[code.Occurrence.Block].Instructions,
        StructuredSequence sequence => sequence.Regions.SelectMany(child => Instructions(method, child)),
        StructuredIf branch => method.Blocks[branch.Condition.Block].Instructions
            .Concat(Instructions(method, branch.WhenTrue)).Concat(Instructions(method, branch.WhenFalse)),
        _ => throw new NotSupportedException(region.GetType().Name),
    };
}
