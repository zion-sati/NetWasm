using System.Collections.Immutable;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Tests.Correctness;

public sealed class CorpusCompilerRequestFactoryTests
{
    [Theory]
    [InlineData(WasmTarget.Wasm32, false)]
    [InlineData(WasmTarget.Wasm64, false)]
    [InlineData(WasmTarget.Wasm32, true)]
    [InlineData(WasmTarget.Wasm64, true)]
    public void CreatePreservesAssemblyAndTargetAndOptionalLinkedArtifacts(WasmTarget target, bool reactor)
    {
        var compilation = CreateCompilation() with
        {
            Fixture = CreateCompilation().Fixture with
            {
                RequiresReactor = reactor,
                EmitStackTrace = true,
                WasmEntryMethod = "Start",
                NetWasmReferencePaths = ["library.dll"],
            },
        };
        var request = ((ICorpusCompilerRequestFactory)CreateFactory()).Create(compilation, target, "trace", "module", "symbols",
            [new("run", compilation.Fixture.EntryType, "Start")], "layout", "interop");

        Assert.Equal("retained.dll", request.EntryAssemblyPath);
        Assert.Equal(target, request.Target);
        Assert.Equal(["corelib.dll", "library.dll"], request.ReferencePaths.AsEnumerable());
        Assert.Equal(compilation.Fixture.EntryType, request.EntryTypeName);
        Assert.Equal("Start", request.EntryMethodName);
        Assert.Equal(new("run", compilation.Fixture.EntryType, "Start"), Assert.Single(request.Exports));
        Assert.Equal("trace", request.DiagnosticTracePath);
        Assert.Equal("module", request.ModulePath);
        Assert.True(request.EmitStackTrace);
        Assert.Equal("symbols", request.StackTraceSymbolsPath);
        Assert.Equal("layout", request.RuntimeLayoutPath);
        Assert.Equal("interop", request.InteropManifestPath);
        Assert.Equal(reactor ? Path.Combine("repository", "wit", "netwasm-platform-1.0.0") : null, request.WitPath);
        Assert.Equal(reactor ? "netwasm:platform@1.0.0/async-platform" : null, request.WitWorld);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void CreatePreservesSourceProvenanceAndLegacyDefaults(int sourceKind)
    {
        var compilation = CreateCompilation() with
        {
            Profile = sourceKind == 1 ? CilProfile.Emitted : CilProfile.Debug,
            Sources = sourceKind == 2 ? [new("first.cs", "source/first.cs", "hash1"), new("second.cs", "source/second.cs", "hash2")] : [],
        };
        var request = ((ICorpusCompilerRequestFactory)CreateFactory()).Create(compilation, WasmTarget.Wasm32, null, "module", null, []);

        Assert.Equal(sourceKind switch
        {
            0 => [Path.Combine("run", "Fixture.cs")],
            1 => Array.Empty<string>(),
            _ => ["source/first.cs", "source/second.cs"],
        }, request.SourcePaths);
        Assert.Null(request.RuntimeLayoutPath);
        Assert.Null(request.InteropManifestPath);
        Assert.Null(request.DiagnosticTracePath);
        Assert.Null(request.StackTraceSymbolsPath);
        Assert.False(request.EmitStackTrace);
        Assert.Empty(request.Exports);
    }

    [Fact]
    public void CreateMergesCompatibleAliasesWithoutMutatingPolicy()
    {
        var aliases = ImmutableDictionary<string, string>.Empty.Add("System.Runtime", "NetWasm.CoreLib");
        var policy = new StubRegistry(aliases);
        var factory = Assert.IsAssignableFrom<ICorpusCompilerRequestFactory>(new CorpusCompilerRequestFactory(CreateEnvironment(), policy));
        var compilation = CreateCompilation() with
        {
            Fixture = CreateCompilation().Fixture with
            {
                ReferenceAssemblyAliases = aliases.Add("Library", "Library.Port"),
                OracleMode = OracleMode.FrozenDesktop,
            },
        };

        var request = factory.Create(compilation, WasmTarget.Wasm64, null, "module", null, []);

        Assert.Equal(OracleMode.FrozenDesktop, policy.RequestedMode);
        Assert.Equal("NetWasm.CoreLib", request.ReferenceAssemblyAliases["System.Runtime"]);
        Assert.Equal("Library.Port", request.ReferenceAssemblyAliases["Library"]);
        Assert.Single(aliases);
    }

    [Fact]
    public void CreateRejectsConflictingAliasesAndNullCompilation()
    {
        var factory = Assert.IsAssignableFrom<ICorpusCompilerRequestFactory>(CreateFactory());
        var compilation = CreateCompilation() with
        {
            Fixture = CreateCompilation().Fixture with
            {
                ReferenceAssemblyAliases = ImmutableDictionary<string, string>.Empty.Add("System.Runtime", "Conflict"),
            },
        };

        Assert.Throws<InvalidOperationException>(() => factory.Create(compilation, WasmTarget.Wasm32, null, "module", null, []));
        Assert.Throws<ArgumentNullException>(() => factory.Create(null!, WasmTarget.Wasm32, null, "module", null, []));
    }

    private static CorpusCompilerRequestFactory CreateFactory() => new(
        CreateEnvironment(), new StubRegistry(ImmutableDictionary<string, string>.Empty.Add("System.Runtime", "NetWasm.CoreLib")));

    private static CompilerCorrectnessEnvironment CreateEnvironment() =>
        new("repository", "dotnet", "sdk", "roslyn", "corelib.dll", "references", "node", "oracle", "compiler", TimeSpan.FromSeconds(1));

    private static CorpusCompilation CreateCompilation()
    {
        var artifact = new CorpusArtifact("retained.dll", "retained.pdb", "pe-hash", "pdb-hash", "compiler", []);
        return new(new("Fixture", "Tests", "", [0]), CilProfile.Debug, artifact, artifact, "run");
    }

    private sealed class StubRegistry(ImmutableDictionary<string, string> aliases) : IOracleModePolicyRegistry
    {
        public OracleMode? RequestedMode { get; private set; }
        public IOracleModePolicy Get(OracleMode mode)
        {
            RequestedMode = mode;
            return new StubPolicy(aliases);
        }
    }

    private sealed class StubPolicy(ImmutableDictionary<string, string> aliases) : IOracleModePolicy
    {
        public OracleMode Mode => OracleMode.SameIl;
        public bool SharesPortableExecutable => true;
        public ImmutableDictionary<string, string> ReferenceAssemblyAliases => aliases;
        public void Validate(CorpusFixture fixture) => throw new NotSupportedException();
        public void ValidateCompilation(CorpusCompilation compilation) => throw new NotSupportedException();
    }
}
