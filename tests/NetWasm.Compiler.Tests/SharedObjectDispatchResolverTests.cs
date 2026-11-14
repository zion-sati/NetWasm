using NetWasm.Compiler.Analysis;
using NetWasm.Compiler.Core;
using static NetWasm.Compiler.Tests.ExecutionPlannerFixture;

namespace NetWasm.Compiler.Tests;

// Object's Equals(object), GetHashCode() and ToString() have shared bodies in CoreLib's SharedObjectDispatch; no
// other declaration does.
public sealed class SharedObjectDispatchResolverTests
{
    private static readonly CliTypeIdentity Object = CliTypeIdentity.FromStackKind(CliValueKind.ManagedReference);

    [Theory]
    [InlineData("Equals", 1, "DispatchEquals")]
    [InlineData("GetHashCode", 0, "DispatchGetHashCode")]
    [InlineData("ToString", 0, "DispatchToString")]
    public void ObjectsVirtualCallsResolveToTheirSharedBody(string name, int parameters, string expected)
    {
        var f = new ExecutionPlannerFixture();
        RegisterHelpers(f);
        var declaration = f.Method(1, name, Object, parameters: [.. Enumerable.Repeat(Object, parameters)]);

        var helper = Create(f).Resolve(declaration);

        Assert.Equal(expected, helper?.Definition.Name);
    }

    [Fact]
    public void OtherDeclarationsHaveNoSharedBody()
    {
        var f = new ExecutionPlannerFixture();
        RegisterHelpers(f);
        var resolver = Create(f);

        Assert.Null(resolver.Resolve(f.Method(1, "GetHashCode", Type("Owner"))));
        Assert.Null(resolver.Resolve(f.Method(2, "Equals", Object, parameters: [Object, Object], isStatic: true)));
        Assert.Null(resolver.Resolve(f.Method(3, "Equals", Object)));
        Assert.Null(resolver.Resolve(f.Method(4, "Finalize", Object)));
        Assert.Empty(f.Lookups);
    }

    [Fact]
    public void MissingSharedBodyIsARuntimeContractFailure()
    {
        var f = new ExecutionPlannerFixture();
        f.RegisterType("System.Runtime.CompilerServices.SharedObjectDispatch");

        var error = Assert.Throws<CompilerException>(() =>
            Create(f).Resolve(f.Method(1, "GetHashCode", Object)));

        Assert.Equal(DiagnosticCode.RuntimeContract, error.Diagnostic.Code);
    }

    [Fact]
    public void RejectsNullDependenciesAndDeclaration()
    {
        var f = new ExecutionPlannerFixture();
        Assert.Throws<ArgumentNullException>(() => new SharedObjectDispatchResolver(null!, f, f, f));
        Assert.Throws<ArgumentNullException>(() => new SharedObjectDispatchResolver(f, null!, f, f));
        Assert.Throws<ArgumentNullException>(() => new SharedObjectDispatchResolver(f, f, null!, f));
        Assert.Throws<ArgumentNullException>(() => new SharedObjectDispatchResolver(f, f, f, null!));
        Assert.Throws<ArgumentNullException>(() => Create(f).Resolve(null!));
    }

    private static SharedObjectDispatchResolver Create(ExecutionPlannerFixture f) => new(f, f, f, f);

    private static void RegisterHelpers(ExecutionPlannerFixture f) => f.RegisterType(
        "System.Runtime.CompilerServices.SharedObjectDispatch",
        f.Method(100, "DispatchEquals", isStatic: true, parameters: [Object, Object]),
        f.Method(101, "DispatchGetHashCode", isStatic: true, parameters: [Object]),
        f.Method(102, "DispatchToString", Object, Object, isStatic: true, parameters: [Object]));
}
