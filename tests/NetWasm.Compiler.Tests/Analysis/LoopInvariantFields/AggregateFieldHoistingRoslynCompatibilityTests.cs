using NetWasm.Compiler.Metadata;
using NetWasm.Compiler.Analysis;
using NetWasm.Compiler.Analysis.LoopInvariantFields;
using NetWasm.Compiler.ComponentModel.Optimization;
using NetWasm.Compiler.ControlFlow;
using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests.Analysis.LoopInvariantFields;

public sealed class AggregateFieldHoistingRoslynCompatibilityTests
{
    [Fact]
    public void OptimizedRecordStructLoopProducesStableFieldHoists()
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileOptimizedSource(
            "LoopInvariantFieldHoistingRoslynFixture",
            Source);
        using var metadata = MetadataCompilationTestFactory.Load(
            assembly,
            [assets.CoreLib]);
        var snapshot = metadata.Snapshot;
        var methods = MetadataCompilationTestActors.Methods(snapshot);
        var methodInstances = MetadataCompilationTestActors.MethodInstances(snapshot);
        var symbols = MetadataCompilationTestActors.Symbols(snapshot);
        var bodies = MetadataCompilationTestActors.MethodBodies(snapshot);
        var method = MetadataCompilationTestActors.MethodFinder(snapshot).FindMethod(
            snapshot.EntryAssemblyIdentity,
            "NetWasm.Compiler.Tests.Fixtures.EntryPoint",
            "Run");
        var body = new ValueTypeFieldReadRewriter().Rewrite(
            bodies.ReadMethodBody(method));
        var calledMethods = new CalledMethodResolver(methods, methodInstances, symbols);
        var projections = Assert.IsAssignableFrom<IFieldProjectionResolver>(
            new FieldProjectionResolver(
                new CalleeMethodBodyReader(bodies),
                calledMethods,
                MetadataCompilationTestActors.Fields(snapshot),
                MetadataCompilationTestActors.TypeDefinitions(snapshot)));
        var planner = Assert.IsAssignableFrom<IAggregateFieldHoistingPlanner>(
            new AggregateFieldHoistingPlanner(
                new ControlFlowGraphBuilderFactory(),
                new ControlFlowGraphAnalyzerFactory(),
                projections,
                LoopInvariantFieldHoistingPolicy.Conservative));

        var plan = planner.Plan(body);

        Assert.Equal(2, plan.Hoists.Length);
        CliValueKind[] expectedFields =
            [CliValueKind.ManagedReference, CliValueKind.I4];
        Assert.Equal(
            expectedFields,
            plan.Hoists.Select(hoist => hoist.FieldType.StackKind)
                .ToArray());
    }

    private const string Source = """
        namespace NetWasm.Compiler.Tests.Fixtures;

        public readonly record struct Accepted(int[] Values, int Count);

        public static class EntryPoint
        {
            public static long Run(int input)
            {
                var accepted = Create(input);
                var total = 0L;
                for (var index = 0; index < accepted.Count; index++)
                {
                    total += accepted.Values[index] / 100L;
                }
                return total;
            }

            private static Accepted Create(int count) =>
                new(new int[count], count);
        }
        """;
}
