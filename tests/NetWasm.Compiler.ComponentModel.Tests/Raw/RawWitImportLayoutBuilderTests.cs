using NetWasm.Compiler.ComponentModel.Raw;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.ComponentModel.Tests.Raw;

public sealed class RawWitImportLayoutBuilderTests
{
    [Fact]
    public void DispatchesExactlyOneBuilderByDeclarationTypeAndSnapshotsRegistrations()
    {
        var callable = new RecordingBuilder();
        var resource = new RecordingBuilder();
        var custom = new RecordingBuilder();
        var registrations = new List<RawWitImportLayoutRegistration>
        {
            new(typeof(RawWitImportDeclaration.Callable), callable),
            new(typeof(RawWitImportDeclaration.Resource), resource),
            new(typeof(CustomDeclaration), custom),
        };
        var builder = Create(registrations);
        registrations.Clear();
        var document = new WitDocument([], [], [], [], "{}");
        var callRequest = new RawWitImportLayoutRequest(document, new RawWitImportDeclaration.Callable("", null!), WasmTarget.Wasm32);
        var resourceRequest = new RawWitImportLayoutRequest(document, new RawWitImportDeclaration.Resource("", null!, CanonicalAbiFunctionKind.ImportedResourceDrop), WasmTarget.Wasm64);
        var customRequest = new RawWitImportLayoutRequest(document, new CustomDeclaration(), WasmTarget.Wasm64);

        Assert.Same(callable.Result, builder.Build(callRequest));
        Assert.Same(resource.Result, builder.Build(resourceRequest));
        Assert.Same(custom.Result, builder.Build(customRequest));
        Assert.Equal([callRequest], callable.Requests);
        Assert.Equal([resourceRequest], resource.Requests);
        Assert.Equal([customRequest], custom.Requests);
    }

    [Fact]
    public void UnknownDeclarationsAndMissingRequestsDoNotProbeOtherBuilders()
    {
        var dependency = new RecordingBuilder();
        var builder = Create(Registrations(dependency));

        Assert.Throws<ArgumentNullException>(() => builder.Build(null!));
        Assert.Throws<ArgumentNullException>(() => builder.Build(new(new([], [], [], [], "{}"), null!, WasmTarget.Wasm32)));
        Assert.Throws<CompilerException>(() => builder.Build(new(new([], [], [], [], "{}"), new CustomDeclaration(), WasmTarget.Wasm32)));
        Assert.Empty(dependency.Requests);
    }

    [Fact]
    public void InvalidOrIncompleteRegistrationsFailAtComposition()
    {
        var dependency = new RecordingBuilder();
        Assert.Throws<ArgumentNullException>(() => Create(null!));
        Assert.Throws<ArgumentNullException>(() => Create([null!]));
        Assert.Throws<ArgumentNullException>(() => Create([new(null!, dependency)]));
        Assert.Throws<ArgumentNullException>(() => Create([new(typeof(RawWitImportDeclaration.Callable), null!)]));
        Assert.Throws<ArgumentException>(() => Create([new(typeof(string), dependency)]));
        Assert.Throws<ArgumentException>(() => Create([new(typeof(RawWitImportDeclaration), dependency)]));
        Assert.Throws<ArgumentException>(() => Create([]));
        Assert.Throws<ArgumentException>(() => Create([new(typeof(RawWitImportDeclaration.Callable), dependency)]));
        Assert.Throws<ArgumentException>(() => Create([new(typeof(RawWitImportDeclaration.Resource), dependency)]));
        Assert.Throws<ArgumentException>(() => Create([.. Registrations(dependency), new(typeof(RawWitImportDeclaration.Callable), dependency)]));
        Assert.Empty(dependency.Requests);
    }

    [Fact]
    public void SelectedBuilderFailureIsNotRetriedThroughAnotherVariant()
    {
        var callable = new RecordingBuilder { Failure = new() };
        var resource = new RecordingBuilder();
        var builder = Create([new(typeof(RawWitImportDeclaration.Callable), callable), new(typeof(RawWitImportDeclaration.Resource), resource)]);

        Assert.Same(callable.Failure, Assert.Throws<InvalidOperationException>(() => builder.Build(new(
            new([], [], [], [], "{}"), new RawWitImportDeclaration.Callable("", null!), WasmTarget.Wasm32))));
        Assert.Single(callable.Requests);
        Assert.Empty(resource.Requests);
    }

    private static IRawWitImportLayoutBuilder Create(IEnumerable<RawWitImportLayoutRegistration> registrations) =>
        Assert.IsAssignableFrom<IRawWitImportLayoutBuilder>(new RawWitImportLayoutBuilder(registrations));

    private static RawWitImportLayoutRegistration[] Registrations(IRawWitImportLayoutBuilder builder) =>
        [new(typeof(RawWitImportDeclaration.Callable), builder), new(typeof(RawWitImportDeclaration.Resource), builder)];

    private sealed record CustomDeclaration() : RawWitImportDeclaration("");

    private sealed class RecordingBuilder : IRawWitImportLayoutBuilder
    {
        public List<RawWitImportLayoutRequest> Requests { get; } = [];
        public InvalidOperationException? Failure { get; init; }
        public RawWitImportLayout Result { get; } = new RawWitImportLayout.Resource(new(WasmTarget.Wasm32,
            new("", new(0, "file", default, 0), CanonicalAbiFunctionKind.ImportedResourceDrop),
            new("physical", "member"), new([], CliValueKind.Void, [], [], false, false)));

        public RawWitImportLayout Build(RawWitImportLayoutRequest request)
        {
            Requests.Add(request);
            if (Failure is not null) throw Failure;
            return Result;
        }
    }
}
