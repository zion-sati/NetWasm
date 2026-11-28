// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Immutable;
using System.Reflection.Emit;
using System.Runtime.Loader;
using NetWasm.Compiler.ComponentModel;
using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests.EcmaStackTypes;

// Every operand pair ECMA-335 Partition III 1.5's tables allow compiles and computes what desktop .NET computes, in
// either order, with runtime operands and with literal ones the optimizers fold, at O0 and O3, on wasm32 and wasm64:
// the validator accepts the pair, the emitters widen an int32 beside a native int, and the analyses that read the
// operands agree. Desktop is 64-bit, so wasm32 is compared on the inputs whose results do not depend on the size of a
// native int.
public sealed class StackTypeTableCompilationTests
{
    public static TheoryData<string, WasmTarget, FinalWasmOptimization> Cells()
    {
        var cells = new TheoryData<string, WasmTarget, FinalWasmOptimization>();
        foreach (var family in StackTypeCases.Families)
        {
            foreach (var target in new[] { WasmTarget.Wasm32, WasmTarget.Wasm64 })
            {
                foreach (var optimization in new[] { FinalWasmOptimization.O0, FinalWasmOptimization.O3 })
                {
                    cells.Add(family, target, optimization);
                }
            }
        }
        return cells;
    }

    [Theory]
    [MemberData(nameof(Cells))]
    public void EveryAllowedPairComputesWhatDesktopComputes(string family, WasmTarget target, FinalWasmOptimization optimization)
    {
        var cases = StackTypeCases.Of(family);
        var bytes = StackTypeTableAssemblyBuilder.Build(cases, "StackTypes_" + family);
        var selector = target == WasmTarget.Wasm64 ? -1 : -2;
        var desktop = Desktop(bytes, [selector, .. Enumerable.Range(0, cases.Count * StackTypeTableAssemblyBuilder.Inputs.Length)]);

        using var assets = TestAssets.Create();
        var assembly = Path.Combine(assets.Directory, $"StackTypes_{family}.dll");
        File.WriteAllBytes(assembly, bytes);
        var compilation = NetWasmCompiler.Compile(new CompilerOptions(
            assembly, [assets.CoreLib], "EntryPoint", "Run", [], target,
            ReferenceAssemblyAliases: ImmutableDictionary<string, string>.Empty.Add("System.Private.CoreLib", "NetWasm.CoreLib"),
            ProjectDirectory: assets.Directory, Optimization: optimization));
        int Run(int input) => CompilerTestSupport.ExecuteWithStandardWasiNode(
            compilation.ApplicationModule, assets.Directory, input, target, compilation.StaticDataEnd);

        if (Run(selector) == desktop[0])
        {
            return;
        }
        // Name the cases that differ (runtime operands); a difference only with literal operands shows as none here.
        var differences = new List<string>();
        for (var index = 0; index < cases.Count && differences.Count < 25; index++)
        {
            for (var input = 0; input < StackTypeTableAssemblyBuilder.Inputs.Length; input++)
            {
                var value = StackTypeTableAssemblyBuilder.Inputs[input];
                var domain = cases[index].Domain;
                if (target == WasmTarget.Wasm32 && (domain == InputDomain.SixtyFourBitOnly || domain != InputDomain.All && value - 3 < 0) ||
                    domain == InputDomain.NotNegative && value - 3 < 0)
                {
                    continue;
                }
                var n = index * StackTypeTableAssemblyBuilder.Inputs.Length + input;
                var actual = Run(n);
                if (actual != desktop[n + 1])
                {
                    differences.Add($"{cases[index].Name} at {value}: {actual}, desktop {desktop[n + 1]}");
                }
            }
        }
        Assert.Fail($"{family} differs from desktop:\n" + (differences.Count == 0
            ? "only with literal operands"
            : string.Join("\n", differences)));
    }

    // Pairs the tables mark X: numbers of two widths, a float in an integer or overflow operation, an address added to
    // an address or offset by add.ovf, an object reference in arithmetic, an ordered comparison or a conversion.
    private static readonly StackTypeCase[] RejectedCases =
    [
        new("binary", OpCodes.Add, [StackKind.Int32, StackKind.Int64], StackKind.Int32),
        new("binary", OpCodes.Add, [StackKind.Int64, StackKind.NativeInt], StackKind.Int32),
        new("binary", OpCodes.Add, [StackKind.Float64, StackKind.Int32], StackKind.Int32),
        new("binary", OpCodes.Add, [StackKind.Address, StackKind.Address], StackKind.Int32),
        new("binary", OpCodes.Add, [StackKind.Object, StackKind.Int32], StackKind.Int32),
        new("binary", OpCodes.Sub, [StackKind.Int32, StackKind.Address], StackKind.Int32),
        new("binary", OpCodes.Mul, [StackKind.Address, StackKind.Int32], StackKind.Int32),
        new("integer", OpCodes.And, [StackKind.Float64, StackKind.Float64], StackKind.Int32),
        new("integer", OpCodes.Div_Un, [StackKind.Float64, StackKind.Float64], StackKind.Int32),
        new("integer", OpCodes.Rem_Un, [StackKind.Float32, StackKind.Float32], StackKind.Int32),
        new("overflow", OpCodes.Add_Ovf, [StackKind.Float64, StackKind.Float64], StackKind.Int32),
        new("overflow", OpCodes.Add_Ovf, [StackKind.Address, StackKind.Int32], StackKind.Int32),
        new("overflow", OpCodes.Mul_Ovf_Un, [StackKind.Address, StackKind.NativeInt], StackKind.Int32),
        new("shift", OpCodes.Shl, [StackKind.Float64, StackKind.Int32], StackKind.Int32),
        new("shift", OpCodes.Shl, [StackKind.Int32, StackKind.Int64], StackKind.Int32),
        new("unary", OpCodes.Neg, [StackKind.Address], StackKind.Int32),
        new("unary", OpCodes.Not, [StackKind.Float64], StackKind.Int32),
        new("compare", OpCodes.Ceq, [StackKind.Int32, StackKind.Int64], StackKind.Int32),
        new("compare", OpCodes.Ceq, [StackKind.Object, StackKind.Int32], StackKind.Int32),
        new("compare", OpCodes.Ceq, [StackKind.Address, StackKind.Int32], StackKind.Int32),
        new("compare", OpCodes.Clt, [StackKind.Object, StackKind.Object], StackKind.Int32),
        new("compare", OpCodes.Clt_Un, [StackKind.Object, StackKind.Object], StackKind.Int32),
        new("branch", OpCodes.Bgt, [StackKind.Object, StackKind.Object], StackKind.Int32),
        new("branch", OpCodes.Blt_Un, [StackKind.Object, StackKind.Object], StackKind.Int32),
        new("convert", OpCodes.Conv_I4, [StackKind.Address], StackKind.Int32),
        new("convert", OpCodes.Conv_R8, [StackKind.Address], StackKind.Float64),
        new("convert", OpCodes.Conv_I8, [StackKind.Object], StackKind.Int64),
    ];

    public static TheoryData<int> Rejected() => [.. Enumerable.Range(0, RejectedCases.Length)];

    [Theory]
    [MemberData(nameof(Rejected))]
    public void APairTheTablesRejectIsInvalidCil(int index)
    {
        var @case = RejectedCases[index];
        using var assets = TestAssets.Create();
        var assembly = Path.Combine(assets.Directory, $"StackTypesRejected{index}.dll");
        File.WriteAllBytes(assembly, StackTypeTableAssemblyBuilder.Build([@case], $"StackTypesRejected{index}"));

        var error = Assert.Throws<CompilerException>(() => NetWasmCompiler.Compile(new CompilerOptions(
            assembly, [assets.CoreLib], "EntryPoint", "Run", [],
            ReferenceAssemblyAliases: ImmutableDictionary<string, string>.Empty.Add("System.Private.CoreLib", "NetWasm.CoreLib"),
            ProjectDirectory: assets.Directory)));
        Assert.True(error.Diagnostic.Code == DiagnosticCode.InvalidCil, $"{@case.Name}: {error.Diagnostic}");
    }

    // Run(selector) on desktop for each selector.
    private static int[] Desktop(byte[] bytes, int[] selectors)
    {
        var context = new AssemblyLoadContext("StackTypes.Desktop", isCollectible: true);
        try
        {
            var run = context.LoadFromStream(new MemoryStream(bytes)).GetType("EntryPoint")!.GetMethod("Run")!;
            return [.. selectors.Select(selector => (int)run.Invoke(null, [selector])!)];
        }
        finally
        {
            context.Unload();
        }
    }
}
