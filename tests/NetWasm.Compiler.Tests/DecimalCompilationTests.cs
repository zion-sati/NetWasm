using NetWasm.TestInfrastructure;
using NetWasm.Compiler.Analysis;
using NetWasm.Compiler.ComponentModel;
using NetWasm.Compiler.ComponentModel.Optimization;
using NetWasm.Compiler.ControlFlow.Structured;
using NetWasm.Compiler.ControlFlow.Structuring;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Layout;
using NetWasm.Compiler.Wasm.Emission;
using NetWasm.Compiler.Wasm.Emission.Methods;
using NetWasm.Compiler.Wasm.Emission.Values;

namespace NetWasm.Compiler.Tests;

using static CompilerTestSupport;

public sealed class DecimalCompilationTests
{
    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void StructuredAdditionConfinesValueFrameAddressesAndTemporariesToOneArm(WasmTarget target)
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileOptimizedSource(
            $"DecimalFrameRegionFixture_{target}",
            """
            public static class EntryPoint
            {
                public static int Run(int input)
                {
                    decimal sum = input;
                    for (int index = 0; index < 4; index++) sum += 2.25m;
                    return (int)sum;
                }
            }
            """);
        var compilation = NetWasmCompiler.Compile(new CompilerOptions(
            assembly, [assets.CoreLib], "EntryPoint", "Run", [],
            Target: target, Optimization: FinalWasmOptimization.O3));
        var addition = Assert.Single(compilation.Program.Methods.Values,
            method => method.Method.Definition.Name == "DecAddSub");
        var structured = new ValidatedStructuredMethodBuilderFactory()
            .Create(BranchPrefixDuplicationPolicy.Speed).Build(addition.ControlFlow);
        var conditional = Assert.IsType<StructuredIf>(structured.Body.Regions[0]);
        var cold = Blocks(conditional.WhenTrue).ToHashSet();
        var hot = Blocks(conditional.WhenFalse).ToHashSet();
        var condition = structured.Blocks[conditional.Condition.Block];
        var tail = Blocks(new StructuredSequence([.. structured.Body.Regions.Skip(1)])).ToHashSet();
        var addresses = addition.Body.Instructions.Where(instruction =>
            structured.InstructionEntryStacks.ContainsKey(instruction.Offset) &&
            instruction.Operation == CilOperation.LoadLocalAddress).ToArray();
        var scalar = Assert.Single(addresses.Select(CilOperandReader.GetIndex).Distinct(),
            index => addition.Body.LocalSignatureTypes[index].StackKind == CliValueKind.I8);
        Assert.Equal(2, addresses.Count(instruction => CilOperandReader.GetIndex(instruction) == scalar));
        Assert.Contains(condition.Instructions, instruction =>
            instruction.Operation == CilOperation.StoreLocal &&
            CilOperandReader.GetIndex(instruction) == scalar);
        Assert.DoesNotContain(condition.Instructions, instruction =>
            instruction.Operation == CilOperation.LoadLocalAddress);
        Assert.All(addresses, address =>
        {
            Assert.Contains(cold, block => structured.Blocks[block].Instructions.Contains(address));
            Assert.DoesNotContain(hot, block => structured.Blocks[block].Instructions.Contains(address));
            Assert.DoesNotContain(tail, block => structured.Blocks[block].Instructions.Contains(address));
        });
        using var metadata = MetadataCompilationTestFactory.Load(assembly, [assets.CoreLib]);
        var snapshot = metadata.Snapshot;
        var fields = MetadataCompilationTestActors.Fields(snapshot);
        var methods = MetadataCompilationTestActors.Methods(snapshot);
        var types = MetadataCompilationTestActors.Types(snapshot);
        var definitions = MetadataCompilationTestActors.TypeDefinitions(snapshot);
        var identities = new CilTypeIdentityResolver(types);
        var values = new ValueLayoutProvider(CreateValueLayouts(definitions, fields, compilation.Layouts));
        var layout = new ValueFrameLayoutPlanner(
            fields, methods, compilation.Layouts, values,
            new CilTypeOperandResolver(identities), identities,
            new ArgumentTypeResolver(types), new ArgumentSignatureTypeResolver(identities),
            new DirectValueLoadSelector(),
            new TransientAggregatePromotionPlanner(
                methods, new RuntimeIntrinsicRegistry(
                    MetadataCompilationTestActors.Symbols(snapshot), snapshot.Methods),
                values, CreateInstanceFields(compilation.Layouts))).Create(structured);
        Assert.Empty(layout.ArgumentOffsets);
        Assert.Empty(layout.NativeArgumentOffsets);
        Assert.Empty(layout.MemberResultOffsets);
        Assert.Empty(layout.CallReceiverOffsets);
        Assert.Equal(2, layout.LocalOffsets.Count);
        Assert.Contains(scalar, layout.SpilledScalarLocals);
        Assert.NotEmpty(layout.TemporaryOffsets);
        Assert.All(layout.TemporaryOffsets.Keys, offset =>
        {
            Assert.Contains(cold, block => structured.Blocks[block].Instructions.Any(
                instruction => instruction.Offset == offset));
            Assert.DoesNotContain(hot, block => structured.Blocks[block].Instructions.Any(
                instruction => instruction.Offset == offset));
            Assert.DoesNotContain(tail, block => structured.Blocks[block].Instructions.Any(
                instruction => instruction.Offset == offset));
        });
        var aggregate = Assert.Single(layout.LocalOffsets.Keys, index => index != scalar);
        Assert.DoesNotContain(hot.Concat(tail).Append(conditional.Condition.Block), block =>
            structured.Blocks[block].Instructions.Any(instruction =>
                instruction.Operation is CilOperation.LoadLocal or CilOperation.StoreLocal or
                    CilOperation.LoadLocalAddress &&
                CilOperandReader.GetIndex(instruction) == aggregate));
        Assert.Empty(structured.ExceptionGroups);
        Assert.NotEmpty(tail);
        Assert.All(structured.Body.Regions.Skip(1), region => Assert.IsType<StructuredCode>(region));
        Assert.All(tail, block => Assert.Empty(structured.Blocks[block].EntryStack));
        Assert.IsType<StructuredTerminalExit>(structured.Blocks[
            ((StructuredCode)structured.Body.Regions[^1]).Occurrence.Block].Exit);
        Assert.Contains(hot.Concat(tail), block => structured.Blocks[block].Exit is
            StructuredTerminalExit { Instruction.Operation: CilOperation.Return });
        Assert.Contains(cold.Concat(tail), block => structured.Blocks[block].Exit is
            StructuredTerminalExit { Instruction.Operation: CilOperation.Return });
        var placement = new[] { new TerminalBranchValueFramePlanner() }
            .Cast<ITerminalBranchValueFramePlanner>().Single();
        var roots = compilation.Program.RootMaps[addition.Method.Definition.Key];
        var plan = Assert.IsType<TerminalBranchValueFramePlan>(placement.Plan(structured, layout, roots));
        Assert.True(plan.FramedWhenTrue);
        Assert.Equal([scalar], plan.IncomingScalarLocals);
        Assert.Equal(conditional.Condition, plan.Condition);
        Assert.Equal(conditional.WhenTrue.Regions.Length + tail.Count, plan.WhenTrue.Regions.Length);
        Assert.Equal(conditional.WhenFalse.Regions.Length + tail.Count, plan.WhenFalse.Regions.Length);
        var unframedOffsets = hot.Concat(tail).Append(conditional.Condition.Block)
            .SelectMany(block => structured.Blocks[block].Instructions)
            .Select(instruction => instruction.Offset).ToHashSet();
        var unframedRoots = roots.Safepoints.Values
            .Where(point => unframedOffsets.Contains(point.IlOffset))
            .SelectMany(point => point.Roots.Concat(point.ConstructorCallRoots));
        Assert.DoesNotContain(unframedRoots, root =>
            root.Kind is RootSourceKind.Local or RootSourceKind.ManagedAddressLocal &&
            layout.LocalOffsets.ContainsKey(root.Index));
        Assert.DoesNotContain(unframedRoots, root => root.Kind == RootSourceKind.StackStorageAnchor);

        static IEnumerable<StructuredBlockId> Blocks(StructuredRegion region)
        {
            IEnumerable<StructuredRegion> children = [];
            switch (region)
            {
                case StructuredCode code:
                    yield return code.Occurrence.Block;
                    break;
                case StructuredSequence sequence:
                    children = sequence.Regions;
                    break;
                case StructuredIf branch:
                    yield return branch.Condition.Block;
                    children = [branch.WhenTrue, branch.WhenFalse];
                    break;
                case StructuredLoop loop:
                    if (loop.Condition is { } condition)
                    {
                        yield return condition.Block;
                    }
                    children = [loop.Body, loop.ContinueBody, loop.ExitBody];
                    break;
                case StructuredPostTestLoop loop:
                    yield return loop.Condition.Block;
                    children = [loop.Body, loop.ContinueBody, loop.ExitBody];
                    break;
                case StructuredDispatcher dispatcher:
                    foreach (var block in dispatcher.Blocks)
                        yield return block.Occurrence.Block;
                    children = dispatcher.Exits.Select(exit => exit.Body);
                    break;
            }
            foreach (var child in children)
                foreach (var block in Blocks(child))
                    yield return block;
        }
    }

    [Fact]
    public void SpeedPolicySeparatesAlignedDecimalAdditionFromTheColdDispatcher()
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileOptimizedSource(
            "DecimalAdditionStructureFixture",
            """
            public static class EntryPoint
            {
                public static int Run(int input)
                {
                    decimal sum = input;
                    for (int index = 0; index < 4; index++) sum += 2.25m;
                    return (int)sum;
                }
            }
            """);
        var compilation = NetWasmCompiler.Compile(new CompilerOptions(
            assembly, [assets.CoreLib], "EntryPoint", "Run", [],
            Optimization: FinalWasmOptimization.O3));
        var addition = Assert.Single(compilation.Program.Methods.Values,
            method => method.Method.Definition.Name == "DecAddSub");
        var factory = new ValidatedStructuredMethodBuilderFactory();
        var sizeAware = factory.Create(BranchPrefixDuplicationPolicy.SizeAware)
            .Build(addition.ControlFlow);
        var speed = factory.Create(BranchPrefixDuplicationPolicy.Speed)
            .Build(addition.ControlFlow);

        Assert.Contains(Regions(sizeAware.Body).OfType<StructuredDispatcher>(),
            dispatcher => dispatcher.Blocks.Length > addition.ControlFlow.Graph.Blocks.Length / 2);
        Assert.Equal(42, ExecuteWithNode(compilation.ApplicationModule, assets.Directory, 33));
        var sizeAwareLargest = Regions(sizeAware.Body).OfType<StructuredDispatcher>()
            .Select(dispatcher => dispatcher.Blocks.Length).DefaultIfEmpty().Max();
        var speedLargest = Regions(speed.Body).OfType<StructuredDispatcher>()
            .Select(dispatcher => dispatcher.Blocks.Length).DefaultIfEmpty().Max();
        Assert.True(speedLargest < sizeAwareLargest,
            $"dispatcher-counts: size={sizeAwareLargest}, speed={speedLargest}");
        var entry = Assert.IsType<StructuredIf>(speed.Body.Regions[0]);
        Assert.DoesNotContain(Regions(entry.WhenFalse),
            region => region is StructuredDispatcher);

        static IEnumerable<StructuredRegion> Regions(StructuredRegion region)
        {
            yield return region;
            IEnumerable<StructuredRegion> children = region switch
            {
                StructuredSequence sequence => sequence.Regions,
                StructuredIf conditional => [conditional.WhenTrue, conditional.WhenFalse],
                StructuredLoop loop => [loop.Body, loop.ContinueBody, loop.ExitBody],
                StructuredPostTestLoop loop => [loop.Body, loop.ContinueBody, loop.ExitBody],
                StructuredDispatcher dispatcher => dispatcher.Exits.Select(exit => exit.Body),
                _ => [],
            };
            foreach (var child in children)
                foreach (var descendant in Regions(child))
                    yield return descendant;
        }
    }

    [Fact]
    public void O3ComposesBranchingDecimalRoundBeforeManagedEmission()
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileOptimizedSource(
            "DecimalRoundCompositionFixture",
            """
            using System;

            public static class EntryPoint
            {
                public static int Run(int input) =>
                    decimal.Round(input / 100m, 2, MidpointRounding.ToEven) == 1.23m
                        ? 42
                        : 0;
            }
            """);

        var compilation = NetWasmCompiler.Compile(new CompilerOptions(
            assembly,
            [assets.CoreLib],
            "EntryPoint",
            "Run",
            [],
            Optimization: FinalWasmOptimization.O3));

        // decimal.Round forwards to the private Round(ref decimal, int, MidpointRounding),
        // a branching callee that takes the decimal by reference: O3 composes it into the
        // public overload.
        Assert.DoesNotContain(compilation.Program.Methods.Values, method =>
            method.Method.Definition.Name == "Round" &&
            method.Method.Signature.ParameterSignatureTypes.Any(static type =>
                type.Shape == CliTypeShape.ManagedByReference));
        var composed = Assert.Single(compilation.Program.Methods.Values, method =>
            method.Method.Definition.Name == "Round" &&
            method.Method.DeclaringType?.FullName == "System.Decimal");
        Assert.Contains(composed.Body.Instructions, instruction =>
            instruction.Operand is CilOperand.BranchTarget);
        Assert.Contains(composed.Body.Instructions, instruction =>
            instruction.Operation is CilOperation.LoadArgumentAddress or CilOperation.LoadLocalAddress);
        // That inlining exposes the call to InternalRound, which stays a call, as .NET keeps it.
        Assert.Contains(composed.Body.Instructions, instruction =>
            instruction.Operation == CilOperation.Call &&
            instruction.Operand is CilOperand.MethodInstance called &&
            called.Value.Definition.Name == "InternalRound");
        var internalRound = Assert.Single(compilation.Program.Methods.Values, method =>
            method.Method.Definition.Name == "InternalRound");
        Assert.Contains(internalRound.Body.Instructions, instruction =>
            instruction.Operation == CilOperation.LoadStaticDataElement &&
            instruction.Operand is CilOperand.ReadOnlySpanData data &&
            data.ElementType.CanonicalName == "primitive:u4");
        Assert.DoesNotContain(internalRound.Body.Instructions, instruction =>
            instruction.Operation == CilOperation.Call &&
            instruction.Operand is CilOperand.MethodInstance called &&
            (called.Value.Definition.Name == "get_UInt32Powers10" ||
             called.Value.Definition.Name == "get_Item" &&
             called.Value.DeclaringType.CanonicalName.Contains("ReadOnlySpan", StringComparison.Ordinal)));
        Assert.Equal(42, ExecuteWithNode(
            compilation.ApplicationModule,
            assets.Directory,
            123));
    }

    [Fact]
    public void Decimal128ScientificFormattingRetainsTheCompleteExponent()
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSource(
            "Decimal128ScientificFormatting",
            """
            using System.Globalization;
            using System.Numerics;

            namespace Decimal128ScientificFormatting;

            public static class EntryPoint
            {
                public static int Run(int input)
                {
                    var provider = CultureInfo.InvariantCulture;
                    if (Decimal128.Parse("1E+6144", provider).ToString("E0", provider) != "1E+6144") return 1;
                    if (Decimal128.Parse("1E-6143", provider).ToString("e0", provider) != "1e-6143") return 2;
                    if (Decimal128.Parse("9.99E+999", provider).ToString("E0", provider) != "1E+1000") return 3;
                    return 42;
                }
            }
            """);
        var compilation = NetWasmCompiler.Compile(new CompilerOptions(
            assembly,
            [assets.CoreLib],
            "Decimal128ScientificFormatting.EntryPoint",
            "Run",
            []));

        Assert.Equal(42, ExecuteWithNode(
            compilation.ApplicationModule,
            assets.Directory,
            0));
    }

    [Theory]
    [InlineData(false, WasmTarget.Wasm32)]
    [InlineData(true, WasmTarget.Wasm32)]
    [InlineData(false, WasmTarget.Wasm64)]
    [InlineData(true, WasmTarget.Wasm64)]
    public void CompilerPreservesDecimalAcrossMutableByValueCalls(
        bool optimize, WasmTarget target)
    {
        using var assets = TestAssets.Create();
        ICompilationScenarioExecutor executor = new CompilationScenarioExecutor(assets);
        var observed = executor.Execute(new CompilationScenario(
            $"DecimalByValueOwnership_{optimize}_{target}",
            """
            using System;

            public static class EntryPoint
            {
                public static int Run(int cents)
                {
                    var expected = 121.91m;
                    var first = CalculateDelta(cents);
                    if (first != expected) return 1;
                    var snapshot = new decimal[] { first };
                    if (snapshot[0] != expected) return 2;
                    var total = 0m;
                    total += first;
                    if (first != expected) return 3;
                    if (total != expected) return 4;
                    if (snapshot[0] != expected) return 5;
                    var second = CalculateDelta(cents);
                    if (second != expected) return 6;
                    if (first != expected) return 7;
                    var amount = cents / 100m;
                    var fee = decimal.Round(amount * 0.0125m, 2, MidpointRounding.ToEven);
                    var inline = amount - fee;
                    if (inline != expected) return 8;
                    if (Subtract(amount, fee) != expected) return 9;
                    if (Identity(inline) != expected) return 10;
                    return 42;
                }

                private static decimal CalculateDelta(int value)
                {
                    var amount = value / 100m;
                    var fee = decimal.Round(amount * 0.0125m, 2, MidpointRounding.ToEven);
                    return amount - fee;
                }

                private static decimal Subtract(decimal left, decimal right) => left - right;
                private static decimal Identity(decimal value) => value;
            }
            """,
            "EntryPoint", optimize, target, 12345, []));

        Assert.Equal(42, observed);
    }

    [Theory]
    [InlineData(false, WasmTarget.Wasm32)]
    [InlineData(true, WasmTarget.Wasm32)]
    [InlineData(false, WasmTarget.Wasm64)]
    [InlineData(true, WasmTarget.Wasm64)]
    public void CompilerPreservesTheUpstreamDecimalPhysicalLayout(
        bool optimize,
        WasmTarget target)
    {
        using var assets = TestAssets.Create();
        ICompilationScenarioExecutor executor = new CompilationScenarioExecutor(assets);
        var observed = executor.Execute(new CompilationScenario(
            $"DecimalPhysicalLayoutFixture_{optimize}_{target}",
            """
            using System;
            using System.Runtime.InteropServices;

            [StructLayout(LayoutKind.Sequential)]
            public struct Envelope
            {
                public byte Prefix;
                public decimal Value;
                public byte Suffix;
            }

            public static class EntryPoint
            {
                public static unsafe int Run(int input)
                {
                    var value = new decimal(1, 2, 3, true, 4);
                    var words = (int*)&value;
                    if (words[0] != unchecked((int)0x80040000)) return 1;
                    if (words[1] != 3) return 2;
                    if (words[2] != 1) return 3;
                    if (words[3] != 2) return 4;
                    if (sizeof(decimal) != 16) return 5;

                    var bits = decimal.GetBits(value);
                    if (bits[0] != 1 || bits[1] != 2 || bits[2] != 3 ||
                        bits[3] != unchecked((int)0x80040000)) return 6;

                    Envelope envelope = default;
                    if ((byte*)&envelope.Value - (byte*)&envelope != 8) return 7;
                    if ((byte*)&envelope.Suffix - (byte*)&envelope != 24) return 8;
                    if (sizeof(Envelope) != 32) return 9;

                    var literal = 12.34m;
                    var literalBits = decimal.GetBits(literal);
                    if (literalBits[0] != 1234 || literalBits[1] != 0 ||
                        literalBits[2] != 0 || literalBits[3] != 0x00020000) return 10;
                    return 42;
                }
            }
            """,
            "EntryPoint",
            optimize,
            target,
            41,
            [])
        {
            AllowUnsafe = true,
        });

        Assert.Equal(42, observed);
    }

    [Theory]
    [InlineData(false, WasmTarget.Wasm32)]
    [InlineData(true, WasmTarget.Wasm32)]
    [InlineData(false, WasmTarget.Wasm64)]
    [InlineData(true, WasmTarget.Wasm64)]
    public void CompilerPreservesDecimalDivisionOperandsAcrossDivisorWidths(
        bool optimize,
        WasmTarget target)
    {
        var cases = new (decimal Left, decimal Right)[]
        {
            (new decimal(-1, -1, -1, false, 7), new decimal(7, 0, 0, true, 3)),
            (new decimal(-1, -1, -1, true, 7), new decimal(1, 2, 0, false, 4)),
            (new decimal(-1, -1, -1, false, 7), new decimal(1, 2, 3, true, 5)),
            (new decimal(0, 0, 0, true, 12), new decimal(1, 2, 3, false, 5)),
        };
        var statements = new System.Text.StringBuilder();
        for (var index = 0; index < cases.Length; index++)
        {
            var (left, right) = cases[index];
            statements.Append("var result")
                .Append(index)
                .Append(" = Check(")
                .Append(DecimalExpression(left))
                .Append(", ")
                .Append(DecimalExpression(right))
                .Append(", ")
                .Append(DecimalExpression(left / right))
                .Append(", ")
                .Append(index * 10 + 1)
                .Append("); if (result")
                .Append(index)
                .Append(" != 0) return result")
                .Append(index)
                .AppendLine(";");
        }

        using var assets = TestAssets.Create();
        ICompilationScenarioExecutor executor = new CompilationScenarioExecutor(assets);
        var observed = executor.Execute(new CompilationScenario(
            $"DecimalDivisionOperandFixture_{optimize}_{target}",
            $$"""
            using System;

            public static class EntryPoint
            {
                public static int Run(int input)
                {
                    {{statements}}
                    return input - input;
                }

                private static int Check(
                    decimal left,
                    decimal right,
                    decimal expected,
                    int failure)
                {
                    var leftBits = decimal.GetBits(left);
                    var rightBits = decimal.GetBits(right);

                    var operatorResult = Identity(left) / Identity(right);
                    if (!SameBits(operatorResult, expected))
                        return failure;
                    if (!SameBits(left, leftBits))
                        return failure + 1;
                    if (!SameBits(right, rightBits))
                        return failure + 2;

                    var methodResult = decimal.Divide(Identity(left), Identity(right));
                    if (!SameBits(methodResult, expected))
                        return failure + 3;
                    if (!SameBits(left, leftBits))
                        return failure + 4;
                    if (!SameBits(right, rightBits))
                        return failure + 5;

                    return 0;
                }

                private static decimal Identity(decimal value) => value;

                private static bool SameBits(decimal left, decimal right) =>
                    SameBits(left, decimal.GetBits(right));

                private static bool SameBits(decimal value, int[] expected)
                {
                    var actual = decimal.GetBits(value);
                    return actual[0] == expected[0] &&
                        actual[1] == expected[1] &&
                        actual[2] == expected[2] &&
                        actual[3] == expected[3];
                }
            }
            """,
            "EntryPoint",
            optimize,
            target,
            41,
            []));

        Assert.Equal(0, observed);
    }

    [Fact]
    public void CompilerPreservesZeroScaleAndSignAcrossDecimalDivision()
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSource(
            "DecimalZeroDivisionFixture",
            """
            namespace DecimalZeroDivisionFixture;

            public static class EntryPoint
            {
                public static int Run(int input)
                {
                    var positive = decimal.GetBits(
                        Identity(new decimal(0, 0, 0, false, 2)) / 1m);
                    var negative = decimal.GetBits(
                        Identity(new decimal(0, 0, 0, true, 2)) / 1m);
                    if ((positive[0] | positive[1] | positive[2] |
                         negative[0] | negative[1] | negative[2]) != 0)
                        return -1;
                    return ((positive[3] >> 16) & 0xff) |
                        (((negative[3] >> 16) & 0xff) << 8) |
                        (negative[3] < 0 ? 1 << 16 : 0);
                }

                private static decimal Identity(decimal value) => value;
            }
            """);
        var compilation = NetWasmCompiler.Compile(new CompilerOptions(
            assembly,
            [assets.CoreLib],
            "DecimalZeroDivisionFixture.EntryPoint",
            "Run",
            []));

        Assert.Equal(0x10202, ExecuteWithNode(
            compilation.ApplicationModule,
            assets.Directory,
            41));
    }

    [Fact]
    public void CompilerMatchesDesktopDecimalBoundaryCorpusExactly()
    {
        using var assets = TestAssets.Create();
        var cases = new List<(string Expression, Func<decimal> Evaluate)>
        {
            ("decimal.MaxValue / 10m", () => decimal.MaxValue / 10m),
            ("decimal.MaxValue % 10m", () => decimal.MaxValue % 10m),
            ("decimal.MaxValue * 0.1m", () => decimal.MaxValue * 0.1m),
            ("decimal.MinValue / -7m", () => decimal.MinValue / -7m),
            ("1m / 3m", () => 1m / 3m),
            ("2m / 3m", () => 2m / 3m),
            ("1.00m / 2m", () => 1.00m / 2m),
            ("1m / 0.1m", () => 1m / 0.1m),
            ("decimal.MaxValue / 7m", () => decimal.MaxValue / 7m),
            ("Identity(decimal.MaxValue) / 0.1m",
                () => DecimalIdentity(decimal.MaxValue) / 0.1m),
            ("0.0000000000000000000000000001m / 2m",
                () => 0.0000000000000000000000000001m / 2m),
            ("-12345678901234567890.123456789m % 97.0001m",
                () => -12345678901234567890.123456789m % 97.0001m),
            ("decimal.Round(9999999999999999999999999999.5m, 0)",
                () => decimal.Round(9999999999999999999999999999.5m, 0)),
            ("decimal.Round(-2.5000000000000000000000000001m, 0, " +
             "MidpointRounding.ToEven)",
                () => decimal.Round(-2.5000000000000000000000000001m, 0,
                    MidpointRounding.ToEven)),
            ("Identity(71305346262837903834189555303m) / 9m",
                () => DecimalIdentity(71305346262837903834189555303m) / 9m),
            ("Identity(55459713759985036315480765235m) / 7m",
                () => DecimalIdentity(55459713759985036315480765235m) / 7m),
            ("Identity(new decimal(0, 0, 0, false, 2)) / 1m",
                () => DecimalIdentity(new decimal(0, 0, 0, false, 2)) / 1m),
            ("Identity(new decimal(0, 0, 0, true, 2)) / 1m",
                () => DecimalIdentity(new decimal(0, 0, 0, true, 2)) / 1m),
            ("1m / Identity(new decimal(-1, 0, 0, false, 0))",
                () => 1m / DecimalIdentity(new decimal(-1, 0, 0, false, 0))),
            ("decimal.MaxValue / Identity(new decimal(0, 1, 0, false, 0))",
                () => decimal.MaxValue / DecimalIdentity(new decimal(0, 1, 0, false, 0))),
            ("Identity(1m) / new decimal(3, 0, 0, false, 28)",
                () => DecimalIdentity(1m) / new decimal(3, 0, 0, false, 28)),
            ("Identity(1m) / new decimal(3, 0, 0, false, 9)",
                () => DecimalIdentity(1m) / new decimal(3, 0, 0, false, 9)),
            ("Identity(1m) / new decimal(3, 0, 0, false, 10)",
                () => DecimalIdentity(1m) / new decimal(3, 0, 0, false, 10)),
            ("Identity(1m) / new decimal(4, 0, 0, false, 28)",
                () => DecimalIdentity(1m) / new decimal(4, 0, 0, false, 28)),
            ("Identity(23m) / new decimal(3, 0, 0, false, 28)",
                () => DecimalIdentity(23m) / new decimal(3, 0, 0, false, 28)),
            ("Identity(25m) / new decimal(3, 0, 0, false, 28)",
                () => DecimalIdentity(25m) / new decimal(3, 0, 0, false, 28)),
            ("Identity(250m) / new decimal(3, 0, 0, false, 28)",
                () => DecimalIdentity(250m) / new decimal(3, 0, 0, false, 28)),
            ("Identity(-1m) / new decimal(3, 0, 0, true, 28)",
                () => DecimalIdentity(-1m) / new decimal(3, 0, 0, true, 28)),
            ("decimal.Floor(Identity(-1.0000000000000000000000000001m))",
                () => decimal.Floor(DecimalIdentity(-1.0000000000000000000000000001m))),
            ("decimal.Ceiling(Identity(1.0000000000000000000000000001m))",
                () => decimal.Ceiling(DecimalIdentity(1.0000000000000000000000000001m))),
            ("decimal.Truncate(Identity(-1.9999999999999999999999999999m))",
                () => decimal.Truncate(DecimalIdentity(-1.9999999999999999999999999999m))),
            ("Identity(decimal.MaxValue) + 1m", () => DecimalIdentity(decimal.MaxValue) + 1m),
            ("Identity(decimal.MaxValue) * 2m", () => DecimalIdentity(decimal.MaxValue) * 2m),
            ("Identity(new decimal(1, 0, 0, true, 2)) + new decimal(1, 0, 0, false, 2)",
                () => DecimalIdentity(new decimal(1, 0, 0, true, 2)) +
                    new decimal(1, 0, 0, false, 2)),
            ("Identity(new decimal(1, 0, 0, false, 2)) + new decimal(1, 0, 0, true, 2)",
                () => DecimalIdentity(new decimal(1, 0, 0, false, 2)) +
                    new decimal(1, 0, 0, true, 2)),
            ("Identity(new decimal(1, 0, 0, true, 2)) - new decimal(1, 0, 0, true, 2)",
                () => DecimalIdentity(new decimal(1, 0, 0, true, 2)) -
                    new decimal(1, 0, 0, true, 2)),
            ("Identity(new decimal(-1, 0, 0, false, 2)) + new decimal(1, 0, 0, false, 2)",
                () => DecimalIdentity(new decimal(-1, 0, 0, false, 2)) +
                    new decimal(1, 0, 0, false, 2)),
            ("Identity(new decimal(-1, -1, 0, false, 2)) + new decimal(1, 0, 0, false, 2)",
                () => DecimalIdentity(new decimal(-1, -1, 0, false, 2)) +
                    new decimal(1, 0, 0, false, 2)),
            ("Identity(new decimal(0, 1, 1, false, 2)) - new decimal(1, 0, 0, false, 2)",
                () => DecimalIdentity(new decimal(0, 1, 1, false, 2)) -
                    new decimal(1, 0, 0, false, 2)),
            ("Identity(new decimal(1, 0, 0, false, 2)) - new decimal(0, 1, 1, false, 2)",
                () => DecimalIdentity(new decimal(1, 0, 0, false, 2)) -
                    new decimal(0, 1, 1, false, 2)),
            ("Identity(new decimal(-1, -1, -1, false, 1)) + new decimal(-1, -1, -1, false, 1)",
                () => DecimalIdentity(new decimal(-1, -1, -1, false, 1)) +
                    new decimal(-1, -1, -1, false, 1)),
            ("Identity(new decimal(3, 0, 0, false, 14)) * new decimal(5, 0, 0, false, 14)",
                () => DecimalIdentity(new decimal(3, 0, 0, false, 14)) *
                    new decimal(5, 0, 0, false, 14)),
            ("Identity(new decimal(3, 0, 0, false, 15)) * new decimal(5, 0, 0, false, 14)",
                () => DecimalIdentity(new decimal(3, 0, 0, false, 15)) *
                    new decimal(5, 0, 0, false, 14)),
            ("Identity(new decimal(2, 0, 0, false, 15)) * new decimal(7, 0, 0, false, 14)",
                () => DecimalIdentity(new decimal(2, 0, 0, false, 15)) *
                    new decimal(7, 0, 0, false, 14)),
            ("Identity(new decimal(4, 0, 0, false, 15)) * new decimal(4, 0, 0, false, 14)",
                () => DecimalIdentity(new decimal(4, 0, 0, false, 15)) *
                    new decimal(4, 0, 0, false, 14)),
            ("Identity(new decimal(-1, 0, 0, false, 28)) * new decimal(-1, 0, 0, false, 19)",
                () => DecimalIdentity(new decimal(-1, 0, 0, false, 28)) *
                    new decimal(-1, 0, 0, false, 19)),
            ("Identity(new decimal(-1, 0, 0, true, 28)) * new decimal(-1, 0, 0, false, 20)",
                () => DecimalIdentity(new decimal(-1, 0, 0, true, 28)) *
                    new decimal(-1, 0, 0, false, 20)),
            ("Identity(new decimal(1, 0, 0, true, 28)) * new decimal(1, 0, 0, false, 28)",
                () => DecimalIdentity(new decimal(1, 0, 0, true, 28)) *
                    new decimal(1, 0, 0, false, 28)),
            ("Identity(new decimal(0, 0, 0, true, 2)) * new decimal(1, 0, 0, false, 3)",
                () => DecimalIdentity(new decimal(0, 0, 0, true, 2)) *
                    new decimal(1, 0, 0, false, 3)),
            ("Identity(new decimal(0, 0, 0, true, 2)) * new decimal(0, 1, 0, false, 3)",
                () => DecimalIdentity(new decimal(0, 0, 0, true, 2)) *
                    new decimal(0, 1, 0, false, 3)),
            ("1m / Identity(decimal.Zero)", () => 1m / DecimalIdentity(decimal.Zero)),
        };
        var targetedRoundCases = new (decimal Value, int Digits, MidpointRounding Mode)[]
        {
            (1.500000000m, 0, MidpointRounding.ToEven),
            (1.500000000000000000m, 0, MidpointRounding.ToEven),
            (1.500000000000000000000000000m, 0, MidpointRounding.ToEven),
            (1.5000000000000000000000000000m, 0, MidpointRounding.ToEven),
            (2.5000000000m, 0, MidpointRounding.ToEven),
            (2.5000000001m, 0, MidpointRounding.ToEven),
            (-2.5000000000m, 0, MidpointRounding.ToEven),
            (-2.5000000001m, 0, MidpointRounding.ToEven),
            (4294967295.5m, 0, MidpointRounding.ToEven),
            (18446744073709551615.5m, 0, MidpointRounding.ToEven),
            (0.0000000000000000000000000001m, 0, MidpointRounding.ToEven),
            (-0.0000000000000000000000000001m, 0, MidpointRounding.ToEven),
            (2.9m, 0, MidpointRounding.ToZero),
            (-2.1m, 0, MidpointRounding.ToNegativeInfinity),
            (2.1m, 0, MidpointRounding.ToPositiveInfinity),
            (2.5m, 0, MidpointRounding.AwayFromZero),
            (new decimal(125, 0, 0, true, 2), 4, MidpointRounding.ToEven),
        };
        foreach (var (value, digits, mode) in targetedRoundCases)
        {
            cases.Add((
                $"decimal.Round({DecimalExpression(value)}, {digits}, " +
                $"MidpointRounding.{mode})",
                () => decimal.Round(value, digits, mode)));
        }
        var random = new Random(0x4e657457);
        for (var index = 0; index < 12; index++)
        {
            var left = new decimal(
                unchecked((int)random.NextInt64()),
                unchecked((int)random.NextInt64()),
                unchecked((int)random.NextInt64()),
                random.Next(2) != 0,
                (byte)random.Next(29));
            var right = new decimal(
                unchecked((int)random.NextInt64()),
                unchecked((int)random.NextInt64()),
                unchecked((int)random.NextInt64()),
                random.Next(2) != 0,
                (byte)random.Next(29));
            var leftExpression = DecimalExpression(left);
            var rightExpression = DecimalExpression(right);
            cases.Add(($"{leftExpression} + {rightExpression}", () => left + right));
            cases.Add(($"{leftExpression} - {rightExpression}", () => left - right));
            cases.Add(($"{leftExpression} * {rightExpression}", () => left * right));
            cases.Add(($"{leftExpression} / {rightExpression}", () => left / right));
            cases.Add(($"{leftExpression} % {rightExpression}", () => left % right));
        }
        for (var index = 0; index < 64; index++)
        {
            var left = new decimal(
                unchecked((int)random.NextInt64()),
                unchecked((int)random.NextInt64()),
                unchecked((int)random.NextInt64()),
                random.Next(2) != 0,
                (byte)random.Next(29));
            var right = new decimal(
                unchecked((int)random.NextInt64()),
                unchecked((int)random.NextInt64()),
                unchecked((int)random.NextInt64()),
                random.Next(2) != 0,
                (byte)random.Next(29));
            cases.Add(($"{DecimalExpression(left)} / {DecimalExpression(right)}", () => left / right));
        }
        for (var index = 0; index < 96; index++)
        {
            var left = new decimal(
                unchecked((int)random.NextInt64()),
                unchecked((int)random.NextInt64()),
                unchecked((int)random.NextInt64()),
                random.Next(2) != 0,
                (byte)random.Next(29));
            var denominator = (uint)random.NextInt64(1, (long)uint.MaxValue + 1);
            var right = new decimal(
                unchecked((int)denominator),
                0,
                0,
                random.Next(2) != 0,
                (byte)random.Next(29));
            cases.Add(($"{DecimalExpression(left)} / {DecimalExpression(right)}", () => left / right));
        }
        for (var index = 0; index < 96; index++)
        {
            var scale = (byte)(index % 29);
            var value = new decimal(
                unchecked((int)random.NextInt64()),
                unchecked((int)random.NextInt64()),
                unchecked((int)random.NextInt64()),
                random.Next(2) != 0,
                scale);
            var digits = index % 4 switch
            {
                0 => 0,
                1 => scale / 2,
                2 => scale == 0 ? 0 : scale - 1,
                _ => Math.Min(28, scale + 1),
            };
            var mode = (MidpointRounding)(index % 5);
            cases.Add((
                $"decimal.Round({DecimalExpression(value)}, {digits}, " +
                $"MidpointRounding.{mode})",
                () => decimal.Round(value, digits, mode)));
        }
        var statements = new System.Text.StringBuilder();
        for (var index = 0; index < cases.Count; index++)
        {
            try
            {
                var expected = cases[index].Evaluate();
                statements.Append("try { if (!SameBits(")
                    .Append(cases[index].Expression)
                    .Append(", ")
                    .Append(DecimalExpression(expected))
                    .Append(")) return ")
                    .Append(index + 1)
                    .Append("; } catch (OverflowException) { return ")
                    .Append(1000 + index + 1)
                    .Append("; } catch (DivideByZeroException) { return ")
                    .Append(2000 + index + 1)
                    .AppendLine("; }");
            }
            catch (OverflowException)
            {
                statements.Append("try { _ = ")
                    .Append(cases[index].Expression)
                    .Append("; return ")
                    .Append(index + 1)
                    .AppendLine("; } catch (OverflowException) { }");
            }
            catch (DivideByZeroException)
            {
                statements.Append("try { _ = ")
                    .Append(cases[index].Expression)
                    .Append("; return ")
                    .Append(index + 1)
                    .AppendLine("; } catch (DivideByZeroException) { }");
            }
        }
        var source = $$"""
            using System;

            namespace DecimalBoundaryFixture;

            public static class EntryPoint
            {
                public static int Run(int input)
                {
                    {{statements}}
                    return input - input;
                }

                private static bool SameBits(decimal left, decimal right)
                {
                    var leftBits = decimal.GetBits(left);
                    var rightBits = decimal.GetBits(right);
                    return leftBits[0] == rightBits[0] &&
                        leftBits[1] == rightBits[1] &&
                        leftBits[2] == rightBits[2] &&
                        leftBits[3] == rightBits[3];
                }

                private static decimal Identity(decimal value) => value;
            }
            """;
        var assembly = assets.CompileSource("DecimalBoundaryFixture", source);
        var compilation = NetWasmCompiler.Compile(new CompilerOptions(
            assembly,
            [assets.CoreLib],
            "DecimalBoundaryFixture.EntryPoint",
            "Run",
            []));

        var actual = ExecuteWithNode(
            compilation.ApplicationModule,
            assets.Directory,
            41);
        var failedCase = actual >= 2000
            ? actual - 2000
            : actual >= 1000 ? actual - 1000 : actual;
        Assert.InRange(failedCase, 0, cases.Count);
        Assert.True(
            actual == 0,
            failedCase == 0
                ? ""
                : $"case {failedCase}: {cases[failedCase - 1].Expression}; " +
                  $"expected={cases[failedCase - 1].Evaluate()}; result={actual}");
    }

    [Fact]
    public void CompilerExecutesDecimalLiteralsArithmeticComparisonAndConversions()
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSource(
            "DecimalArithmeticFixture",
            """
            namespace DecimalArithmeticFixture;

            public static class EntryPoint
            {
                public static int Run(int input)
                {
                    var left = 12.5m;
                    var right = 2.25m;
                    var result = 0;
                    if (left + right == 14.75m) result |= 1;
                    if (left - right == 10.25m) result |= 2;
                    if (left * right == 28.125m) result |= 4;
                    if (left / right > 5.55m && left / right < 5.56m) result |= 8;
                    if (left % right == 1.25m) result |= 16;
                    if ((int)decimal.Truncate(left) == 12) result |= 32;
                    if (decimal.Round(2.5m) == 2m && decimal.Round(3.5m) == 4m)
                        result |= 64;
                    return result + input - input;
                }
            }
            """);

        var result = NetWasmCompiler.Compile(new CompilerOptions(
            assembly,
            [assets.CoreLib],
            "DecimalArithmeticFixture.EntryPoint",
            "Run",
            []));

        Assert.Equal(127, ExecuteWithNode(result.ApplicationModule, assets.Directory, 41));
    }

    [Fact]
    public void CompilerProducesManagedDecimalExceptions()
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSource(
            "DecimalExceptionFixture",
            """
            namespace DecimalExceptionFixture;

            public static class EntryPoint
            {
                public static int Run(int input)
                {
                    var result = 0;
                    // decimal.MaxValue is a constant: a local keeps the overflows at run time.
                    var maximum = decimal.MaxValue;
                    try { _ = maximum + 1m; }
                    catch (System.OverflowException) { result |= 1; }
                    try { _ = 1m / (input - input); }
                    catch (System.DivideByZeroException) { result |= 2; }
                    try { _ = (int)maximum; }
                    catch (System.OverflowException) { result |= 4; }
                    return result;
                }
            }
            """);

        var result = NetWasmCompiler.Compile(new CompilerOptions(
            assembly,
            [assets.CoreLib],
            "DecimalExceptionFixture.EntryPoint",
            "Run",
            []));

        Assert.Equal(7, ExecuteWithNode(result.ApplicationModule, assets.Directory, 41));
    }

    [Fact]
    public void CompilerMatchesSupportedDecimalBehaviorMatrix()
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSource(
            "DecimalBehaviorFixture",
            """
            namespace DecimalBehaviorFixture;

            public static class EntryPoint
            {
                public static int Run(int input)
                {
                    var result = 0;
                    var one = new decimal(10, 0, 0, false, 1);
                    if (one == 1m && one.GetHashCode() == 1m.GetHashCode()) result |= 1;
                    var negativeZero = new decimal(0, 0, 0, true, 4);
                    if (negativeZero == 0m && negativeZero.GetHashCode() == 0) result |= 2;
                    if (decimal.Round(2.5m, System.MidpointRounding.ToEven) == 2m &&
                        decimal.Round(2.5m, System.MidpointRounding.AwayFromZero) == 3m &&
                        decimal.Round(-2.1m, System.MidpointRounding.ToNegativeInfinity) == -3m &&
                        decimal.Round(-2.9m, System.MidpointRounding.ToPositiveInfinity) == -2m)
                        result |= 4;
                    if (decimal.Floor(-1.1m) == -2m && decimal.Ceiling(-1.1m) == -1m &&
                        decimal.Truncate(-1.9m) == -1m) result |= 8;
                    if (decimal.TryParse("-123.4500", out var parsed) &&
                        parsed == -123.45m && parsed.ToString() == "-123.4500") result |= 16;
                    if (decimal.Parse("  +1.25e2  ",
                            System.Globalization.NumberStyles.Float, null) == 125m &&
                        !decimal.TryParse("1e2", out _) &&
                        !decimal.TryParse("1.2.3", out _)) result |= 32;
                    if ((decimal)18446744073709551615UL == 18446744073709551615m &&
                        (ulong)18446744073709551615m == 18446744073709551615UL &&
                        (long)-9223372036854775808m == long.MinValue) result |= 64;
                    if ((decimal)0.5 == 0.5m && (double)12.25m == 12.25) result |= 128;
                    if (decimal.Abs(-3m) == 3m && decimal.Sign(-3m) == -1 &&
                        decimal.Min(2m, 3m) == 2m && decimal.Max(2m, 3m) == 3m &&
                        decimal.Clamp(5m, 1m, 4m) == 4m) result |= 256;
                    try { _ = decimal.Parse("not-a-number"); }
                    catch (System.FormatException) { result |= 512; }
                    try { _ = decimal.Clamp(1m, 2m, 0m); }
                    catch (System.ArgumentException) { result |= 1024; }
                    var bits = decimal.GetBits(new decimal(1, 2, 3, true, 4));
                    if (bits.Length == 4 && bits[0] == 1 && bits[1] == 2 && bits[2] == 3 &&
                        bits[3] == unchecked((int)0x80040000)) result |= 2048;
                    try
                    {
                        _ = decimal.Parse("1e1000",
                            System.Globalization.NumberStyles.Float, null);
                    }
                    catch (System.OverflowException) { result |= 4096; }
                    System.IComparable comparable = 2m;
                    System.IComparable<decimal> genericComparable = 2m;
                    if (comparable.CompareTo(null) == 1 && comparable.CompareTo(3m) < 0 &&
                        genericComparable.CompareTo(1m) > 0) result |= 8192;
                    try { _ = comparable.CompareTo("not-decimal"); }
                    catch (System.ArgumentException) { result |= 16384; }
                    System.IConvertible convertible = 12.5m;
                    var invalidConversions = 0;
                    try { _ = convertible.ToChar(null); }
                    catch (System.InvalidCastException) { invalidConversions++; }
                    if (convertible.GetTypeCode() == System.TypeCode.Decimal &&
                        convertible.ToBoolean(null) &&
                        convertible.ToInt32(null) == 12 &&
                        convertible.ToDouble(null) == 12.5 &&
                        convertible.ToDecimal(null) == 12.5m &&
                        (decimal)convertible.ToType(typeof(decimal), null) == 12.5m &&
                        invalidConversions == 1)
                        result |= 32768;
                    return result + input - input;
                }
            }
            """);

        var compilation = NetWasmCompiler.Compile(new CompilerOptions(
            assembly,
            [assets.CoreLib],
            "DecimalBehaviorFixture.EntryPoint",
            "Run",
            []));

        Assert.Equal(65535, ExecuteWithNode(
            compilation.ApplicationModule,
            assets.Directory,
            41));
    }

    [Fact]
    public void DecimalMatchesDesktopConstantsParameterNamesAndExceptionMessages()
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSource(
            "DecimalSurfaceFixture",
            """
            using System;

            namespace DecimalSurfaceFixture;

            public static class EntryPoint
            {
                // .NET declares Zero, One, MinusOne, MaxValue and MinValue as constants.
                private const decimal Limit = decimal.MaxValue;
                private const decimal Lowest = decimal.MinValue + decimal.One;

                private static decimal Scale(decimal value, decimal factor = decimal.MinusOne) => value * factor;

                public static int Run(int input)
                {
                    var result = 0;
                    var maximum = Limit - input + input;
                    if (Scale(2m) == -2m && Lowest == -79228162514264337593543950334m && decimal.Zero == 0m)
                        result |= 1;
                    if (decimal.Parse(s: "12.5") == 12.5m && decimal.TryParse(s: "1.5", out var parsed) && parsed == 1.5m)
                        result |= 2;
                    try { _ = maximum + 1m; }
                    catch (OverflowException e) when (e.Message == "Value was either too large or too small for a Decimal.") { result |= 4; }
                    try { _ = decimal.ToByte(maximum); }
                    catch (OverflowException e) when (e.Message == "Value was either too large or too small for an unsigned byte.") { result |= 8; }
                    try { _ = decimal.ToInt32(maximum); }
                    catch (OverflowException e) when (e.Message == "Value was either too large or too small for an Int32.") { result |= 16; }
                    try { _ = decimal.Round(maximum, 29 + input - input); }
                    catch (ArgumentOutOfRangeException e) when (e.ParamName == "decimals" &&
                        e.Message.StartsWith("Decimal can only round to between 0 and 28 digits of precision.", StringComparison.Ordinal)) { result |= 32; }
                    try { _ = decimal.Round(maximum, 2, (MidpointRounding)(7 + input - input)); }
                    catch (ArgumentException e) when (e.ParamName == "mode" &&
                        e.Message.StartsWith("The value '7' is not valid for this usage of the type MidpointRounding.", StringComparison.Ordinal)) { result |= 64; }
                    try { _ = new decimal([1, 2, 3, 0x7F000000 + input - input]); }
                    catch (ArgumentException e) when (e.Message == "Decimal constructor requires an array or span of four valid decimal bytes.") { result |= 128; }
                    try { _ = ((IComparable)maximum).CompareTo("text"); }
                    catch (ArgumentException e) when (e.Message == "Object must be of type Decimal.") { result |= 256; }
                    try { _ = ((IConvertible)maximum).ToChar(null); }
                    catch (InvalidCastException e) when (e.Message == "Invalid cast from 'Decimal' to 'Char'.") { result |= 512; }
                    return result;
                }
            }
            """);

        var compilation = NetWasmCompiler.Compile(new CompilerOptions(
            assembly,
            [assets.CoreLib],
            "DecimalSurfaceFixture.EntryPoint",
            "Run",
            []));

        // Desktop .NET 11 returns 1023: every check passes.
        Assert.Equal(1023, ExecuteWithNode(
            compilation.ApplicationModule,
            assets.Directory,
            41));
    }

    [Theory]
    [InlineData(FinalWasmOptimization.Oz)]
    [InlineData(FinalWasmOptimization.O3)]
    public void CompilerMatchesDesktopDecimalSeedCorpus(FinalWasmOptimization optimization)
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSource(
            "DecimalCorpusFixture",
            """
            namespace DecimalCorpusFixture;

            public static class EntryPoint
            {
                public static int Run(int input)
                {
                    var hash = input;
                    for (var offset = 1; offset <= 128; offset++)
                    {
                        var seed = input + offset;
                        var negative = (seed & 1) != 0;
                        var left = new decimal(seed * 7919 + 17, 0, 0, negative, (byte)(seed % 5));
                        var right = new decimal(seed * 97 + 3, 0, 0, false, (byte)(seed % 3));
                        hash = (hash * 31) ^ (left + right).GetHashCode();
                        hash = (hash * 31) ^ (left - right).GetHashCode();
                        hash = (hash * 31) ^ (left * right).GetHashCode();
                        hash = (hash * 31) ^ (left / right).GetHashCode();
                        hash = (hash * 31) ^ (left % right).GetHashCode();
                        hash = (hash * 31) ^ decimal.Round(left / right, 4).GetHashCode();
                    }
                    return hash;
                }
            }
            """);

        var compilation = NetWasmCompiler.Compile(new CompilerOptions(
            assembly,
            [assets.CoreLib],
            "DecimalCorpusFixture.EntryPoint",
            "Run",
            [],
            Optimization: optimization));

        foreach (var seed in new[] { 1, 29, 101 })
        {
            Assert.Equal(DesktopCorpus(seed), ExecuteWithNode(
                compilation.ApplicationModule,
                assets.Directory,
                seed));
        }
    }

    [Fact]
    public void CompilerExecutesDecimalInManagedLanguageShapes()
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSource(
            "DecimalManagedShapesFixture",
            """
            namespace DecimalManagedShapesFixture;

            public delegate decimal Transform(decimal value);

            public sealed class Holder<T>
            {
                public T Value = default!;
            }

            public static class EntryPoint
            {
                private static decimal Increment(decimal value) => value + 1m;

                public static int Run(int input)
                {
                    var holder = new Holder<decimal> { Value = 12.5m };
                    var values = new decimal[] { holder.Value, 2.5m };
                    Transform transform = Increment;
                    object boxed = transform(values[0] + values[1]);
                    var value = boxed is decimal parsed ? parsed : 0m;
                    var result = value == 16m ? 1 : 0;
                    try
                    {
                        _ = value / (input - input);
                    }
                    catch (System.DivideByZeroException)
                    {
                        result |= 2;
                    }
                    for (var index = 0; index < 64; index++)
                    {
                        _ = new decimal[] { value, index };
                    }
                    return result;
                }
            }
            """);

        var compilation = NetWasmCompiler.Compile(new CompilerOptions(
            assembly,
            [assets.CoreLib],
            "DecimalManagedShapesFixture.EntryPoint",
            "Run",
            []));

        Assert.Equal(3, ExecuteWithNode(
            compilation.ApplicationModule,
            assets.Directory,
            41));
    }

    private static int DesktopCorpus(int input)
    {
        var hash = input;
        for (var offset = 1; offset <= 128; offset++)
        {
            var seed = input + offset;
            var negative = (seed & 1) != 0;
            var left = new decimal(seed * 7919 + 17, 0, 0, negative, (byte)(seed % 5));
            var right = new decimal(seed * 97 + 3, 0, 0, false, (byte)(seed % 3));
            hash = (hash * 31) ^ (left + right).GetHashCode();
            hash = (hash * 31) ^ (left - right).GetHashCode();
            hash = (hash * 31) ^ (left * right).GetHashCode();
            hash = (hash * 31) ^ (left / right).GetHashCode();
            hash = (hash * 31) ^ (left % right).GetHashCode();
            hash = (hash * 31) ^ decimal.Round(left / right, 4).GetHashCode();
        }
        return hash;
    }

    private static string DecimalExpression(decimal value)
    {
        var bits = decimal.GetBits(value);
        var flags = (uint)bits[3];
        var negative = (flags & 0x80000000U) != 0 ? "true" : "false";
        var scale = (byte)(flags >> 16);
        return $"new decimal({bits[0]}, {bits[1]}, {bits[2]}, {negative}, {scale})";
    }

    private static decimal DecimalIdentity(decimal value) => value;

    [Fact]
    public void CompilerTrimsUnusedDecimalImplementationAndSupportsBothTargets()
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSource(
            "DecimalTrimmingFixture",
            """
            namespace DecimalTrimmingFixture;
            public static class EntryPoint
            {
                public static int Run(int input) => input + 1;
            }
            """);

        foreach (var target in new[] { WasmTarget.Wasm32, WasmTarget.Wasm64 })
        {
            var compilation = NetWasmCompiler.Compile(new CompilerOptions(
                assembly,
                [assets.CoreLib],
                "DecimalTrimmingFixture.EntryPoint",
                "Run",
                [],
                target));

            Assert.DoesNotContain(compilation.Program.MethodInstances.Values,
                method => method.CanonicalName.Contains(
                    "System.Decimal", StringComparison.Ordinal));
            Assert.True(compilation.ApplicationModule.AsSpan().IndexOf("System.Decimal"u8) < 0);
        }
    }
}
