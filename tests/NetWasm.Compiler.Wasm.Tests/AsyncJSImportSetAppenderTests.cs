using System.Collections.Immutable;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission;
using NetWasm.Compiler.Wasm.Emission.GeneratedFunctions;
using NetWasm.Compiler.Wasm.Emission.Planning;

namespace NetWasm.Compiler.Wasm.Tests;

public sealed class AsyncJSImportSetAppenderTests
{
    [Fact]
    public void SelectsReachableImportsAndAppendsInMethodKeyOrder()
    {
        var program = new FakeProgram();
        var method = program.GetMethod(EmitterTestSupport.EntryKey);
        var lowMethod = Import(method with { Key = EmitterTestSupport.Key(0x06000010) });
        var highMethod = Import(method with { Key = EmitterTestSupport.Key(0x06000020) });
        var unreachableMethod = Import(method with
        {
            Key = EmitterTestSupport.Key(0x06000030),
        });
        var low = CreateBinding(lowMethod);
        var high = CreateBinding(highMethod);
        var unreachable = CreateBinding(unreachableMethod);
        var request = EmitterTestSupport.CreateEmissionRequest(program) with
        {
            JSImportMethods = [highMethod, lowMethod],
            JavaScriptAsyncBindings =
                ImmutableDictionary<EntityKey, JavaScriptAsyncMethodBinding>.Empty
                    .Add(high.Method, high)
                    .Add(unreachable.Method, unreachable)
                    .Add(low.Method, low),
        };
        var leaf = new RecordingAsyncJSImportHelperAppender();
        var appender = CreateAppender(leaf);
        var target = EmitterTestSupport.CreateFunctionIndexResolver(program);

        appender.Append([], 4, ImmutableDictionary.CreateBuilder<string, int>(),
            request, target, []);

        Assert.Equal([low.Method, high.Method], leaf.Methods);
        Assert.Equal([4, 4], leaf.ImportCounts);
    }

    [Fact]
    public void SupportsEmptySetAndRejectsInvalidCollaborativeInputs()
    {
        var appender = CreateAppender(new RecordingAsyncJSImportHelperAppender());
        var request = EmitterTestSupport.CreateEmissionRequest();
        var functions = new List<WasmFunctionDefinition>();
        var indices = ImmutableDictionary.CreateBuilder<string, int>();
        var resolver = EmitterTestSupport.CreateFunctionIndexResolver();
        var boundaries = new List<ManagedBoundaryPlanEntry>();

        appender.Append(functions, 0, indices, request, resolver, boundaries);
        Assert.Throws<ArgumentNullException>(() => appender.Append(null!, 0, indices,
            request, resolver, boundaries));
        Assert.Throws<ArgumentOutOfRangeException>(() => appender.Append(functions, -1,
            indices, request, resolver, boundaries));
        Assert.Throws<ArgumentNullException>(() => appender.Append(functions, 0, null!,
            request, resolver, boundaries));
        Assert.Throws<ArgumentNullException>(() => appender.Append(functions, 0, indices,
            null!, resolver, boundaries));
        Assert.Throws<ArgumentNullException>(() => appender.Append(functions, 0, indices,
            request, null!, boundaries));
        Assert.Throws<ArgumentNullException>(() => appender.Append(functions, 0, indices,
            request, resolver, null!));
    }

    private static IAsyncJSImportSetAppender CreateAppender(
        IAsyncJSImportHelperAppender leaf) => new[]
    {
        new AsyncJSImportSetAppender(leaf),
    }.Cast<IAsyncJSImportSetAppender>().Single();

    private static JavaScriptAsyncMethodBinding CreateBinding(
        MethodDefinitionModel method)
    {
        var instance = new MethodInstanceModel(method,
            CliTypeIdentity.Named(EmitterTestSupport.Assembly, "Tests", "TaskSource",
                isValueType: false), [], method.Signature);
        return new(method.Key, new(JavaScriptAsyncReturnKind.Task, null),
            instance.DeclaringType, instance, instance, instance);
    }

    private static MethodDefinitionModel Import(MethodDefinitionModel method) =>
        method with { JSImport = new("invoke", null) };

    private sealed class RecordingAsyncJSImportHelperAppender :
        IAsyncJSImportHelperAppender
    {
        public List<EntityKey> Methods { get; } = [];
        public List<int> ImportCounts { get; } = [];

        public void Append(IList<WasmFunctionDefinition> functions, int importCount,
            ImmutableDictionary<string, int>.Builder indices,
            JavaScriptAsyncMethodBinding binding,
            IFunctionIndexResolver functionIndices,
            IList<ManagedBoundaryPlanEntry> boundaryEntries)
        {
            Methods.Add(binding.Method);
            ImportCounts.Add(importCount);
        }
    }
}
