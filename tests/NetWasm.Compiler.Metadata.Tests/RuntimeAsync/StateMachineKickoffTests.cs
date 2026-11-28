// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Metadata.Inputs;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Metadata.Tests.RuntimeAsync;

// Async methods Roslyn compiled to state machines (without runtime-async=on), as a published package's are: a class's
// method over one of the task builders compiles as runtime async, its body its MoveNext rewritten; any other keeps its
// machine. The programs run in the compiler's RuntimeAsyncExecutionTests.
public sealed class StateMachineKickoffTests : IDisposable
{
    private const string Source = """
        using System;
        using System.Collections.Generic;
        using System.Threading.Tasks;

        public sealed class Shapes
        {
            private int _total;

            public async Task<int> Plain(int x)
            {
                await Task.Yield();
                _total += x;
                return x + 1;
            }

            public async ValueTask<string> Configured(Task<string> text, int count)
            {
                var value = await text.ConfigureAwait(false);
                for (var index = 0; index < count; index++)
                {
                    await new ValueTask(Task.Delay(0));
                    value += index;
                }
                return value;
            }

            public async Task Guarded(Func<Task> work)
            {
                try
                {
                    await work();
                }
                catch (InvalidOperationException)
                {
                    await Task.Yield();
                    _total--;
                }
                finally
                {
                    await Task.Yield();
                    _total++;
                }
            }

            public async void Fire() => await Task.Yield();

            public async IAsyncEnumerable<int> Iterate()
            {
                await Task.Yield();
                yield return _total;
            }

            public Task<int> NotAsync() => Task.FromResult(_total);

        #pragma warning disable CS1998
            public async Task<int> NoAwait(int x) => x + _total;
        #pragma warning restore CS1998

            public async Task<int> Ranged(Func<Task<int>> work)
            {
                var total = await work();
                try
                {
                    total += await work();
                    total += await work();
                }
                finally
                {
                    _total++;
                }
                return total;
            }
        }

        public struct Copied
        {
            public int Value;

            public async Task<int> Copy()
            {
                await Task.Yield();
                return Value;
            }
        }
        """;

    private static readonly string[] TypeNames = ["Shapes", "Copied"];

    private readonly TestAssets _assets = TestAssets.Create();

    public void Dispose() => _assets.Dispose();

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void OnlyAClasssTaskBuilderMachinesCompileAsRuntimeAsync(bool optimize)
    {
        var assembly = _assets.CompileSourceWithCompiler("StateMachineShapes", Source, TestAssets.RepositorySdkVersion,
            "latest", optimize);
        using var lease = MetadataCompilationTestFactory.Load(assembly, [_assets.CoreLib]);
        var methods = Methods(lease.Snapshot);

        foreach (var (type, name) in new[] { ("Shapes", "Plain"), ("Shapes", "Configured"), ("Shapes", "Guarded"), ("Shapes", "NoAwait"), ("Shapes", "Ranged") })
        {
            var method = methods[(type, name)];
            Assert.True(method.IsRuntimeAsync, name);
            Assert.Equal("MoveNext", MetadataCapabilityTestData.Methods(lease.Snapshot).GetMethod(method.StateMachineMoveNext!.Value).Name);
        }
        foreach (var (type, name) in new[] { ("Shapes", "Fire"), ("Shapes", "Iterate"), ("Shapes", "NotAsync"), ("Copied", "Copy") })
        {
            var method = methods[(type, name)];
            Assert.False(method.IsRuntimeAsync, name);
            Assert.Null(method.StateMachineMoveNext);
        }
    }

    // An async iterator keeps its machine, whose MoveNextAsync and DisposeAsync compile as runtime async: MoveNextAsync
    // from MoveNext, DisposeAsync awaiting MoveNextAsync.
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void AnAsyncIteratorsMoveNextAsyncAndDisposeAsyncCompileAsRuntimeAsync(bool optimize)
    {
        var assembly = _assets.CompileSourceWithCompiler("StateMachineShapes", Source, TestAssets.RepositorySdkVersion,
            "latest", optimize);
        using var lease = MetadataCompilationTestFactory.Load(assembly, [_assets.CoreLib]);
        var methods = MetadataCapabilityTestData.Methods(lease.Snapshot);
        using var image = new PEReader(File.OpenRead(assembly));
        var reader = image.GetMetadataReader();
        var name = new AssemblyIdentity(reader.GetString(reader.GetAssemblyDefinition().Name));
        MethodDefinitionModel Named(string suffix) => methods.GetMethod(new EntityKey(name, MetadataTokens.GetToken(
            reader.MethodDefinitions.Single(handle =>
                reader.GetString(reader.GetTypeDefinition(reader.GetMethodDefinition(handle).GetDeclaringType()).Name)
                    .StartsWith("<Iterate>", StringComparison.Ordinal) &&
                reader.GetString(reader.GetMethodDefinition(handle).Name).EndsWith(suffix, StringComparison.Ordinal)))));

        var moveNext = Named("MoveNext");
        var moveNextAsync = Named(".MoveNextAsync");
        var disposeAsync = Named(".DisposeAsync");

        Assert.True(moveNextAsync.IsRuntimeAsync);
        Assert.Equal(moveNext.Key, moveNextAsync.AsyncIteratorMoveNext);
        Assert.True(disposeAsync.IsRuntimeAsync);
        Assert.Equal(moveNextAsync.Key, disposeAsync.AsyncIteratorDisposeOf);
        Assert.False(moveNext.IsRuntimeAsync);
    }

    // Rewritten, not kept: the lowered body neither starts the machine nor touches its fields.
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void AKickoffsBodyIsItsMachineRewritten(bool optimize)
    {
        var assembly = _assets.CompileSourceWithCompiler("StateMachineShapes", Source, TestAssets.RepositorySdkVersion,
            "latest", optimize);
        using var lease = MetadataCompilationTestFactory.Load(assembly, [_assets.CoreLib]);
        var methods = Methods(lease.Snapshot);
        var bodies = MetadataCapabilityTestData.MethodBodies(lease.Snapshot);
        var shapes = MetadataCapabilityTestData.TypeFinder(lease.Snapshot).FindType("Shapes");

        foreach (var name in new[] { "Plain", "Configured", "Guarded", "NoAwait", "Ranged" })
        {
            var method = methods[("Shapes", name)];
            var machine = MetadataCapabilityTestData.Methods(lease.Snapshot).GetMethod(method.StateMachineMoveNext!.Value).DeclaringType;
            var body = bodies.ReadMethodBody(new MethodInstanceModel(method, CliTypeIdentity.FromDefinition(shapes), [],
                method.Signature));

            Assert.DoesNotContain(body.Instructions, instruction => instruction.Operand switch
            {
                CilOperand.MethodInstance call => call.Value.Definition.Name == "Start" || call.Value.Definition.DeclaringType == machine,
                CilOperand.FieldInstance field => field.Value.Definition.DeclaringType == machine,
                _ => false,
            });
        }
    }

    // Debug IL gives each await its own awaiter local and pads each access to a local past the 255th with nops
    // (`stloc 255; nop; nop; ldloca 255; nop; nop`): the machine is rewritten all the same.
    [Fact]
    public void ADebugMachineWithMoreThan255LocalsIsRewritten()
    {
        var awaits = string.Concat(Enumerable.Range(0, 140).Select(index =>
            $"await Task.Yield(); total += await Task.FromResult({index}); "));
        var source = $$"""
            using System.Threading.Tasks;

            public sealed class Shapes
            {
                public async Task<int> Many(int total)
                {
                    {{awaits}}
                    return total;
                }
            }
            """;
        var assembly = _assets.CompileSourceWithCompiler("StateMachineLocals", source, TestAssets.RepositorySdkVersion,
            "latest", optimize: false);
        using var lease = MetadataCompilationTestFactory.Load(assembly, [_assets.CoreLib]);
        var methods = MetadataCapabilityTestData.Methods(lease.Snapshot);
        var shapes = MetadataCapabilityTestData.TypeFinder(lease.Snapshot).FindType("Shapes");
        var method = shapes.Methods.Select(methods.GetMethod).Single(candidate => candidate.Name == "Many");
        var moveNext = methods.GetMethod(method.StateMachineMoveNext!.Value);
        var body = MetadataCapabilityTestData.MethodBodies(lease.Snapshot).ReadMethodBody(new MethodInstanceModel(method,
            CliTypeIdentity.FromDefinition(shapes), [], method.Signature));

        Assert.Contains(body.Instructions, instruction =>
            instruction is { Operation: CilOperation.LoadLocal or CilOperation.StoreLocal, Operand: CilOperand.Index { Value: > 255 } });
        var fields = MetadataCapabilityTestData.Fields(lease.Snapshot);
        Assert.DoesNotContain(body.Instructions, instruction => instruction switch
        {
            { Operand: CilOperand.MethodInstance call } => call.Value.Definition.Name == "Start" ||
                call.Value.Definition.DeclaringType == moveNext.DeclaringType,
            {
                Operation: CilOperation.LoadField or CilOperation.LoadFieldAddress or CilOperation.StoreField,
                Operand: CilOperand.Entity entity
            } => fields.GetField(entity.Key).DeclaringType == moveNext.DeclaringType,
            _ => false,
        });
    }

    // An edit to an async method's code changes only its MoveNext's IL; the method's own fingerprint covers it, so the
    // dev loop's early cutoff and unit cache see the method as edited.
    [Fact]
    public void AnEditToTheMachineChangesTheKickoffsFingerprint()
    {
        const string Edited = """
            using System.Threading.Tasks;

            public static class Edited
            {
                public static async Task<int> Next(int x)
                {
                    await Task.Yield();
                    return x + INCREMENT;
                }

                public static int Plain(int x) => x * 3;
            }
            """;
        var before = Fingerprints(_assets.CompileSourceWithCompiler("EditedBefore", Edited.Replace("INCREMENT", "1"),
            TestAssets.RepositorySdkVersion, "latest", true));
        var after = Fingerprints(_assets.CompileSourceWithCompiler("EditedAfter", Edited.Replace("INCREMENT", "2"),
            TestAssets.RepositorySdkVersion, "latest", true));

        Assert.Equal(before.KickoffIl, after.KickoffIl);
        Assert.NotEqual(before.Kickoff, after.Kickoff);
        Assert.Equal(before.Plain, after.Plain);
    }

    private (InputFingerprint Kickoff, InputFingerprint Plain, byte[] KickoffIl) Fingerprints(string assembly)
    {
        using var lease = MetadataCompilationTestFactory.Load(assembly, [_assets.CoreLib]);
        var name = Path.GetFileNameWithoutExtension(assembly);
        var bodies = new CompilationInputReader().Read(lease.Snapshot, CompilationInputs.Empty).Find(name)!.Detail!.Bodies;
        using var image = new PEReader(File.OpenRead(assembly));
        var reader = image.GetMetadataReader();
        int Token(string method) => MetadataTokens.GetToken(reader.MethodDefinitions.Single(handle =>
            reader.GetString(reader.GetMethodDefinition(handle).Name) == method));
        var kickoffIl = image.GetMethodBody(reader.GetMethodDefinition(
            MetadataTokens.MethodDefinitionHandle(Token("Next"))).RelativeVirtualAddress).GetILBytes()!;
        return (bodies[Token("Next")], bodies[Token("Plain")], kickoffIl);
    }

    private static Dictionary<(string Type, string Name), MethodDefinitionModel> Methods(MetadataCompilationSnapshot snapshot)
    {
        var types = MetadataCapabilityTestData.TypeFinder(snapshot);
        var methods = MetadataCapabilityTestData.Methods(snapshot);
        return TypeNames
            .SelectMany(type => types.FindType(type).Methods.Select(methods.GetMethod).Select(method => (type, method)))
            .GroupBy(item => (item.type, item.method.Name))
            .ToDictionary(group => group.Key, group => group.First().method);
    }
}
