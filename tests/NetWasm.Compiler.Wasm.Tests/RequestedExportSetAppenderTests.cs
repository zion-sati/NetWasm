using System.Collections.Immutable;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission;
using NetWasm.Compiler.Wasm.Emission.GeneratedFunctions;
using NetWasm.Compiler.Wasm.Emission.Planning;

namespace NetWasm.Compiler.Wasm.Tests;

public sealed class RequestedExportSetAppenderTests
{
    [Fact]
    public void AppendsExportsInOrdinalNameOrder()
    {
        var leaf = new RecordingRequestedExportAppender();
        var appender = CreateAppender(leaf);
        var low = EmitterTestSupport.Key(0x06000010);
        var high = EmitterTestSupport.Key(0x06000020);
        var exports = ImmutableDictionary<string, EntityKey>.Empty
            .Add("zeta", high)
            .Add("alpha", low);

        appender.Append([], 5, new Dictionary<string, int>(),
            new Dictionary<string, int>(), exports,
            ImmutableDictionary<EntityKey, JavaScriptAsyncMethodBinding>.Empty,
            TestRuntimeInitialization.Create(128), true, WasmModuleProfile.ComponentCoreModule,
            EmitterTestSupport.CreateFunctionIndexResolver(), []);

        Assert.Equal(["alpha", "zeta"], leaf.Names);
        Assert.Equal([low, high], leaf.Methods);
        Assert.Equal([5, 5], leaf.ImportCounts);
        Assert.All(leaf.HasFinalizers, Assert.True);
    }

    [Fact]
    public void SupportsEmptySetAndRejectsInvalidCollaborativeInputs()
    {
        var appender = CreateAppender(new RecordingRequestedExportAppender());
        var functions = new List<WasmFunctionDefinition>();
        var indices = new Dictionary<string, int>();
        var helpers = new Dictionary<string, int>();
        var exports = ImmutableDictionary<string, EntityKey>.Empty;
        var bindings = ImmutableDictionary<EntityKey, JavaScriptAsyncMethodBinding>.Empty;
        var resolver = EmitterTestSupport.CreateFunctionIndexResolver();
        var boundaries = new List<ManagedBoundaryPlanEntry>();

        appender.Append(functions, 0, indices, helpers, exports, bindings, TestRuntimeInitialization.Create(0), false,
            WasmModuleProfile.CoreApplication, resolver, boundaries);
        Assert.Throws<ArgumentNullException>(() => appender.Append(null!, 0, indices,
            helpers, exports, bindings, TestRuntimeInitialization.Create(0), false, WasmModuleProfile.CoreApplication,
            resolver, boundaries));
        Assert.Throws<ArgumentOutOfRangeException>(() => appender.Append(functions, -1,
            indices, helpers, exports, bindings, TestRuntimeInitialization.Create(0), false,
            WasmModuleProfile.CoreApplication, resolver, boundaries));
        Assert.Throws<ArgumentNullException>(() => appender.Append(functions, 0, null!,
            helpers, exports, bindings, TestRuntimeInitialization.Create(0), false, WasmModuleProfile.CoreApplication,
            resolver, boundaries));
        Assert.Throws<ArgumentNullException>(() => appender.Append(functions, 0, indices,
            null!, exports, bindings, TestRuntimeInitialization.Create(0), false, WasmModuleProfile.CoreApplication,
            resolver, boundaries));
        Assert.Throws<ArgumentNullException>(() => appender.Append(functions, 0, indices,
            helpers, null!, bindings, TestRuntimeInitialization.Create(0), false, WasmModuleProfile.CoreApplication,
            resolver, boundaries));
        Assert.Throws<ArgumentNullException>(() => appender.Append(functions, 0, indices,
            helpers, exports, null!, TestRuntimeInitialization.Create(0), false, WasmModuleProfile.CoreApplication,
            resolver, boundaries));
        Assert.Throws<ArgumentOutOfRangeException>(() => appender.Append(functions, 0,
            indices, helpers, exports, bindings, TestRuntimeInitialization.Create(-1), false,
            WasmModuleProfile.CoreApplication, resolver, boundaries));
        Assert.Throws<ArgumentNullException>(() => appender.Append(functions, 0, indices,
            helpers, exports, bindings, TestRuntimeInitialization.Create(0), false, WasmModuleProfile.CoreApplication,
            null!, boundaries));
        Assert.Throws<ArgumentNullException>(() => appender.Append(functions, 0, indices,
            helpers, exports, bindings, TestRuntimeInitialization.Create(0), false, WasmModuleProfile.CoreApplication,
            resolver, null!));
    }

    private static IRequestedExportSetAppender CreateAppender(
        IRequestedExportFunctionAppender leaf) => new[]
    {
        new RequestedExportSetAppender(leaf),
    }.Cast<IRequestedExportSetAppender>().Single();

    private sealed class RecordingRequestedExportAppender :
        IRequestedExportFunctionAppender
    {
        public List<string> Names { get; } = [];
        public List<EntityKey> Methods { get; } = [];
        public List<int> ImportCounts { get; } = [];
        public List<bool> HasFinalizers { get; } = [];

        public void Append(IList<WasmFunctionDefinition> functions, int importCount,
            IDictionary<string, int> requestedExportIndices,
            IDictionary<string, int> asyncHelperIndices, string exportName,
            EntityKey methodKey,
            IReadOnlyDictionary<EntityKey, JavaScriptAsyncMethodBinding> asyncBindings,
            RuntimeInitializationPlan initialization, bool hasFinalizers,
            WasmModuleProfile profile,
            IFunctionIndexResolver functionIndices,
            ICollection<ManagedBoundaryPlanEntry> boundaryEntries)
        {
            Names.Add(exportName);
            Methods.Add(methodKey);
            ImportCounts.Add(importCount);
            HasFinalizers.Add(hasFinalizers);
        }
    }
}
