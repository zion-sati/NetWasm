using System.Collections.Immutable;
using NetWasm.Compiler.Metadata.Inputs;
using NetWasm.Compiler.Pipeline;

namespace NetWasm.Compiler.Tests.Units;

// A unit depends on the shapes of its assembly, of everything that assembly references
// transitively, and of the core assemblies: a shape change anywhere else leaves it alone.
public sealed class UnitAssemblyShapesTests
{
    private static readonly Dictionary<string, ImmutableArray<string>> References = new(StringComparer.Ordinal)
    {
        ["App"] = ["Lib", "Missing"],
        ["Facaded"] = ["LibFacade"],
        ["Lib"] = ["CoreLib"],
        ["CoreLib"] = [],
        ["Cycle"] = ["Loop", "CoreLib"],
        ["Loop"] = ["Cycle"],
    };

    private static readonly Dictionary<string, string> Aliases = new(StringComparer.Ordinal) { ["LibFacade"] = "Lib" };

    private static System.Collections.Frozen.FrozenDictionary<string, Wasm.Units.UnitFingerprint> Shapes(string? changed = null,
        IReadOnlyDictionary<string, string>? aliases = null) =>
        UnitAssemblyShapes.Of(References, aliases ?? Aliases,
            References.Keys.ToDictionary(name => name, name => new InputFingerprint(name == changed ? 2UL : 1UL, 0),
                StringComparer.Ordinal));

    // Which assemblies' units a shape change reaches.
    private static IEnumerable<string> Reached(string changed, IReadOnlyDictionary<string, string>? aliases = null)
    {
        var before = Shapes(aliases: aliases);
        var after = Shapes(changed, aliases);
        return before.Keys.Where(name => before[name] != after[name]).Order(StringComparer.Ordinal);
    }

    [Fact]
    public void AShapeReachesItsAssemblyAndThoseThatReferenceIt() =>
        Assert.Equal(["App", "Facaded", "Lib"], Reached("Lib"));

    [Fact]
    public void ACoreAssemblysShapeReachesEveryAssembly() =>
        Assert.Equal(References.Keys.Order(StringComparer.Ordinal), Reached("CoreLib"));

    [Fact]
    public void AnAssemblyThatReferencesNoneReachesNoneButItself() => Assert.Equal(["App"], Reached("App"));

    // A reference cycle closes over both.
    [Fact]
    public void ACycleReachesBothAssemblies() => Assert.Equal(["Cycle", "Loop"], Reached("Loop"));

    // A reference resolves through its alias; without it, a facade the program lacks reaches nothing.
    [Fact]
    public void AReferenceResolvesThroughItsAlias()
    {
        Assert.Contains("Facaded", Reached("Lib"));
        Assert.DoesNotContain("Facaded", Reached("Lib", new Dictionary<string, string>()));
        Assert.Equal(Shapes(), UnitAssemblyShapes.Of(References, Aliases,
            References.Keys.ToDictionary(name => name, _ => new InputFingerprint(1, 0), StringComparer.Ordinal)));
    }
}

public sealed class CompiledUnitStateFactoryTests
{
    [Fact]
    public void ItNeedsItsStoreAndItsStructuring()
    {
        Assert.Throws<ArgumentNullException>(() => new CompiledUnitStateFactory(null!, new NoStructuring(), null!, null!, null!, null!));
        Assert.Throws<ArgumentNullException>(() => new CompiledUnitStateFactory(new NoStore(), null!, null!, null!, null!, null!));
    }

    private sealed class NoStore : Wasm.Units.IUnitStoreLoader
    {
        public Wasm.Units.UnitStore Load(string location) => throw new NotSupportedException();
    }

    private sealed class NoStructuring : Wasm.Emission.Planning.IWasmMethodLowerer
    {
        public ControlFlow.Structured.StructuredMethod Lower(ControlFlow.ManagedMethodBody method,
            NetWasm.Compiler.ComponentModel.Optimization.BranchPrefixDuplicationPolicy branchPrefixDuplication,
            NetWasm.Compiler.ComponentModel.Optimization.DispatcherEliminationPolicy dispatcherElimination) => throw new NotSupportedException();
    }
}
