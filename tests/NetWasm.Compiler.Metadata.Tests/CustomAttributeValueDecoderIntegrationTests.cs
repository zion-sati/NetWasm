// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Metadata.Tests;

public sealed class CustomAttributeValueDecoderIntegrationTests
{
    [Fact]
    public void DecodesEnumArgumentsNestedInClosedGenericTypes()
    {
        using var assets = TestAssets.Create();
        var path = assets.CompileSource("NestedEnumAttribute", """
            using System;
            namespace NestedEnumAttribute;

            public class Outer<T>
            {
                public enum Code : ushort { Answer = 42 }
            }
            public sealed class MarkerAttribute(Outer<int>.Code value) : Attribute
            {
                public Outer<int>.Code Value { get; } = value;
            }
            [Marker(Outer<int>.Code.Answer)] public class Target;
            """);
        using var lease = MetadataCompilationTestFactory.Load(path, [assets.CoreLib]);
        var snapshot = lease.Snapshot;
        var definitions = MetadataCapabilityTestData.TypeDefinitions(snapshot);
        var identities = MetadataCapabilityTestData.TypeIdentities(snapshot);
        var target = snapshot.Types.Single(type => type.FullName == "NestedEnumAttribute.Target");
        var descriptors = new CustomAttributeDescriptorReaderFactory().Create(snapshot, definitions, identities);
        var descriptor = Assert.Single(descriptors.Read(CliTypeIdentity.FromDefinition(target)));
        var decoder = new CustomAttributeValueDecoderFactory().Create(snapshot, definitions, identities,
            MetadataCapabilityTestData.MethodInstances(snapshot),
            snapshot.Types.Single(type => type.FullName == "System.Type").Key);

        var value = decoder.Decode(descriptor);

        var argument = Assert.Single(value.FixedArguments);
        Assert.Equal((ushort)42, Assert.IsType<ushort>(argument.Value));
        Assert.Equal(CliTypeShape.GenericInstantiation, argument.Type.Shape);
        Assert.Equal("NestedEnumAttribute.Outer`1+Code", argument.Type.ElementType!.FullName);
        Assert.Equal("primitive:i4", Assert.Single(argument.Type.TypeArguments).CanonicalName);
        Assert.Empty(value.NamedArguments);
    }
}
