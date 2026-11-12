using System.Collections.Immutable;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Metadata;
using NetWasm.Compiler.StackTraces;

namespace NetWasm.Compiler.Tests;

public sealed class StackTraceReachabilityRootProviderTests
{
    private static readonly AssemblyIdentity Assembly =
        new("StackTraceRoots");
    private static readonly EntityKey ExceptionKey = new(Assembly, 0x02000001);
    private static readonly EntityKey StringKey = new(Assembly, 0x02000002);
    private static readonly EntityKey SidecarsKey = new(Assembly, 0x02000003);
    private static readonly EntityKey StackTraceKey = new(Assembly, 0x04000001);
    private static readonly EntityKey RegisterKey = new(Assembly, 0x06000001);
    private static readonly EntityKey LoadTypesKey = new(Assembly, 0x06000002);
    private static readonly EntityKey LoadMethodsKey = new(Assembly, 0x06000003);

    [Fact]
    public void NeitherStackTracesNorNamesProvideNoRoots()
    {
        var provider = new StackTraceReachabilityRootProvider();

        var roots = provider.Provide(false, false, new TypeFinder(), new FieldRepository(), new MethodRepository());

        Assert.Same(NetWasm.Compiler.Analysis.ReachabilityRoots.Empty, roots);
    }

    [Fact]
    public void StackTracesKeepTheirTypesFieldAndTheMethodSymbolsLoader()
    {
        var provider = new StackTraceReachabilityRootProvider();

        var roots = provider.Provide(true, false, new TypeFinder(), new FieldRepository(), new MethodRepository());

        Assert.Equal([ExceptionKey, StringKey], roots.Types.ToArray());
        Assert.Equal([StackTraceKey], roots.Fields.ToArray());
        Assert.Equal([LoadMethodsKey], roots.Methods.ToArray());
    }

    // Issue 225: a program built with type names loads them at startup.
    [Fact]
    public void TypeNamesKeepOnlyTheTypeSymbolsLoader()
    {
        var provider = new StackTraceReachabilityRootProvider();

        var roots = provider.Provide(false, true, new TypeFinder(), new FieldRepository(), new MethodRepository());

        Assert.Empty(roots.Types);
        Assert.Empty(roots.Fields);
        Assert.Equal([LoadTypesKey], roots.Methods.ToArray());
    }

    [Fact]
    public void ProviderRejectsMissingCollaborators()
    {
        var provider = new StackTraceReachabilityRootProvider();

        Assert.Throws<ArgumentNullException>(() =>
            provider.Provide(false, false, null!, new FieldRepository(), new MethodRepository()));
        Assert.Throws<ArgumentNullException>(() =>
            provider.Provide(false, false, new TypeFinder(), null!, new MethodRepository()));
        Assert.Throws<ArgumentNullException>(() =>
            provider.Provide(false, false, new TypeFinder(), new FieldRepository(), null!));
    }

    private sealed class TypeFinder : ITypeFinder
    {
        public TypeDefinitionModel FindType(string fullName) => fullName switch
        {
            "System.Exception" => new(ExceptionKey, "System", "Exception", false, [StackTraceKey], []),
            "System.String" => new(StringKey, "System", "String", false, [], []),
            "System.Runtime.InteropServices.ProgramSidecars" =>
                new(SidecarsKey, "System.Runtime.InteropServices", "ProgramSidecars", false, [],
                    [RegisterKey, LoadTypesKey, LoadMethodsKey]),
            _ => throw new InvalidOperationException(fullName),
        };
    }

    private sealed class FieldRepository : IFieldRepository
    {
        public FieldDefinitionModel GetField(EntityKey key)
        {
            Assert.Equal(StackTraceKey, key);
            return new(
                StackTraceKey,
                ExceptionKey,
                "_stackTrace",
                CliTypeIdentity.Named(Assembly, "System", "String", false),
                false);
        }
    }

    private sealed class MethodRepository : IMethodRepository
    {
        public MethodDefinitionModel GetMethod(EntityKey key) => new(
            key, SidecarsKey, key == LoadTypesKey ? "LoadTypeSymbols" : key == LoadMethodsKey ? "LoadMethodSymbols" : "RegisterMethodSymbol", true,
            MethodSignatureModel.Create(CliValueKind.Void), 0);
    }
}
