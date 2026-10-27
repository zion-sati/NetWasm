using System.Collections.Immutable;
using NetWasm.Compiler.ControlFlow;
using NetWasm.Compiler.ControlFlow.Structuring;
using New = NetWasm.Compiler.ControlFlow.Structured;
using StructuredMethod = NetWasm.Compiler.ControlFlow.Structured.StructuredMethod;
using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

using NetWasm.Compiler.Diagnostics;
using NetWasm.Compiler.Wasm.Emission.Planning;

namespace NetWasm.Compiler.Tests;

public sealed class CompilerDiagnosticTraceTests
{
    [Fact]
    public void CompilerWritesDeterministicWholeProgramDiagnosticTrace()
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSource(
            "DiagnosticTraceFixture",
            """
            namespace DiagnosticTraceFixture;

            public sealed class Box(int value)
            {
                public int Value { get; } = value;
            }

            public static class EntryPoint
            {
                public static int Run(int input)
                {
                    var box = new Box(input);
                    return box.Value;
                }
            }
            """);
        var firstPath = Path.Combine(assets.Directory, "first.trace");
        var secondPath = Path.Combine(assets.Directory, "second.trace");

        var first = NetWasmCompiler.Compile(new CompilerOptions(
            assembly,
            [assets.CoreLib],
            "DiagnosticTraceFixture.EntryPoint",
            "Run",
            [],
            DiagnosticTracePath: firstPath));
        var second = NetWasmCompiler.Compile(new CompilerOptions(
            assembly,
            [assets.CoreLib],
            "DiagnosticTraceFixture.EntryPoint",
            "Run",
            [],
            DiagnosticTracePath: secondPath));

        var firstText = File.ReadAllText(firstPath);
        Assert.Equal(firstText, File.ReadAllText(secondPath));
        Assert.Contains('\n', firstText);
        Assert.DoesNotContain('\r', firstText);
        Assert.Contains("NETWASM COMPILER DIAGNOSTIC TRACE v1", firstText);
        Assert.Contains("target=Wasm32", firstText);
        Assert.Contains($"static-data-end={first.StaticDataEnd}", firstText);
        Assert.Contains("[layouts]", firstText);
        Assert.Contains("DiagnosticTraceFixture.Box", firstText);
        Assert.Contains("[dispatch]", firstText);
        Assert.Contains("[method DiagnosticTraceFixture.EntryPoint::Run", firstText);
        Assert.Contains("allocating=true", firstText);
        Assert.Contains("structure:", firstText);
        Assert.Equal(first.ApplicationModule, second.ApplicationModule);
        var firstPasses = firstPath + ".passes";
        var secondPasses = secondPath + ".passes";
        string[] expectedArtifacts =
        [
            "00-options.json",
            "00-sources.json",
            "00-toolchain.json",
            "compiler.log.jsonl",
            "01-metadata.json",
            "02-entry.json",
            "03-program.json",
            "04-layouts.json",
            "05-root-maps.json",
            "06-emission.json",
            "06-emission.wasm",
            "06-emission.wat",
            "07-success.json",
            "observations.json",
        ];
        Assert.All(expectedArtifacts, name =>
            Assert.True(File.Exists(Path.Combine(firstPasses, name)), name));
        Assert.All(expectedArtifacts, name =>
            Assert.True(File.Exists(Path.Combine(secondPasses, name)), name));
        Assert.Equal(
                    File.ReadAllBytes(Path.Combine(firstPasses, "06-emission.wasm")),
                    File.ReadAllBytes(Path.Combine(secondPasses, "06-emission.wasm")));
        string[] deterministicPassArtifacts =
        [
            "01-metadata.json",
            "02-entry.json",
            "03-program.json",
            "04-layouts.json",
            "05-root-maps.json",
            "06-emission.json",
            "06-emission.wat",
            "07-success.json",
        ];
        Assert.All(deterministicPassArtifacts, name => Assert.Equal(
            File.ReadAllText(Path.Combine(firstPasses, name)),
            File.ReadAllText(Path.Combine(secondPasses, name))));
        Assert.All(deterministicPassArtifacts, name =>
            Assert.DoesNotContain('\r', File.ReadAllText(Path.Combine(firstPasses, name))));
        var provenance = File.ReadAllText(Path.Combine(
            firstPasses,
            "00-toolchain.json"));

        Assert.NotEmpty(File.ReadAllLines(Path.Combine(
                    firstPasses,
                    "compiler.log.jsonl")));
    }

    [Fact]
    public void CompilerDoesNotWriteATraceUnlessRequested()
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSource(
            "NoDiagnosticTraceFixture",
            """
            namespace NoDiagnosticTraceFixture;

            public static class EntryPoint
            {
                public static int Run(int input) => input;
            }
            """);
        var unexpectedPath = Path.Combine(assets.Directory, "unexpected.trace");

        _ = NetWasmCompiler.Compile(new CompilerOptions(
            assembly,
            [assets.CoreLib],
            "NoDiagnosticTraceFixture.EntryPoint",
            "Run",
            []));

        Assert.False(File.Exists(unexpectedPath));
        Assert.False(Directory.Exists(unexpectedPath + ".passes"));
    }

    [Fact]
    public void CompilerWritesProgressiveArtifactsWhenEntryResolutionFailsBeforeAnalysis()
    {
        using var assets = TestAssets.Create();
        var sourcePath = Path.Combine(assets.Directory, "ProgressiveFailureFixture.cs");
        var assembly = assets.CompileSource(
            "ProgressiveFailureFixture",
            """
            namespace ProgressiveFailureFixture;
            public static class EntryPoint
            {
                public static int Run(int input) => input;
            }
            """);
        var tracePath = Path.Combine(assets.Directory, "failure.trace");

        var exception = Assert.Throws<CompilerException>(() =>
            NetWasmCompiler.Compile(new CompilerOptions(
                assembly,
                [assets.CoreLib],
                "ProgressiveFailureFixture.Missing",
                "Run",
                [],
                DiagnosticTracePath: tracePath,
                SourcePaths: [sourcePath])));

        Assert.Equal(DiagnosticCode.InvalidEntryPoint, exception.Diagnostic.Code);
        var passes = tracePath + ".passes";
        Assert.True(File.Exists(Path.Combine(passes, "00-options.json")));
        Assert.True(File.Exists(Path.Combine(passes, "01-metadata.json")));
        Assert.True(File.Exists(Path.Combine(passes, "failure.json")));
        Assert.True(File.Exists(Path.Combine(passes, "compiler.log.jsonl")));
        Assert.True(File.Exists(Path.Combine(
            passes,
            "sources",
            "0000-ProgressiveFailureFixture.cs")));
        Assert.False(File.Exists(Path.Combine(passes, "03-program.json")));
        var failure = File.ReadAllText(Path.Combine(passes, "failure.json"));
        Assert.Contains("metadata", failure);
        Assert.Contains("-out:reproduction.wasm", failure);
    }

    [Fact]
    public void CompilerTraceUsesConstructedStorageForGenericStaticFields()
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSource(
            "GenericStaticDiagnosticTraceFixture",
            """
            namespace GenericStaticDiagnosticTraceFixture;

            public static class Holder<T>
            {
                public static T Value = default!;
            }

            public static class EntryPoint
            {
                public static int Run(int input)
                {
                    Holder<int>.Value = input;
                    return Holder<int>.Value;
                }
            }
            """);
        var tracePath = Path.Combine(assets.Directory, "generic-static.trace");

        _ = NetWasmCompiler.Compile(new CompilerOptions(
            assembly,
            [assets.CoreLib],
            "GenericStaticDiagnosticTraceFixture.EntryPoint",
            "Run",
            [],
            DiagnosticTracePath: tracePath));

        var trace = File.ReadAllText(tracePath);
        Assert.Contains("Holder", trace);
        Assert.Contains("field ", trace);
    }

    [Fact]
    public void StructureFormatterCoversEveryRegionAndExceptionPartContract()
    {
        var original = CreateStructuredMethod();
        var block = Assert.Single(original.Blocks.Values);
        var owner = new New.StructuredBlockOccurrence(block.Id, New.StructuredBlockRole.Owner);
        var replica = owner with { Role = New.StructuredBlockRole.ExecutingReplica };
        var leaf = new New.StructuredSequence([new New.StructuredCode(replica)]);
        var clause = new New.StructuredExceptionClause(
            CilExceptionRegionKind.Catch,
            block.StartOffset,
            1,
            new EntityKey(ValidationTestData.Assembly, 0x01000001),
            block.StartOffset,
            leaf,
            leaf)
        {
            HandlerBlock = block.Id,
            FilterBlock = block.Id,
        };
        var inner = new New.StructuredExceptionGroup(
            new(1),
            new(0),
            block.StartOffset,
            1,
            [new New.StructuredExceptionCode(leaf)],
            [],
            [],
            null,
            null)
        {
            ProtectedBlocks = [block.Id],
        };
        var group = new New.StructuredExceptionGroup(
            new(0),
            null,
            block.StartOffset,
            1,
            [new New.StructuredExceptionCode(leaf), new New.StructuredNestedExceptionGroup(inner.Id)],
            [clause],
            [new New.StructuredExceptionContinuation(new(0), block.StartOffset, block.Id, leaf)],
            null,
            null)
        {
            ProtectedBlocks = [block.Id],
        };
        var routedOwner = owner with
        {
            LeaveContinuation = new New.StructuredContinuationId(42),
        };
        var body = new New.StructuredSequence(
        [
            new New.StructuredIf(replica, leaf, leaf),
            new New.StructuredLoop(replica, true, leaf, leaf, leaf),
            new New.StructuredLoop(
                null, true, leaf, New.StructuredSequence.Empty, New.StructuredSequence.Empty),
            new New.StructuredPostTestLoop(leaf, replica, false, leaf, leaf),
            new New.StructuredExceptionRegion(group.Id, null),
            new New.StructuredExceptionRegion(group.Id, new(0)),
            new New.StructuredDispatcher(
                block.Id,
                [new New.StructuredDispatcherBlock(replica, null, null)],
                [new New.StructuredDispatcherExit(block.Id, leaf)]),
            new New.StructuredDispatcher(
                null,
                [new New.StructuredDispatcherBlock(routedOwner, null, null)],
                []),
            new New.StructuredLoopBreak(),
            new New.StructuredLoopContinue(),
            new New.StructuredDispatcherContinue(block.Id),
        ]);

        var formatter = new CompilerStructuredMethodFormatter();
        var text = formatter.FormatStructure(original with
        {
            Body = body,
            ExceptionGroups = ImmutableDictionary<New.StructuredExceptionGroupId,
                New.StructuredExceptionGroup>.Empty
                .Add(group.Id, group)
                .Add(inner.Id, inner),
        });

        string[] contracts =
        [
            "if ", "loop ", "loop unconditional", "post-loop ", "exception-region ", "protected code",
            "nested try=", "handler Catch", "filter ", "continuation ",
            "group already described", "dispatcher entry=", "exit block=", "break",
            "continue", "dispatcher-continue",
        ];
        Assert.All(contracts, contract => Assert.Contains(contract, text));
        Assert.DoesNotContain('\r', text);
    }

    [Fact]
    public void MethodTraceIncludesConstructedMethodsAndExceptionGroups()
    {
        var original = ValidationTestData.StructuredMethod();
        var block = Assert.Single(original.Blocks.Values);
        var leaf = new New.StructuredSequence([new New.StructuredCode(
            new(block.Id, New.StructuredBlockRole.ExecutingReplica))]);
        var group = new New.StructuredExceptionGroup(
            new(0),
            null,
            0,
            1,
            [new New.StructuredExceptionCode(leaf)],
            [new New.StructuredExceptionClause(
                CilExceptionRegionKind.Catch,
                0,
                1,
                new EntityKey(ValidationTestData.Assembly, 0x01000001),
                null,
                leaf,
                null)
            {
                HandlerBlock = block.Id,
            }],
            [new New.StructuredExceptionContinuation(new(0), 0, block.Id, leaf)],
            null,
            null)
        {
            ProtectedBlocks = [block.Id],
        };
        var method = original with
        {
            ExceptionGroups = ImmutableDictionary<New.StructuredExceptionGroupId,
                New.StructuredExceptionGroup>.Empty.Add(group.Id, group),
        };
        var methodKey = method.Header.Method.Key;
        var managed = ValidationTestData.ManagedMethodBody();
        var roots = new MethodRootMap(
            methodKey,
            ImmutableDictionary<RootSource, int>.Empty,
            ImmutableDictionary<int, SafepointRootMap>.Empty);
        var program = ValidationTestData.Program() with
        {
            RootMaps = ImmutableDictionary<EntityKey, MethodRootMap>.Empty
                .Add(methodKey, roots),
            ConstructedMethods = ImmutableDictionary<string, ManagedMethodBody>.Empty
                .Add("constructed", managed),
            ConstructedRootMaps = ImmutableDictionary<string, MethodRootMap>.Empty
                .Add("constructed", roots),
            ConstructedAllocatingMethods = ["constructed"],
        };
        var lowering = new WasmMethodLoweringResult(
            ImmutableDictionary<EntityKey, StructuredMethod>.Empty.Add(methodKey, method),
            ImmutableDictionary<string, StructuredMethod>.Empty.Add("constructed", method));

        var text = new CompilerDiagnosticMethodTraceFormatter(
            new CompilerCilOperandFormatter(),
            new CompilerStructuredMethodFormatter()).FormatMethods(
                ValidationTestData.Symbols(),
                program,
                lowering);

        Assert.Contains("allocating=false", text);
        Assert.Contains("allocating=true", text);
        Assert.Contains("eh try=IL_", text);
        Assert.Contains("continuations=", text);
        Assert.DoesNotContain('\r', text);
    }

    private static StructuredMethod CreateStructuredMethod()
    {
        var assembly = new AssemblyIdentity("TraceUnitTest");
        var type = new EntityKey(assembly, 0x02000001);
        var method = new MethodDefinitionModel(
            new EntityKey(assembly, 0x06000001),
            type,
            "Run",
            true,
            MethodSignatureModel.Create(CliValueKind.Void),
            1);
        var instance = new MethodInstanceModel(
            method,
            CliTypeIdentity.Named(assembly, "TraceUnitTest", "Fixture", false),
            [],
            method.Signature);
        var instruction = new CilInstruction(
            0,
            1,
            CilOperation.Return,
            new CilOperand.None());
        var body = new CilMethodBody(method, 0, [], [instruction])
        {
            MethodInstance = instance,
        };
        var graph = new ControlFlowGraphBuilderFactory().Create().Build(body);
        var validated = new ValidatedControlFlowGraph(
            graph,
            ImmutableDictionary<int, ImmutableArray<CliValueKind>>.Empty.Add(0, []),
            ImmutableDictionary<int, ImmutableArray<CliValueKind>>.Empty.Add(0, []));
        return new ValidatedStructuredMethodBuilderFactory()
            .Create()
            .Build(validated);
    }
}
