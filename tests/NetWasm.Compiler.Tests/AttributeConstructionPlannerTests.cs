// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Immutable;
using System.Reflection.Metadata;
using NetWasm.Compiler.Analysis;
using NetWasm.Compiler.Analysis.Attributes;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Core.IntermediateRepresentation.Attributes;
using NetWasm.Compiler.Metadata;

namespace NetWasm.Compiler.Tests;

public sealed class AttributeConstructionPlannerTests
{
    private static readonly AssemblyIdentity Assembly = new("Construction");
    private static readonly CliTypeIdentity AttributeType = CliTypeIdentity.Named(Assembly, "Test", "Marker", false);
    private static readonly CliTypeIdentity BaseType = CliTypeIdentity.Named(Assembly, "Test", "Base", false);
    private static readonly CliTypeIdentity Integer = CliTypeIdentity.FromStackKind(CliValueKind.I4);
    private static readonly CustomAttributeDescriptor Attribute = new(Assembly, 1, 2, AttributeType, new([Integer], []));

    [Fact]
    public void PreservesConstructorAndOrderedNamedAssignmentsThroughTheirClosedMembers()
    {
        var fixture = new Fixture
        {
            Value = new([new(Integer, 11)],
                [new("Field", CustomAttributeNamedArgumentKind.Field, Integer, 13),
                 new("Property", CustomAttributeNamedArgumentKind.Property, Integer, 17)]),
        };
        var field = new FieldInstanceModel(new(new(Assembly, 0x04000001), new(Assembly, 0x02000002),
            "Field", Integer, false), BaseType, Integer);
        var setter = Method("set_Property", [Integer]);
        fixture.Members[(BaseType, "Field")] = new(field, null);
        fixture.Members[(AttributeType, "Property")] = new(null, setter);

        var result = fixture.Plan(Attribute);

        Assert.Same(AttributeType, result.AttributeType);
        Assert.Same(fixture.Constructor, result.Constructor);
        Assert.Same(fixture.Plans[0], Assert.Single(result.ConstructorArguments));
        Assert.Collection(result.NamedArguments,
            named =>
            {
                Assert.Equal("Field", named.Name);
                Assert.Same(field, named.Field);
                Assert.Null(named.Setter);
                Assert.Same(fixture.Plans[1], named.Value);
            },
            named =>
            {
                Assert.Equal("Property", named.Name);
                Assert.Null(named.Field);
                Assert.Same(setter, named.Setter);
                Assert.Same(fixture.Plans[2], named.Value);
            });
        Assert.Equal([11, 13, 17], fixture.Arguments.Select(argument => argument.Value));
        Assert.All(fixture.Destinations, type => Assert.Same(Integer, type));
        Assert.Equal([(AttributeType, "Field"), (BaseType, "Field"), (AttributeType, "Property")], fixture.MemberReads);
        Assert.Equal([AttributeType], fixture.BaseReads);
    }

    [Fact]
    public void EmptyPayloadPreservesAnEmptyConstructionPlan()
    {
        var fixture = new Fixture { Constructor = Method(".ctor", []), Value = new([], []) };
        var result = fixture.Plan(Attribute);
        Assert.Same(fixture.Constructor, result.Constructor);
        Assert.Empty(result.ConstructorArguments);
        Assert.Empty(result.NamedArguments);
        Assert.Empty(fixture.Arguments);
        Assert.Empty(fixture.MemberReads);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void RejectsMalformedArityUnnamedAndUnboundPayloads(int shape)
    {
        var fixture = new Fixture
        {
            Malformed = shape == 0,
            Value = shape switch
            {
                1 => new([], []),
                2 => new([new(Integer, 11)], [new(null, CustomAttributeNamedArgumentKind.Field, Integer, 13)]),
                3 => new([new(Integer, 11)], [new("Missing", CustomAttributeNamedArgumentKind.Property, Integer, 13)]),
                _ => new([new(Integer, 11)], []),
            },
        };
        var exception = Assert.Throws<CompilerException>(() => fixture.Plan(Attribute));
        Assert.Equal(DiagnosticCode.UnsupportedMetadata, exception.Diagnostic.Code);
        Assert.Equal(shape >= 2 ? 1 : 0, fixture.Arguments.Count);
        if (shape == 3)
        {
            Assert.Equal([(AttributeType, "Missing"), (BaseType, "Missing")], fixture.MemberReads);
            Assert.Equal([AttributeType, BaseType], fixture.BaseReads);
        }
        else Assert.Empty(fixture.MemberReads);
    }

    [Fact]
    public void MissingDescriptorFailsBeforeResolvingAnyMetadata()
    {
        var fixture = new Fixture();
        Assert.Throws<ArgumentNullException>(() => fixture.Plan(null!));
        Assert.Equal(0, fixture.MethodReads);
        Assert.Empty(fixture.Arguments);
    }

    private static MethodInstanceModel Method(string name, ImmutableArray<CliTypeIdentity> parameters)
    {
        var signature = new MethodSignatureModel(CliTypeIdentity.FromStackKind(CliValueKind.Void), parameters);
        return new(new(new(Assembly, 2), new(Assembly, 0x02000001), name, false, signature, 1),
            AttributeType, [], signature);
    }

    private sealed class Fixture : IMethodInstanceResolver, ICustomAttributeValueDecoder,
        ICustomAttributeMemberResolver, IAttributeArgumentDecoder, IBaseTypeResolver
    {
        internal MethodInstanceModel Constructor { get; init; } = Method(".ctor", [Integer]);
        internal CustomAttributeValue<CliTypeIdentity> Value { get; init; } = new([new(Integer, 11)], []);
        internal bool Malformed { get; init; }
        internal int MethodReads { get; private set; }
        internal Dictionary<(CliTypeIdentity Type, string Name), CustomAttributeMemberBinding> Members { get; } = [];
        internal List<(CliTypeIdentity Type, string Name)> MemberReads { get; } = [];
        internal List<CliTypeIdentity> BaseReads { get; } = [];
        internal List<CustomAttributeTypedArgument<CliTypeIdentity>> Arguments { get; } = [];
        internal List<CliTypeIdentity> Destinations { get; } = [];
        internal List<AttributeArgumentPlan> Plans { get; } = [];

        internal AttributeConstructionPlan Plan(CustomAttributeDescriptor attribute) =>
            ((IAttributeConstructionPlanner)new AttributeConstructionPlanner(this, this, this, this, this)).Plan(attribute);

        public MethodInstanceModel ResolveMethodInstance(AssemblyIdentity source, int metadataToken,
            string methodDisplayName, int ilOffset, CliGenericContext? genericContext = null)
        {
            Assert.Equal(Attribute.Source, source);
            Assert.Equal(Attribute.ConstructorToken, metadataToken);
            Assert.Equal(Attribute.GenericContext, genericContext);
            MethodReads++;
            return Constructor;
        }

        public CustomAttributeValue<CliTypeIdentity> Decode(CustomAttributeDescriptor attribute)
        {
            Assert.Same(Attribute, attribute);
            return Malformed ? throw new BadImageFormatException() : Value;
        }

        public CustomAttributeMemberBinding? Resolve(CliTypeIdentity declaringType, string name, CustomAttributeNamedArgumentKind kind)
        {
            Assert.Equal(name == "Field" ? CustomAttributeNamedArgumentKind.Field : CustomAttributeNamedArgumentKind.Property, kind);
            MemberReads.Add((declaringType, name));
            return Members.GetValueOrDefault((declaringType, name));
        }

        public AttributeArgumentPlan Decode(CustomAttributeTypedArgument<CliTypeIdentity> argument, CliTypeIdentity destination)
        {
            Arguments.Add(argument);
            Destinations.Add(destination);
            var plan = new AttributeArgumentPlan(destination, AttributeArgumentKind.SignedInteger) { SignedValue = Plans.Count };
            Plans.Add(plan);
            return plan;
        }

        public CliTypeIdentity? Resolve(CliTypeIdentity type)
        {
            BaseReads.Add(type);
            return type.Equals(AttributeType) ? BaseType : null;
        }
    }
}
