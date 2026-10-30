using NetWasm.Compiler.Analysis;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Layout;
using NetWasm.Compiler.Wasm.Emission;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

using static CompilerTestSupport;

public sealed class IntegerDivRemCompilationTests
{
    private const string Source = """
        using System;

        public static class EntryPoint
        {
            public static int Run(int input)
            {
                var signedByte = Math.DivRem((sbyte)-128, (sbyte)-1);
                if (signedByte != ((sbyte)-128, (sbyte)0)) return 1;

                var unsignedByte = Math.DivRem(byte.MaxValue, (byte)7);
                if (unsignedByte != ((byte)36, (byte)3)) return 2;

                var signedShort = Math.DivRem(short.MinValue, (short)-1);
                if (signedShort != (short.MinValue, (short)0)) return 3;

                var unsignedShort = Math.DivRem(ushort.MaxValue, (ushort)257);
                if (unsignedShort != ((ushort)255, (ushort)0)) return 4;

                var signed = Math.DivRem(-123_456_789 - input, 97);
                if (signed.Quotient * 97 + signed.Remainder != -123_456_789 - input ||
                    signed.Remainder >= 0) return 5;

                var unsigned = Math.DivRem(uint.MaxValue, 65_537U);
                if (unsigned != (65_535U, 0U)) return 6;

                var signedLong = Math.DivRem(-9_000_000_000_000_000_000L - input, 97L);
                if (signedLong.Quotient * 97L + signedLong.Remainder !=
                    -9_000_000_000_000_000_000L - input ||
                    signedLong.Remainder >= 0) return 7;

                var unsignedLong = Math.DivRem(ulong.MaxValue, 4_294_967_296UL);
                if (unsignedLong != (4_294_967_295UL, 4_294_967_295UL)) return 8;

                var native = Math.DivRem((nint)(-123_456_789 - input), (nint)97);
                if (native.Quotient * 97 + native.Remainder != -123_456_789 - input ||
                    native.Remainder >= 0) return 9;

                var unsignedNative = Math.DivRem((nuint)123_456_789U + (nuint)input, (nuint)97);
                if (unsignedNative.Quotient * 97 + unsignedNative.Remainder !=
                    (nuint)123_456_789U + (nuint)input) return 10;

                var character = char.DivRem('Z', (char)7);
                if (character != ((char)12, (char)6)) return 11;

                var exceptions = 0;
                try
                {
                    _ = Math.DivRem(input, input - input);
                }
                catch (DivideByZeroException)
                {
                    exceptions |= 1;
                }

                try
                {
                    _ = Math.DivRem(int.MinValue, input - 42);
                }
                catch (OverflowException)
                {
                    exceptions |= 2;
                }

                try
                {
                    _ = Math.DivRem(long.MinValue, (long)input - 42L);
                }
                catch (OverflowException)
                {
                    exceptions |= 4;
                }

                return exceptions == 7 ? 42 : 12;
            }
        }
        """;

    private const string PromotionSource = """
        using System;
        using System.Runtime.CompilerServices;

        public static class EntryPoint
        {
            [MethodImpl(MethodImplOptions.NoInlining)]
            private static bool Direct(
                ulong left,
                ulong right,
                ulong expectedQuotient,
                ulong expectedRemainder)
            {
                (ulong quotient, ulong remainder) = Math.DivRem(left, right);
                return quotient == expectedQuotient && remainder == expectedRemainder;
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static bool ThroughLocal(
                int left,
                int right,
                int expectedQuotient,
                int expectedRemainder)
            {
                var result = Math.DivRem(left, right);
                var quotient = result.Quotient;
                var remainder = result.Remainder;
                return quotient == expectedQuotient && remainder == expectedRemainder;
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static bool ThroughLocal64(
                long left,
                long right,
                long expectedQuotient,
                long expectedRemainder)
            {
                var result = Math.DivRem(left, right);
                var quotient = result.Quotient;
                var remainder = result.Remainder;
                return quotient == expectedQuotient && remainder == expectedRemainder;
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static bool ThroughLocalNative(
                nint left,
                nint right,
                nint expectedQuotient,
                nint expectedRemainder)
            {
                var result = nint.DivRem(left, right);
                var quotient = result.Quotient;
                var remainder = result.Remainder;
                return quotient == expectedQuotient && remainder == expectedRemainder;
            }

            public static int Run(int input)
            {
                if (!Direct(
                    ulong.MaxValue - (uint)input,
                    97,
                    190_172_619_316_593_315,
                    19)) return 1;
                if (!ThroughLocal(
                    -123_456_789 - input,
                    97,
                    -1_272_750,
                    -80)) return 2;
                if (!ThroughLocal64(
                    -9_000_000_000_000_000_000L - input,
                    97,
                    -92_783_505_154_639_175L,
                    -66)) return 3;
                if (!ThroughLocalNative(
                    (nint)(-123_456_789 - input),
                    (nint)97,
                    (nint)(-1_272_750),
                    (nint)(-80))) return 4;

                var exceptions = 0;
                try
                {
                    _ = Direct((uint)input, 0, 0, 0);
                }
                catch (DivideByZeroException)
                {
                    exceptions |= 1;
                }

                try
                {
                    _ = ThroughLocal(int.MinValue, -1, 0, 0);
                }
                catch (OverflowException)
                {
                    exceptions |= 2;
                }

                return exceptions == 3 ? 42 : 5;
            }
        }
        """;

    private const string DecimalDivisionSource = """
        public static class EntryPoint
        {
            public static int Run(int input)
            {
                var quotient = (decimal.MaxValue - input) / 97m;
                return quotient > 0m ? 42 : 0;
            }
        }
        """;

    [Theory]
    [InlineData(false, WasmTarget.Wasm32)]
    [InlineData(true, WasmTarget.Wasm32)]
    [InlineData(false, WasmTarget.Wasm64)]
    [InlineData(true, WasmTarget.Wasm64)]
    public void CompilerExecutesIntegralTupleDivRemWithoutRuntimeHelpers(
        bool optimize,
        WasmTarget target)
    {
        using var assets = TestAssets.Create();
        ICompilationScenarioExecutor executor = new CompilationScenarioExecutor(assets);

        var observed = executor.Execute(new CompilationScenario(
            $"IntegerDivRemFixture_{optimize}_{target}",
            Source,
            "EntryPoint",
            optimize,
            target,
            41,
            []));

        Assert.Equal(42, observed);
    }

    [Fact]
    public void CompilerDoesNotRetainIntrinsicizedDivRemImplementations()
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileOptimizedSource(
            "IntegerDivRemReachabilityFixture",
            Source);

        var compilation = NetWasmCompiler.Compile(new CompilerOptions(
            assembly,
            [assets.CoreLib],
            "EntryPoint",
            "Run",
            []));

        Assert.DoesNotContain(
            compilation.Program.Methods.Values,
            method => method.Method.Definition.Name == "DivRem" &&
                method.Method.DeclaringType.FullName is
                    "System.Math" or "System.Char");
        Assert.Contains(ManagedExceptionKind.DivideByZero,
            compilation.Program.ImplicitExceptions);
        Assert.Contains(ManagedExceptionKind.Overflow,
            compilation.Program.ImplicitExceptions);
        Assert.Equal(42, ExecuteWithNode(
            compilation.ApplicationModule,
            assets.Directory,
            41));
    }

    [Theory]
    [InlineData(false, WasmTarget.Wasm32)]
    [InlineData(true, WasmTarget.Wasm32)]
    [InlineData(false, WasmTarget.Wasm64)]
    [InlineData(true, WasmTarget.Wasm64)]
    public void CompilerExecutesPromotedDivRemHelpersAndPropagatesExceptions(
        bool optimize,
        WasmTarget target)
    {
        using var assets = TestAssets.Create();
        ICompilationScenarioExecutor executor = new CompilationScenarioExecutor(assets);

        var observed = executor.Execute(new CompilationScenario(
            $"PromotedIntegerDivRemFixture_{optimize}_{target}",
            PromotionSource,
            "EntryPoint",
            optimize,
            target,
            41,
            []));

        Assert.Equal(42, observed);
    }

    [Fact]
    public void PromotionFixtureSelectsScalarPromotionForEveryNoEhHelper()
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileOptimizedSource(
            "PromotedIntegerDivRemPlanFixture",
            PromotionSource);
        var compilation = NetWasmCompiler.Compile(new CompilerOptions(
            assembly,
            [assets.CoreLib],
            "EntryPoint",
            "Run",
            []));
        using var metadata = MetadataCompilationTestFactory.Load(
            assembly,
            [assets.CoreLib]);
        var snapshot = metadata.Snapshot;
        var fields = MetadataCompilationTestActors.Fields(snapshot);
        var methods = MetadataCompilationTestActors.Methods(snapshot);
        var valueLayouts = new ValueLayoutProvider(
            CompilerTestSupport.CreateValueLayouts(
                MetadataCompilationTestActors.TypeDefinitions(snapshot),
                fields,
                compilation.Layouts));
        var promotions = new TransientAggregatePromotionPlanner(
            methods,
            new RuntimeIntrinsicRegistry(
                MetadataCompilationTestActors.Symbols(snapshot),
                snapshot.Methods),
            valueLayouts,
            CompilerTestSupport.CreateInstanceFields(compilation.Layouts));
        var lowered = ValidationTestData.Lower(compilation.Program);

        foreach (var helperName in new[]
                 {
                     "Direct",
                     "ThroughLocal",
                     "ThroughLocal64",
                     "ThroughLocalNative",
                 })
        {
            var body = compilation.Program.Methods.Values.Single(candidate =>
                candidate.Method.Definition.Name == helperName);
            var plan = promotions.Create(
                lowered.Methods[body.Method.Definition.Key]);

            Assert.Single(plan.Producers);
            Assert.Equal(2, plan.Projections.Count);
        }
    }

    [Fact]
    public void DecimalDiv96By32PromotesAllDivRemResultsAndEliminatesItsValueFrame()
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileOptimizedSource(
            "DecimalDivRemPromotionFixture",
            DecimalDivisionSource);
        // Div96By32 is aggressively inlined; the plan is for its own body.
        var compilation = NetWasmCompiler.Compile(new CompilerOptions(
            assembly,
            [assets.CoreLib],
            "EntryPoint",
            "Run",
            [],
            OptimizationOverrides: new(
                ManagedMethodInlining: NetWasm.Compiler.ComponentModel.Optimization.ManagedMethodInlining.Disabled)));
        using var metadata = MetadataCompilationTestFactory.Load(
            assembly,
            [assets.CoreLib]);
        var snapshot = metadata.Snapshot;
        var types = MetadataCompilationTestActors.Types(snapshot);
        var fields = MetadataCompilationTestActors.Fields(snapshot);
        var methods = MetadataCompilationTestActors.Methods(snapshot);
        var symbols = MetadataCompilationTestActors.Symbols(snapshot);
        var typeDefinitions = MetadataCompilationTestActors.TypeDefinitions(snapshot);
        var valueLayoutResolver = CompilerTestSupport.CreateValueLayouts(
            typeDefinitions,
            fields,
            compilation.Layouts);
        IValueLayoutProvider valueLayouts = new ValueLayoutProvider(
            valueLayoutResolver);
        var instanceFields = CompilerTestSupport.CreateInstanceFields(
            compilation.Layouts);
        var intrinsics = new RuntimeIntrinsicRegistry(symbols, snapshot.Methods);
        var promotions = new TransientAggregatePromotionPlanner(
            methods,
            intrinsics,
            valueLayouts,
            instanceFields);
        var lowered = ValidationTestData.Lower(compilation.Program);
        var div96Body = compilation.Program.Methods.Values.Single(body =>
            body.Method.Definition.Name == "Div96By32");
        var div96 = lowered.Methods[div96Body.Method.Definition.Key];
        compilation.Program.ManagedByRefEffects.Methods.TryGetValue(
            div96Body.Method.CanonicalName,
            out var byRefEffects);

        var plan = promotions.Create(div96);

        Assert.Equal(3, plan.Producers.Count);
        Assert.Equal(6, plan.Projections.Count);

        var identities = new CilTypeIdentityResolver(types);
        ValueFrameLayoutPlanner CreateFramePlanner(
            ITransientAggregatePromotionPlanner promotionPlanner) => new(
            fields,
            methods,
            compilation.Layouts,
            valueLayouts,
            new CilTypeOperandResolver(identities),
            identities,
            new ArgumentTypeResolver(types),
            new ArgumentSignatureTypeResolver(identities),
            new DirectValueLoadSelector(),
            promotionPlanner);
        var baseline = CreateFramePlanner(EmptyPromotionPlanner.Instance).Create(
            div96,
            memberExecution: compilation.Program.MemberExecution,
            byRefEffects: byRefEffects);
        var promoted = CreateFramePlanner(promotions).Create(
            div96,
            memberExecution: compilation.Program.MemberExecution,
            byRefEffects: byRefEffects);

        Assert.Equal(64, baseline.Size);
        Assert.Equal(0, promoted.Size);
        Assert.Empty(promoted.LocalOffsets);
        Assert.Empty(promoted.TemporaryOffsets);
    }

    [Fact]
    public void DecimalPowersOfTenUsesRvaBackedReadOnlySpanWithoutArrayInitialization()
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileOptimizedSource(
            "DecimalRvaSpanFixture",
            DecimalDivisionSource);
        var compilation = NetWasmCompiler.Compile(new CompilerOptions(
            assembly,
            [assets.CoreLib],
            "EntryPoint",
            "Run",
            []));
        var instructions = compilation.Program.Methods.Values
            .SelectMany(body => body.Body.Instructions)
            .ToArray();
        // Division reads the table from its RVA data, through the span getter or,
        // once static data-view elimination folds the getter away, element by element.
        var span = Assert.Single(instructions
            .Where(instruction => instruction.Operation is
                CilOperation.CreateReadOnlySpanFromRva or CilOperation.LoadStaticDataElement)
            .Select(instruction => instruction.Operand)
            .OfType<CilOperand.ReadOnlySpanData>()
            .Where(data => data.ElementType.CanonicalName == "primitive:u4" && data.ElementCount == 10)
            .DistinctBy(data => data.Field.Definition.Key));

        Assert.Equal(40, span.Field.Definition.InitialData.Length);
        Assert.Contains(span.Field.Definition.Key, compilation.Program.Fields);
        Assert.DoesNotContain(instructions, instruction =>
            instruction.Operation == CilOperation.InitializeArrayData ||
            instruction.Operation == CilOperation.NewArray &&
            instruction.Operand is CilOperand.TypeIdentity { Value.CanonicalName: "primitive:u4" });
        Assert.DoesNotContain(instructions, instruction =>
            instruction.Operation == CilOperation.Call &&
            instruction.Operand is CilOperand.MethodInstance method &&
            method.Value.Definition.Name is "CreateSpan" or "InitializeArray");
    }

    private sealed class EmptyPromotionPlanner : ITransientAggregatePromotionPlanner
    {
        private EmptyPromotionPlanner()
        {
        }

        public static EmptyPromotionPlanner Instance { get; } = new();

        public TransientAggregatePromotionPlan Create(
            NetWasm.Compiler.ControlFlow.Structured.StructuredMethod method)
        {
            ArgumentNullException.ThrowIfNull(method);
            return TransientAggregatePromotionPlan.Empty;
        }
    }
}
