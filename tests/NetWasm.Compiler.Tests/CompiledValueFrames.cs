using NetWasm.Compiler.Analysis;
using NetWasm.Compiler.ControlFlow.Structured;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Layout;
using NetWasm.Compiler.Wasm.Emission;
using NetWasm.Compiler.Wasm.Emission.Methods;
using NetWasm.Compiler.Wasm.Emission.Values;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

using static CompilerTestSupport;

// A compiled method's value frame as its emission lays it out, and where the branch planner
// places it, from the compile's own metadata and layouts.
internal static class CompiledValueFrames
{
    public static (ValueFrameLayout Layout, TerminalBranchValueFramePlan? Plan) Place(
        TestAssets assets, string assembly, ManagedLayoutSnapshot layouts, StructuredMethod structured,
        MethodRootMap roots)
    {
        using var metadata = MetadataCompilationTestFactory.Load(assembly, [assets.CoreLib]);
        var snapshot = metadata.Snapshot;
        var fields = MetadataCompilationTestActors.Fields(snapshot);
        var methods = MetadataCompilationTestActors.Methods(snapshot);
        var types = MetadataCompilationTestActors.Types(snapshot);
        var definitions = MetadataCompilationTestActors.TypeDefinitions(snapshot);
        var identities = new CilTypeIdentityResolver(types);
        var values = new ValueLayoutProvider(CreateValueLayouts(definitions, fields, layouts));
        var layout = new ValueFrameLayoutPlanner(
            fields, methods, layouts, values,
            new CilTypeOperandResolver(identities), identities,
            new ArgumentTypeResolver(types), new ArgumentSignatureTypeResolver(identities),
            new DirectValueLoadSelector(),
            new TransientAggregatePromotionPlanner(
                methods, new RuntimeIntrinsicRegistry(
                    MetadataCompilationTestActors.Symbols(snapshot), snapshot.Methods),
                values, CreateInstanceFields(layouts)),
            new SimdValueFrameSlotAlignmentResolver()).Create(structured);
        var planner = Assert.IsAssignableFrom<ITerminalBranchValueFramePlanner>(
            new TerminalBranchValueFramePlanner(new ArgumentSignatureTypeResolver(identities)));
        return (layout, planner.Plan(structured, layout, roots));
    }
}
