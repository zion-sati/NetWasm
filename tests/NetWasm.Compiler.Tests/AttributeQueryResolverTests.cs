// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Immutable;
using NetWasm.Compiler.Analysis;
using NetWasm.Compiler.Analysis.Attributes;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Tests;

public sealed class AttributeQueryResolverTests
{
    private static readonly AssemblyIdentity Assembly = new("Queries");
    private static readonly CliTypeIdentity Target = CliTypeIdentity.Named(Assembly, "Example", "Target", false);
    private static readonly CliTypeIdentity Filter = CliTypeIdentity.Named(Assembly, "Example", "MarkerAttribute", false);
    private static readonly CliTypeIdentity Attribute = CliTypeIdentity.Named(new("Core"), "System", "Attribute", false);
    private static readonly AttributeQueryCall ExplicitCall = new(AttributeQueryOperation.GetMany, 3, 1, null, 2);

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, true)]
    [InlineData(2, true)]
    public void ResolvesExplicitFiltersAndConstantInheritance(int inherit, bool expected)
    {
        var resolver = Create();

        var result = Resolve(resolver, ExplicitCall,
            [default, Type(Target), Type(Filter), new(AttributeValueKind.Int32, Integer: inherit)]);

        Assert.NotNull(result);
        Assert.Equal(Target, result.Target);
        Assert.Equal(Filter, result.Filter);
        Assert.Equal(expected, result.InheritConstant);
        Assert.Same(ExplicitCall, result.Call);
    }

    [Fact]
    public void RuntimeInheritanceDoesNotWeakenTheTypeBounds()
    {
        var result = Resolve(Create(), ExplicitCall, [Type(Target), Type(Filter), default]);

        Assert.NotNull(result);
        Assert.Equal(Target, result.Target);
        Assert.Equal(Filter, result.Filter);
        Assert.Null(result.InheritConstant);
    }

    [Fact]
    public void GenericUserDefinedFilterAndDefaultInheritanceAreSupported()
    {
        var result = Resolve(Create(), new(AttributeQueryOperation.GetOne, 1, null, Filter, null), [Type(Target)]);

        Assert.NotNull(result);
        Assert.Equal(Filter, result.Filter);
        Assert.True(result.InheritConstant);
    }

    [Fact]
    public void ClosedGenericTargetAndAttributePreserveTheirArguments()
    {
        var target = CliTypeIdentity.GenericInstantiation(Target, [Filter]);
        var filter = CliTypeIdentity.GenericInstantiation(Filter, [Target]);
        var result = Resolve(Create(new Definitions(1)),
            new(AttributeQueryOperation.GetOne, 1, null, filter, null), [Type(target)]);

        Assert.NotNull(result);
        Assert.Equal(target, result.Target);
        Assert.Equal(filter, result.Filter);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void UnknownNullOrTokenTargetsRemainUnresolvedBeforeMetadataAccess(int kind)
    {
        var definitions = new Definitions();
        var proof = kind switch
        {
            0 => default,
            1 => new AttributeValueProof(AttributeValueKind.Null),
            2 => new AttributeValueProof(AttributeValueKind.TypeToken, Target),
            _ => new AttributeValueProof(AttributeValueKind.Int32, Integer: 7),
        };

        Assert.Null(Resolve(Create(definitions), ExplicitCall, [proof, Type(Filter), default]));

        Assert.Empty(definitions.Requests);
    }

    [Fact]
    public void RuntimeFilterRemainsUnresolvedBeforeMetadataAccess()
    {
        var definitions = new Definitions();

        Assert.Null(Resolve(Create(definitions), ExplicitCall, [Type(Target), default, default]));

        Assert.Empty(definitions.Requests);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CatchAllAndNonAttributeFiltersRemainUnresolved(bool catchAll)
    {
        var relationships = new Relationships(false);
        var filter = catchAll ? Attribute : Filter;

        Assert.Null(Resolve(new AttributeQueryResolver(new Definitions(), relationships, Attribute, []),
            ExplicitCall, [Type(Target), Type(filter), default]));

        Assert.Equal(catchAll ? 0 : 1, relationships.Calls);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void NonDeclarationAndOpenGenericTargetsRemainUnresolved(int shape)
    {
        var type = shape switch
        {
            0 => CliTypeIdentity.SzArray(Target),
            1 => CliTypeIdentity.UnmanagedPointer(Target),
            2 => CliTypeIdentity.ManagedByReference(Target),
            3 => CliTypeIdentity.GenericParameter(false, 0),
            _ => CliTypeIdentity.GenericInstantiation(Target, [CliTypeIdentity.GenericParameter(false, 0)]),
        };
        var definitions = new Definitions();

        Assert.Null(Resolve(Create(definitions), ExplicitCall, [Type(type), Type(Filter), default]));

        Assert.Empty(definitions.Requests);
    }

    [Fact]
    public void UninstantiatedNamedGenericDefinitionRemainsUnresolved()
    {
        Assert.Null(Resolve(Create(new Definitions(1)), ExplicitCall,
            [Type(Target), Type(Filter), default]));
    }

    [Fact]
    public void InvalidAnalysisInputsAreRejected()
    {
        var resolver = Create();
        Assert.Throws<ArgumentNullException>(() => Resolve(resolver, null!, []));
        Assert.Throws<ArgumentException>(() => Resolve(resolver, ExplicitCall, default));
        Assert.Throws<ArgumentException>(() => Resolve(resolver, ExplicitCall, []));
    }

    private static AttributeValueProof Type(CliTypeIdentity type) => new(AttributeValueKind.Type, type);

    [Fact]
    public void SynthesizedAttributeFiltersRemainUnresolvedByBoundIdentity()
    {
        var resolver = new AttributeQueryResolver(new Definitions(), new Relationships(true), Attribute, [Filter]);
        Assert.Null(Resolve(resolver, ExplicitCall, [Type(Target), Type(Filter), default]));
        var userType = CliTypeIdentity.Named(new("User"), "Example", "MarkerAttribute", false);
        Assert.Equal(userType, Resolve(resolver, ExplicitCall, [Type(Target), Type(userType), default])!.Filter);
    }

    [Fact]
    public void TypeProofWithoutIdentityRemainsUnresolved()
    {
        var definitions = new Definitions();
        Assert.Null(Resolve(Create(definitions), ExplicitCall,
            [new(AttributeValueKind.Type), Type(Filter), default]));
        Assert.Empty(definitions.Requests);
    }

    [Fact]
    public void NonDeclarationFilterRemainsUnresolvedAfterTargetResolution()
    {
        var definitions = new Definitions();
        Assert.Null(Resolve(Create(definitions), ExplicitCall,
            [Type(Target), Type(CliTypeIdentity.SzArray(Filter)), default]));
        Assert.Equal([Target], definitions.Requests);
    }

    // An assembly the compile knows is a target whose own attributes a sealed, top-level filter finds by name; another
    // filter keeps the runtime query.
    [Theory]
    [InlineData(true, false, true)]
    [InlineData(false, false, false)]
    [InlineData(true, true, false)]
    public void AssemblyTargetsResolveForSealedTopLevelFilters(bool isSealed, bool nested, bool resolves)
    {
        var assembly = new AttributeValueProof(AttributeValueKind.Assembly, Assembly: Assembly);

        var result = Resolve(Create(new Definitions(isSealed: isSealed, nested: nested)), ExplicitCall, [assembly, Type(Filter), default]);

        Assert.Equal(resolves, result is not null);
        if (result is not null)
        {
            Assert.Null(result.Target);
            Assert.Equal(Assembly, result.TargetAssembly);
            Assert.Equal(Filter, result.Filter);
        }
        Assert.True(assembly.Condition);
        var generic = CliTypeIdentity.GenericInstantiation(Filter, [Target]);
        Assert.Null(Resolve(Create(new Definitions(arity: 1, isSealed: true)), ExplicitCall, [assembly, Type(generic), default]));
    }

    private static AttributeQueryResolver Create(Definitions? definitions = null) =>
        new(definitions ?? new Definitions(), new Relationships(true), Attribute, []);

    private static ResolvedAttributeQuery? Resolve<TResolver>(TResolver resolver,
        AttributeQueryCall call, ImmutableArray<AttributeValueProof> stack)
        where TResolver : IAttributeQueryResolver => resolver.Resolve(call, stack);

    // typeof(TagAttribute<>) is the generic definition itself: it resolves, and its matches come back as Attribute[];
    // a closed filter's come back as an array of the filter.
    [Fact]
    public void AnOpenGenericFilterResolvesWithAnAttributeArray()
    {
        var open = CliTypeIdentity.Named(Assembly, "Example", "TagAttribute`1", false);
        var resolver = new AttributeQueryResolver(new Arities(open), new Relationships(true), Attribute, []);
        ImmutableArray<AttributeValueProof> stack = [default, Type(Target), Type(open), new(AttributeValueKind.Int32, Integer: 1)];

        var result = Resolve(resolver, ExplicitCall, stack);

        Assert.NotNull(result);
        Assert.Equal(open, result.Filter);
        Assert.Equal(CliTypeIdentity.SzArray(Attribute), result.ResultArrayType);
        var closed = Resolve(Create(), ExplicitCall, stack.SetItem(2, Type(Filter)));
        Assert.Equal(CliTypeIdentity.SzArray(Filter), closed!.ResultArrayType);
        // An open target stays a run-time query.
        Assert.Null(Resolve(resolver, ExplicitCall, stack.SetItem(1, Type(open))));
    }

    private sealed class Arities(CliTypeIdentity open) : ITypeDefinitionResolver
    {
        public TypeDefinitionModel ResolveTypeIdentity(CliTypeIdentity identity) =>
            new(new(Assembly, 1), "Example", "Type", false, [], []) { GenericArity = identity.Equals(open) ? 1 : 0 };
    }

    private sealed class Definitions(int arity = 0, bool isSealed = false, bool nested = false) : ITypeDefinitionResolver
    {
        public List<CliTypeIdentity> Requests { get; } = [];

        public TypeDefinitionModel ResolveTypeIdentity(CliTypeIdentity identity)
        {
            Requests.Add(identity);
            return new(new(Assembly, 1), "Example", "Type", false, [], [])
            {
                GenericArity = arity,
                IsSealed = isSealed,
                DeclaringType = nested ? new EntityKey(Assembly, 2) : null,
            };
        }
    }

    private sealed class Relationships(bool compatible) : ITypeRelationshipClassifier
    {
        public int Calls { get; private set; }

        public TypeRelationship Classify(CliTypeIdentity candidate, CliTypeIdentity target)
        {
            Calls++;
            return new(compatible ? TypeRelationshipCharacteristics.Hierarchy : TypeRelationshipCharacteristics.None);
        }
    }
}
