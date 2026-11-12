using System.Collections.Immutable;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Metadata;
using NetWasm.Compiler.Symbols;

namespace NetWasm.Compiler.Tests.Symbols;

public sealed class RuntimeTypeSymbolTableBuilderTests
{
    private static readonly AssemblyIdentity Assembly = new("Symbols");
    private static readonly TypeDefinitionModel Object = Definition(1, "App", "Root");
    private static readonly TypeDefinitionModel Exception = Definition(2, "System", "Exception");
    private static readonly TypeDefinitionModel Custom = Definition(3, "App", "CustomException");
    private static readonly TypeDefinitionModel Point = Definition(4, "App", "Point");
    private static readonly TypeDefinitionModel Box = Definition(5, "App", "Box`1") with
    {
        GenericArity = 1,
        GenericParameterNames = ["T"],
    };

    [Fact]
    public void NamesEveryDescriptorAndMarksTheExceptionTypes()
    {
        var boxOfPoint = CliTypeIdentity.GenericInstantiation(Identity(Box), [Identity(Point)]);
        var parameter = CliTypeIdentity.ScopedGenericParameter(Identity(Box).CanonicalName, method: false, 0);
        var descriptors = new Descriptors(
            [Type(Object, 1, 0), Type(Exception, 2, 1), Type(Custom, 3, 2)],
            [new(boxOfPoint, 6, 1, 0, 0, 0, null), new(CliTypeIdentity.SzArray(Identity(Custom)), 7, 2, 0, 0, 0, null)],
            [new(Identity(Point), 4, 8, 0, 0, 0)],
            [new MetadataTypeDescriptorLayout(parameter, 8) { GenericParameterName = "T" }]);

        var table = Build(descriptors);

        Assert.Equal([1, 2, 3, 4, 6, 7, 8], table.Symbols.Select(symbol => symbol.TypeId));
        Assert.Equal("App.Box`1[App.Point]", table.Symbols.Single(symbol => symbol.TypeId == 6).DisplayName);
        Assert.Equal("T", table.Symbols.Single(symbol => symbol.TypeId == 8).Name);
        // A constructed type whose base chain reaches System.Exception is one too (the array's base here).
        Assert.Equal([2, 3, 7], table.ExceptionTypeIds.Order());
        Assert.Equal(32, table.Digest.Length);
    }

    [Fact]
    public void AProgramWithoutSystemExceptionHasNoExceptionTypes() =>
        Assert.Empty(Build(new Descriptors([Type(Object, 1, 0)], [], [], [])).ExceptionTypeIds);

    [Fact]
    public void ABaseTypeCycleOrAnUnknownBaseEndsTheSearch()
    {
        var table = Build(new Descriptors(
            [Type(Exception, 2, 0), Type(Object, 1, 4), Type(Point, 4, 1), Type(Custom, 3, 9)], [], [], []));

        Assert.Equal([2], table.ExceptionTypeIds);
    }

    [Fact]
    public void TheDigestFollowsTheNames()
    {
        var first = Build(new Descriptors([Type(Object, 1, 0)], [], [], []));
        var same = Build(new Descriptors([Type(Object, 1, 0)], [], [], []));
        var renamed = Build(new Descriptors([Type(Point, 1, 0)], [], [], []));
        var global = Build(new Descriptors([Type(Definition(6, "", "Root"), 1, 0)], [], [], []));

        Assert.Equal(first.Digest, same.Digest);
        Assert.NotEqual(first.Digest, renamed.Digest);
        Assert.NotEqual(first.Digest, global.Digest);
    }

    [Fact]
    public void RejectsInvalidAndConflictingTypeIds()
    {
        Assert.Equal(DiagnosticCode.RuntimeContract, Assert.Throws<CompilerException>(() =>
            Build(new Descriptors([Type(Object, 0, 0)], [], [], []))).Diagnostic.Code);
        Assert.Equal(DiagnosticCode.RuntimeContract, Assert.Throws<CompilerException>(() =>
            Build(new Descriptors([Type(Object, 1, 0)], [], [new(Identity(Point), 1, 8, 0, 0, 0)], []))).Diagnostic.Code);
        // The same type under the same ID in two descriptor kinds is one entry.
        Assert.Single(Build(new Descriptors([Type(Point, 4, 0)], [], [new(Identity(Point), 4, 8, 0, 0, 0)], [])).Symbols);
    }

    [Fact]
    public void RejectsMissingCollaborators()
    {
        var builder = new RuntimeTypeSymbolTableBuilder();
        var descriptors = new Descriptors([], [], [], []);

        Assert.Throws<ArgumentNullException>(() => builder.Build(null!, new Identities(), new Definitions()));
        Assert.Throws<ArgumentNullException>(() => builder.Build(descriptors, null!, new Definitions()));
        Assert.Throws<ArgumentNullException>(() => builder.Build(descriptors, new Identities(), null!));
    }

    private static RuntimeTypeSymbolTable Build(ITypeDescriptorSource descriptors) =>
        new RuntimeTypeSymbolTableBuilder().Build(descriptors, new Identities(), new Definitions());

    private static TypeDescriptorLayout Type(TypeDefinitionModel definition, int typeId, int baseTypeId) =>
        new(definition.Key, typeId, baseTypeId, 8, 0, 0, null);

    private static TypeDefinitionModel Definition(int token, string @namespace, string name) =>
        new(new EntityKey(Assembly, token), @namespace, name, IsValueType: false, [], []);

    private static CliTypeIdentity Identity(TypeDefinitionModel definition) =>
        CliTypeIdentity.Named(Assembly, definition.Namespace, definition.Name, isValueType: false);

    private sealed class Descriptors(
        ImmutableArray<TypeDescriptorLayout> types,
        ImmutableArray<ConstructedTypeDescriptorLayout> constructed,
        ImmutableArray<ValueTypeDescriptorLayout> values,
        ImmutableArray<MetadataTypeDescriptorLayout> metadata) : ITypeDescriptorSource
    {
        public ImmutableArray<TypeDescriptorLayout> TypeDescriptors => types;
        public ImmutableArray<ConstructedTypeDescriptorLayout> ConstructedTypeDescriptors => constructed;
        public ImmutableArray<ValueTypeDescriptorLayout> ValueTypeDescriptors => values;
        public ImmutableArray<MetadataTypeDescriptorLayout> MetadataTypeDescriptors => metadata;
    }

    // System.Object would be the primitive object identity, so the fixtures root at App.Root.
    private sealed class Identities : ITypeIdentityResolver
    {
        public CliTypeIdentity GetTypeIdentity(EntityKey type) =>
            Identity(new[] { Object, Exception, Custom, Point, Box, Definition(6, "", "Root") }
                .First(definition => definition.Key == type));
    }

    private sealed class Definitions : ITypeDefinitionResolver
    {
        public TypeDefinitionModel ResolveTypeIdentity(CliTypeIdentity identity) =>
            new[] { Object, Exception, Custom, Point, Box, Definition(6, "", "Root") }
                .First(definition => definition.FullName == (identity.FullName ?? identity.ElementType?.FullName));
    }
}
