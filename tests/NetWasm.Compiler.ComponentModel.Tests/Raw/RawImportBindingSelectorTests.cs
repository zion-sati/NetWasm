using System.Collections.Immutable;
using NetWasm.Compiler.ComponentModel.Raw;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.ComponentModel.Tests.Raw;

public sealed class RawImportBindingSelectorTests
{
    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void SelectsOnlyFinalImportsWithExactOwnershipAndOrdinalOrder(WasmTarget target)
    {
        var first = new RawCanonicalImportIdentity("wit", "B");
        var second = new RawCanonicalImportIdentity("wit", "a");
        var earlierModule = new RawCanonicalImportIdentity("Wit", "z");
        var unused = new RawCanonicalImportIdentity("wit", "unused");
        var javascript = new RawCanonicalImportIdentity("app", "js");
        var runtime = new RawCanonicalImportIdentity("netwasm:runtime/reactor-host", "watch");
        var request = new RawImportBindingSelectionRequest(Catalog(target, [second, unused, first, earlierModule]),
            [runtime, second, javascript, first, earlierModule], [javascript], [runtime]);

        var selected = Create().SelectBindings(request);
        var repeated = Create().SelectBindings(request with { FinalImports = [.. request.FinalImports.Reverse()] });

        Assert.Same(request.Catalog, selected.Catalog);
        Assert.Equal([earlierModule, first, second], selected.WitImports);
        Assert.Equal([javascript], selected.JavaScriptImports);
        Assert.Equal([runtime], selected.RuntimeImports);
        Assert.Equal(selected.WitImports, repeated.WitImports);
        Assert.Equal(selected.JavaScriptImports, repeated.JavaScriptImports);
        Assert.Equal(selected.RuntimeImports, repeated.RuntimeImports);
        Assert.Equal(4, request.Catalog.Imports.Count);
        Assert.DoesNotContain(unused, selected.WitImports);
    }

    [Fact]
    public void EmptyFinalInventoryDoesNotActivateDeclaredImports()
    {
        var selected = Create().SelectBindings(new(Catalog(WasmTarget.Wasm32, [new("wit", "unused")]),
            [], [new("js", "unused")], [new("runtime", "unused")]));

        Assert.Empty(selected.WitImports);
        Assert.Empty(selected.JavaScriptImports);
        Assert.Empty(selected.RuntimeImports);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void MultipleDeclaredOwnersAreRejectedEvenWhenTheImportIsUnused(int pair)
    {
        var identity = new RawCanonicalImportIdentity("module", "member");
        var request = new RawImportBindingSelectionRequest(Catalog(WasmTarget.Wasm32, pair == 2 ? [] : [identity]), [],
            pair == 1 ? [] : [identity], pair == 0 ? [] : [identity]);

        var failure = Assert.Throws<CompilerException>(() => Create().SelectBindings(request));

        Assert.Equal(DiagnosticCode.ComponentContract, failure.Diagnostic.Code);
        Assert.Contains("multiple declared owners", failure.Diagnostic.Message);
    }

    [Fact]
    public void DuplicateFinalEntriesAreNotSilentlyDeduplicated()
    {
        var identity = new RawCanonicalImportIdentity("wit", "member");

        var failure = Assert.Throws<CompilerException>(() => Create().SelectBindings(new(
            Catalog(WasmTarget.Wasm64, [identity]), [identity, identity], [], [])));

        Assert.Contains("duplicate", failure.Diagnostic.Message);
    }

    [Fact]
    public void MissingOrCaseMismatchedImportCannotUseAFallbackBinding()
    {
        var request = new RawImportBindingSelectionRequest(Catalog(WasmTarget.Wasm32, [new("wit", "member")]),
            [new("wit", "Member")], [], []);

        var failure = Assert.Throws<CompilerException>(() => Create().SelectBindings(request));

        Assert.Contains("no declared binding", failure.Diagnostic.Message);
    }

    [Fact]
    public void CallerCollectionComparersCannotChangePhysicalIdentityOwnership()
    {
        var lower = new RawCanonicalImportIdentity("module", "member");
        var upper = new RawCanonicalImportIdentity("module", "MEMBER");
        var third = new RawCanonicalImportIdentity("MODULE", "MEMBER");
        var comparer = new IgnoreCaseIdentityComparer();
        var catalog = Catalog(WasmTarget.Wasm64, [lower]);
        catalog = catalog with { Imports = catalog.Imports.WithComparers(comparer) };
        var javascript = ImmutableHashSet.Create(comparer, upper);
        var runtime = ImmutableHashSet.Create(comparer, third);

        var selection = Create().SelectBindings(new(catalog, [third, upper, lower], javascript, runtime));

        Assert.Equal([lower], selection.WitImports);
        Assert.Equal([upper], selection.JavaScriptImports);
        Assert.Equal([third], selection.RuntimeImports);
    }

    [Fact]
    public void MissingInputsAndInvalidTargetOrInventoryAreRejected()
    {
        var selector = Create();
        var request = new RawImportBindingSelectionRequest(Catalog(WasmTarget.Wasm32, []), [], [], []);

        Assert.Throws<ArgumentNullException>(() => selector.SelectBindings(null!));
        Assert.Throws<ArgumentNullException>(() => selector.SelectBindings(request with { Catalog = null! }));
        Assert.Throws<ArgumentNullException>(() => selector.SelectBindings(request with { Catalog = request.Catalog with { Imports = null! } }));
        Assert.Throws<ArgumentNullException>(() => selector.SelectBindings(request with { JavaScriptImports = null! }));
        Assert.Throws<ArgumentNullException>(() => selector.SelectBindings(request with { RuntimeImports = null! }));
        Assert.Throws<ArgumentOutOfRangeException>(() => selector.SelectBindings(request with { Catalog = request.Catalog with { Target = (WasmTarget)99 } }));
        Assert.Throws<CompilerException>(() => selector.SelectBindings(request with { FinalImports = default }));
        Assert.Throws<ArgumentNullException>(() => selector.SelectBindings(request with { FinalImports = [null!] }));
        Assert.Throws<ArgumentException>(() => selector.SelectBindings(request with { FinalImports = [new(" ", "member")] }));
        Assert.Throws<ArgumentException>(() => selector.SelectBindings(request with { FinalImports = [new("module", " ")] }));
        Assert.Throws<ArgumentNullException>(() => selector.SelectBindings(request with { JavaScriptImports = [null!] }));
        Assert.Throws<ArgumentException>(() => selector.SelectBindings(request with { RuntimeImports = [new(" ", "member")] }));
        Assert.Throws<ArgumentException>(() => selector.SelectBindings(request with { Catalog = Catalog(WasmTarget.Wasm32, [new("module", " ")]) }));
    }

    private static IRawImportBindingSelector Create() =>
        Assert.IsAssignableFrom<IRawImportBindingSelector>(new RawImportBindingSelector());

    private static RawWitImportCatalog Catalog(WasmTarget target, RawCanonicalImportIdentity[] imports)
    {
        var world = new WitWorld(0, "test", "example:test@1.0.0", [], []);
        var declaration = new RawWitImportDeclaration.Callable("", new("member", [], null, new("freestanding")));
        return new(new([], [], [world], [], "{}"), world, target,
            imports.ToImmutableDictionary(identity => identity, _ => (RawWitImportDeclaration)declaration));
    }

    private sealed class IgnoreCaseIdentityComparer : IEqualityComparer<RawCanonicalImportIdentity>
    {
        public bool Equals(RawCanonicalImportIdentity? left, RawCanonicalImportIdentity? right) =>
            StringComparer.OrdinalIgnoreCase.Equals(left!.Module, right!.Module) &&
            StringComparer.OrdinalIgnoreCase.Equals(left.Name, right.Name);

        public int GetHashCode(RawCanonicalImportIdentity identity) => HashCode.Combine(
            StringComparer.OrdinalIgnoreCase.GetHashCode(identity.Module),
            StringComparer.OrdinalIgnoreCase.GetHashCode(identity.Name));
    }
}
