using System.Collections.Immutable;
using NetWasm.Compiler.ComponentModel.Raw;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.ComponentModel.Tests.Raw;

public sealed class RawWitBindingPlanBuilderTests
{
    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void BuildsOnlySelectedWitLayoutsAndRetainsTheCompleteSelection(WasmTarget target)
    {
        var dependencies = new RecordingDependencies(target);
        var request = dependencies.Request();

        var plan = Create(dependencies).Build(request);

        Assert.Same(request, dependencies.SelectionRequest);
        Assert.Same(dependencies.Selection, plan.Selection);
        Assert.Equal(2, plan.Imports.Length);
        Assert.Same(dependencies.Results[0], plan.Imports[0]);
        Assert.Same(dependencies.Results[1], plan.Imports[1]);
        Assert.Equal(["select", "layout", "layout"], dependencies.Calls);
        Assert.Equal(dependencies.Selection.WitImports, plan.Imports.Select(layout => layout.Identity));
        Assert.All(dependencies.LayoutRequests, layoutRequest =>
        {
            Assert.Same(dependencies.Selection.Catalog.Document, layoutRequest.Document);
            Assert.Equal(target, layoutRequest.Target);
        });
        Assert.Same(dependencies.Selection.Catalog.Imports[dependencies.Selection.WitImports[0]], dependencies.LayoutRequests[0].Declaration);
        Assert.Same(dependencies.Selection.Catalog.Imports[dependencies.Selection.WitImports[1]], dependencies.LayoutRequests[1].Declaration);
        Assert.Single(plan.Selection.JavaScriptImports);
        Assert.Single(plan.Selection.RuntimeImports);
        Assert.Equal(3, plan.Selection.Catalog.Imports.Count);
    }

    [Fact]
    public void EmptyWitSelectionDoesNotRequestLayouts()
    {
        var dependencies = new RecordingDependencies(WasmTarget.Wasm32);
        dependencies.Selection = dependencies.Selection with { WitImports = [] };

        var plan = Create(dependencies).Build(dependencies.Request());

        Assert.Same(dependencies.Selection, plan.Selection);
        Assert.Empty(plan.Imports);
        Assert.Equal(["select"], dependencies.Calls);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void IdentityOrTargetDriftRejectsThePlanBeforeLaterLayouts(bool wrongIdentity, bool wrongTarget)
    {
        var dependencies = new RecordingDependencies(WasmTarget.Wasm32);
        var first = Assert.IsType<RawWitImportLayout.Resource>(dependencies.Results[0]);
        dependencies.Results[0] = new RawWitImportLayout.Resource(first.Intrinsic with
        {
            Identity = wrongIdentity ? new("different", "member") : first.Identity,
            Target = wrongTarget ? WasmTarget.Wasm64 : first.Target,
        });

        Assert.Throws<CompilerException>(() => Create(dependencies).Build(dependencies.Request()));
        Assert.Equal(["select", "layout"], dependencies.Calls);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void DependencyFailureDoesNotPublishAPartialPlan(int failingCall)
    {
        var dependencies = new RecordingDependencies(WasmTarget.Wasm64) { FailingCall = failingCall };

        Assert.Same(dependencies.Failure, Assert.Throws<InvalidOperationException>(() => Create(dependencies).Build(dependencies.Request())));
        Assert.Equal(failingCall, dependencies.Calls.Count);
    }

    [Fact]
    public void RequiresRequestAndBothCapabilitiesBeforeDelegation()
    {
        var dependencies = new RecordingDependencies(WasmTarget.Wasm32);
        Assert.Throws<ArgumentNullException>(() => Create(dependencies).Build(null!));
        Assert.Throws<ArgumentNullException>(() => new RawWitBindingPlanBuilder(null!, dependencies));
        Assert.Throws<ArgumentNullException>(() => new RawWitBindingPlanBuilder(dependencies, null!));
        Assert.Empty(dependencies.Calls);
    }

    private static IRawWitBindingPlanBuilder Create(RecordingDependencies dependencies) =>
        Assert.IsAssignableFrom<IRawWitBindingPlanBuilder>(new RawWitBindingPlanBuilder(dependencies, dependencies));

    private sealed class RecordingDependencies : IRawImportBindingSelector, IRawWitImportLayoutBuilder
    {
        public RecordingDependencies(WasmTarget target)
        {
            var declaration = new RawWitImportDeclaration.Resource("", new(0, "file", default, 0), CanonicalAbiFunctionKind.ImportedResourceDrop);
            var first = new RawCanonicalImportIdentity("module", "first");
            var second = new RawCanonicalImportIdentity("module", "second");
            var unused = new RawCanonicalImportIdentity("module", "unused");
            var world = new WitWorld(0, "main", "example:test@1.0.0", [], []);
            var catalog = new RawWitImportCatalog(new([], [], [world], [], "{}"), world, target,
                ImmutableDictionary<RawCanonicalImportIdentity, RawWitImportDeclaration>.Empty
                    .Add(first, declaration).Add(second, declaration with { Kind = CanonicalAbiFunctionKind.ExportedResourceDrop })
                    .Add(unused, declaration));
            Selection = new(catalog, [second, first], [new("js", "call")], [new("runtime", "watch")]);
            Results = [.. Selection.WitImports.Select(identity => new RawWitImportLayout.Resource(new(target,
                (RawWitImportDeclaration.Resource)catalog.Imports[identity], identity,
                new([CliValueKind.I4], CliValueKind.Void, [CliValueKind.I4], [], false, false))))];
        }

        public RawImportBindingSelection Selection { get; set; }
        public RawWitImportLayout[] Results { get; }
        public List<string> Calls { get; } = [];
        public List<RawWitImportLayoutRequest> LayoutRequests { get; } = [];
        public RawImportBindingSelectionRequest? SelectionRequest { get; private set; }
        public int FailingCall { get; init; }
        public InvalidOperationException Failure { get; } = new();

        public RawImportBindingSelectionRequest Request() => new(Selection.Catalog,
            [.. Selection.WitImports, .. Selection.JavaScriptImports, .. Selection.RuntimeImports],
            Selection.JavaScriptImports.ToImmutableHashSet(), Selection.RuntimeImports.ToImmutableHashSet());

        public RawImportBindingSelection SelectBindings(RawImportBindingSelectionRequest request)
        {
            Enter("select");
            SelectionRequest = request;
            return Selection;
        }

        public RawWitImportLayout Build(RawWitImportLayoutRequest request)
        {
            Enter("layout");
            LayoutRequests.Add(request);
            return Results[LayoutRequests.Count - 1];
        }

        private void Enter(string name)
        {
            Calls.Add(name);
            if (Calls.Count == FailingCall) throw Failure;
        }
    }
}
