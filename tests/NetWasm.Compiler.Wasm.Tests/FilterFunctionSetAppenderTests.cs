using NetWasm.Compiler.Wasm.Emission;
using NetWasm.Compiler.Wasm.Emission.GeneratedFunctions;
using NetWasm.Compiler.Wasm.Emission.Methods;
using NetWasm.Compiler.Wasm.Emission.Planning;
using NetWasm.Compiler.Core.IntermediateRepresentation.Identity;

namespace NetWasm.Compiler.Wasm.Tests;

public sealed class FilterFunctionSetAppenderTests
{
    [Fact]
    public void AppendsFuncletsInIdOrderWithOwningMethodEnvironment()
    {
        var request = EmitterTestSupport.CreateEmissionRequest();
        var method = request.Methods[EmitterTestSupport.EntryKey];
        var key = ManagedMethodBodyKey.Resolve(method);
        var environment = FilterEnvironmentLayout.Empty;
        var leaf = new RecordingFilterFunctionAppender();
        var appender = CreateAppender(leaf);
        var target = EmitterTestSupport.CreateInstructionModuleTarget();

        appender.Append([], 6, new Dictionary<int, int>(),
            [new(9, new ManagedMethodIdentity("test-filter"), method, null!), new(2, new ManagedMethodIdentity("test-filter"), method, null!)],
            new Dictionary<string, FilterEnvironmentLayout> { [key] = environment },
            target, EmitterTestSupport.CreateFunctionIndexResolver(), []);

        Assert.Equal([2, 9], leaf.Ids);
        Assert.All(leaf.Environments, actual => Assert.Same(environment, actual));
        Assert.Equal([6, 6], leaf.ImportCounts);
    }

    [Fact]
    public void SupportsEmptySetAndRejectsInvalidCollaborativeInputs()
    {
        var appender = CreateAppender(new RecordingFilterFunctionAppender());
        var functions = new List<WasmFunctionDefinition>();
        var indices = new Dictionary<int, int>();
        var filters = new List<FilterFunclet>();
        var environments = new Dictionary<string, FilterEnvironmentLayout>();
        var target = EmitterTestSupport.CreateInstructionModuleTarget();
        var resolver = EmitterTestSupport.CreateFunctionIndexResolver();
        var emissions = new List<ManagedMethodEmissionRecord>();

        appender.Append(functions, 0, indices, filters, environments, target,
            resolver, emissions);
        Assert.Throws<ArgumentNullException>(() => appender.Append(null!, 0, indices,
            filters, environments, target, resolver, emissions));
        Assert.Throws<ArgumentOutOfRangeException>(() => appender.Append(functions, -1,
            indices, filters, environments, target, resolver, emissions));
        Assert.Throws<ArgumentNullException>(() => appender.Append(functions, 0, null!,
            filters, environments, target, resolver, emissions));
        Assert.Throws<ArgumentNullException>(() => appender.Append(functions, 0, indices,
            null!, environments, target, resolver, emissions));
        Assert.Throws<ArgumentNullException>(() => appender.Append(functions, 0, indices,
            filters, null!, target, resolver, emissions));
        Assert.Throws<ArgumentNullException>(() => appender.Append(functions, 0, indices,
            filters, environments, null!, resolver, emissions));
        Assert.Throws<ArgumentNullException>(() => appender.Append(functions, 0, indices,
            filters, environments, target, null!, emissions));
        Assert.Throws<ArgumentNullException>(() => appender.Append(functions, 0, indices,
            filters, environments, target, resolver, null!));
    }

    private static IFilterFunctionSetAppender CreateAppender(
        IFilterFunctionAppender leaf) => new[]
    {
        new FilterFunctionSetAppender(leaf),
    }.Cast<IFilterFunctionSetAppender>().Single();

    private sealed class RecordingFilterFunctionAppender : IFilterFunctionAppender
    {
        public List<int> Ids { get; } = [];
        public List<int> ImportCounts { get; } = [];
        public List<FilterEnvironmentLayout> Environments { get; } = [];

        public void Append(IList<WasmFunctionDefinition> functions, int importCount,
            IDictionary<int, int> indices, FilterFunclet filter,
            FilterEnvironmentLayout environment, InstructionModuleTarget target,
            IFunctionIndexResolver functionIndices,
            IList<ManagedMethodEmissionRecord> managedMethodEmissions)
        {
            Ids.Add(filter.Id);
            ImportCounts.Add(importCount);
            Environments.Add(environment);
        }
    }
}
