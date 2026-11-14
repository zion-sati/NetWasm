using NetWasm.Compiler.Core;
using NetWasm.Compiler.Metadata.UnsafeAccessors;

namespace NetWasm.Compiler.Metadata.Tests.UnsafeAccessors;

public sealed class UnsafeAccessorTypeNameResolverTests
{
    private static readonly AssemblyIdentity CoreLibrary = new("NetWasm.CoreLib");
    private static readonly AssemblyIdentity Claims = new("System.Security.Claims");
    private static readonly AssemblyIdentity Application = new("Application");
    private static readonly TypeDefinitionModel Principal = Type(Claims, "System.Security.Principal", "GenericPrincipal");
    private static readonly TypeDefinitionModel Local = Type(Application, "App", "Hidden");
    private static readonly TypeDefinitionModel Nested = Type(Application, "App", "Outer+Inner");
    private static readonly TypeDefinitionModel CoreType = Type(CoreLibrary, "System", "Shared");
    private static readonly TypeDefinitionModel Shadow = Type(Application, "System", "Shared");

    [Theory]
    [InlineData("System.Security.Principal.GenericPrincipal, System.Security.Claims")]
    [InlineData("System.Security.Principal.GenericPrincipal, System.Security.Claims, Version=11.0.0.0, Culture=neutral")]
    [InlineData(" System.Security.Principal.GenericPrincipal ,  System.Security.Claims ")]
    public void AssemblyQualifiedNamesResolveInThatAssembly(string name) =>
        Assert.Same(Principal, Resolver().Resolve(name, Application));

    [Fact]
    public void UnqualifiedNamesResolveInTheAccessorsAssemblyThenCoreLib()
    {
        Assert.Same(Local, Resolver().Resolve("App.Hidden", Application));
        Assert.Same(Nested, Resolver().Resolve("App.Outer+Inner", Application));
        Assert.Same(Shadow, Resolver().Resolve("System.Shared", Application));
        Assert.Same(CoreType, Resolver().Resolve("System.Shared", Claims));
    }

    [Theory]
    [InlineData("System.Security.Principal.GenericPrincipal, Other")]
    [InlineData("System.Security.Principal.GenericPrincipal")]
    [InlineData("App.Missing, Application")]
    public void NamesThatTheProgramDoesNotDefineResolveToNothing(string name) =>
        Assert.Null(Resolver().Resolve(name, Application));

    [Theory]
    [InlineData("System.Collections.Generic.List`1[[System.Int32]], System.Collections")]
    [InlineData("App.Hidden[]")]
    [InlineData("App.Hidden*")]
    [InlineData("App.Hidden&")]
    public void GenericArrayPointerAndByRefNamesAreUnsupported(string name)
    {
        var exception = Assert.Throws<CompilerException>(() => Resolver().Resolve(name, Application));
        Assert.Equal(DiagnosticCode.UnsupportedMetadata, exception.Diagnostic.Code);
    }

    [Fact]
    public void DuplicateDefinitionsKeepTheFirst()
    {
        var duplicate = Type(Application, "App", "Hidden");
        var resolver = new UnsafeAccessorTypeNameResolver([Local, duplicate], CoreLibrary);
        Assert.Same(Local, resolver.Resolve("App.Hidden", Application));
    }

    [Fact]
    public void InvalidInputsFailAtTheBoundary()
    {
        Assert.Throws<ArgumentNullException>(() => new UnsafeAccessorTypeNameResolver(null!, CoreLibrary));
        Assert.Throws<ArgumentException>(() => Resolver().Resolve(" ", Application));
        Assert.Throws<ArgumentNullException>(() => Resolver().Resolve(null!, Application));
    }

    // Through the contract, as the target resolver uses it.
    private static IUnsafeAccessorTypeNameResolver Resolver() =>
        Assert.IsAssignableFrom<IUnsafeAccessorTypeNameResolver>(
            new UnsafeAccessorTypeNameResolver([Principal, Local, Nested, CoreType, Shadow], CoreLibrary));

    private static TypeDefinitionModel Type(AssemblyIdentity assembly, string ns, string name) =>
        new(new(assembly, 0x02000001), ns, name, false, [], []);
}
