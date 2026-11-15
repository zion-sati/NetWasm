// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Immutable;
using System.Reflection.Metadata;
using NetWasm.Compiler.Analysis.Attributes;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Metadata;

namespace NetWasm.Compiler.Tests;

public sealed class AttributeUsageReaderTests
{
    private static readonly CliTypeIdentity AttributeType = CliTypeIdentity.Named(new("Test"), "Test", "MarkerAttribute", false);
    private static readonly CliTypeIdentity UsageType = CliTypeIdentity.Named(new("Core"), "System", "AttributeUsageAttribute", false);
    private static readonly CliTypeIdentity Boolean = CliTypeIdentity.Primitive("bool", CliValueKind.I4);
    private static readonly CliTypeIdentity Integer = CliTypeIdentity.Primitive("i4", CliValueKind.I4);
    private static readonly CustomAttributeDescriptor Usage = new(new("Test"), 1, 2, UsageType, CliGenericContext.Empty);

    [Fact]
    public void NoDeclaredUsageUsesDefaultsWithoutReadingOtherAttributeTypesOrPayloads()
    {
        var fixture = new Fixture { Descriptors = [Usage with { AttributeType = AttributeType }] };
        Assert.Equal(new(true, false), fixture.Read());
        Assert.Equal([AttributeType], fixture.Reads);
        Assert.Equal(0, fixture.Decodes);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void DeclaredUsageReadsBothFlags(bool inherited, bool multiple)
    {
        var fixture = new Fixture
        {
            Value = new([new(Integer, 4)],
                [Named("Inherited", inherited), Named("AllowMultiple", multiple)]),
        };
        Assert.Equal(new(inherited, multiple), fixture.Read());
        Assert.Equal(1, fixture.Decodes);
    }

    [Fact]
    public void UnspecifiedUsagePropertiesKeepDefaults()
    {
        Assert.Equal(new(true, false), new Fixture().Read());
    }

    [Fact]
    public void DuplicateUsageFailsBeforeDecoding()
    {
        var fixture = new Fixture { Descriptors = [Usage, Usage] };
        AssertInvalid(fixture);
        Assert.Equal(0, fixture.Decodes);
    }

    [Fact]
    public void MalformedPayloadProducesACompilerDiagnostic()
    {
        AssertInvalid(new Fixture { Malformed = true });
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public void InvalidUsageValuesAreRejected(int shape)
    {
        var fixture = new Fixture
        {
            Value = shape switch
            {
                0 => new([], []),
                1 => new([new(Integer, "wrong")], []),
                2 => new([new(Integer, 4)], [new("Inherited", CustomAttributeNamedArgumentKind.Field, Boolean, true)]),
                3 => new([new(Integer, 4)], [new("Inherited", CustomAttributeNamedArgumentKind.Property, Boolean, 1)]),
                4 => new([new(Integer, 4)], [new("Inherited", CustomAttributeNamedArgumentKind.Property, Integer, true)]),
                _ => new([new(Integer, 4)], [Named("Unknown", true)]),
            },
        };
        AssertInvalid(fixture);
    }

    private static CustomAttributeNamedArgument<CliTypeIdentity> Named(string name, bool value) =>
        new(name, CustomAttributeNamedArgumentKind.Property, Boolean, value);

    private static void AssertInvalid(Fixture fixture) => Assert.Equal(DiagnosticCode.UnsupportedMetadata,
        Assert.Throws<CompilerException>(() => fixture.Read()).Diagnostic.Code);

    private sealed class Fixture : ICustomAttributeDescriptorReader, ICustomAttributeValueDecoder
    {
        internal ImmutableArray<CustomAttributeDescriptor> Descriptors { get; init; } = [Usage];
        internal CustomAttributeValue<CliTypeIdentity> Value { get; init; } = new([new(Integer, 4)], []);
        internal bool Malformed { get; init; }
        internal List<CliTypeIdentity> Reads { get; } = [];
        internal int Decodes { get; private set; }

        internal AttributeUsage Read() => ((IAttributeUsageReader)new AttributeUsageReader(this, this, UsageType))
            .Read(AttributeType);

        public ImmutableArray<CustomAttributeDescriptor> Read(CliTypeIdentity type)
        {
            Reads.Add(type);
            return Descriptors;
        }

        public CustomAttributeValue<CliTypeIdentity> Decode(CustomAttributeDescriptor attribute)
        {
            Assert.Equal(Usage, attribute);
            Decodes++;
            return Malformed ? throw new BadImageFormatException() : Value;
        }
    }
}
