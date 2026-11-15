// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Metadata.Tests;

public sealed class ReadonlyTypeFieldValueResolverIntegrationTests
{
    [Fact]
    public void FactoryRejectsAccessorExposedFieldWithoutRetainingOrReadingTheAccessorBody()
    {
        using var assets = TestAssets.Create();
        var path = assets.CompileSource("AccessorReadonlyTypeProof", """
            using System;
            using System.Runtime.CompilerServices;

            public class Known
            {
                private readonly Type _type = typeof(int);
                public Type Read() => _type;
            }
            public class Unrelated
            {
                private readonly Type _type = typeof(int);
                public Type Read() => _type;
            }
            public static class Accessors
            {
                [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "_type")]
                public static extern ref Type Address(Known owner);
            }
            """);
        using var lease = MetadataCompilationTestFactory.Load(path, [assets.CoreLib]);
        var snapshot = lease.Snapshot;
        var assembly = snapshot.Assemblies.Single(item => item.Identity == snapshot.EntryAssemblyIdentity);
        var resolver = Create(new ReadonlyTypeFieldValueResolverFactory(), snapshot);
        Assert.Null(resolver.Resolve(Field("Known")));
        Assert.Equal(CliTypeIdentity.FromStackKind(CliValueKind.I4), resolver.Resolve(Field("Unrelated")));

        FieldInstanceModel Field(string name)
        {
            var type = assembly.Types.Values.Single(type => type.Name == name);
            var field = assembly.Fields.Values.Single(field => field.DeclaringType == type.Key);
            return new(field, CliTypeIdentity.FromDefinition(type), field.SignatureType);
        }
    }

    [Fact]
    public void ProvesClosedGenericInitializersAndRejectsMutationOrAddressEscapes()
    {
        using var assets = TestAssets.Create();
        var path = assets.CompileSource("ReadonlyTypeProof", """
            using System;

            public class Known<T>
            {
                private readonly Type _type = typeof(T);
                public Known() { }
                public Known(int unused) { }
                public Type Read() => _type;
            }
            public class Fixed
            {
                private readonly Type _type = typeof(Known<int>);
                public Type Read() => _type;
            }
            public class Mutable
            {
                private Type _type = typeof(int);
                public Type Read() => _type;
            }
            public class Visible
            {
                public readonly Type _type = typeof(int);
            }
            public class Reassigned
            {
                private readonly Type _type = typeof(int);
                public Reassigned() { _type = typeof(string); }
                public Type Read() => _type;
            }
            public class BodyOnly
            {
                private readonly Type _type;
                public BodyOnly() { _type = typeof(int); }
                public Type Read() => _type;
            }
            public class AddressEscapes
            {
                private readonly Type _type = typeof(int);
                public ref readonly Type Read() => ref _type;
            }
            public struct Value
            {
                private readonly Type _type;
                public Value() { _type = typeof(int); }
                public Type Read() => _type;
            }
            """);
        using var lease = MetadataCompilationTestFactory.Load(path, [assets.CoreLib]);
        var snapshot = lease.Snapshot;
        var assembly = snapshot.Assemblies.Single(item => item.Identity == snapshot.EntryAssemblyIdentity);
        var factory = new ReadonlyTypeFieldValueResolverFactory();
        var resolver = Create(factory, snapshot);
        var integer = CliTypeIdentity.FromStackKind(CliValueKind.I4);
        var text = CliTypeIdentity.Primitive("string", CliValueKind.ManagedReference, false);

        Assert.Equal(integer, resolver.Resolve(Field("Known`1", integer)));
        Assert.Equal(text, resolver.Resolve(Field("Known`1", text)));
        Assert.Equal(integer, resolver.Resolve(Field("Known`1", integer)));
        var fixedValue = resolver.Resolve(Field("Fixed"));
        Assert.NotNull(fixedValue);
        Assert.Equal(CliTypeShape.GenericInstantiation, fixedValue.Shape);
        Assert.Equal("Known`1", fixedValue.ElementType!.FullName);
        Assert.Equal(integer, Assert.Single(fixedValue.TypeArguments));
        foreach (var name in new[] { "Mutable", "Visible", "Reassigned", "BodyOnly", "AddressEscapes", "Value" })
        {
            Assert.Null(resolver.Resolve(Field(name)));
        }

        FieldInstanceModel Field(string name, params CliTypeIdentity[] arguments)
        {
            var type = assembly.Types.Values.Single(type => type.Name == name);
            var field = assembly.Fields.Values.Single(field => field.DeclaringType == type.Key && field.Name == "_type");
            var identity = CliTypeIdentity.FromDefinition(type);
            if (arguments.Length != 0)
            {
                identity = CliTypeIdentity.GenericInstantiation(identity, [.. arguments]);
            }
            return new(field, identity, field.SignatureType);
        }
    }

    private static IReadonlyTypeFieldValueResolver Create<TFactory>(TFactory factory, MetadataCompilationSnapshot snapshot)
        where TFactory : IReadonlyTypeFieldValueResolverFactory => factory.Create(snapshot,
            MetadataCapabilityTestData.MethodInstances(snapshot),
            MetadataCapabilityTestData.TypeDefinitions(snapshot),
            MetadataCapabilityTestData.Types(snapshot),
            snapshot.Types.Single(type => type.FullName == "System.Type").Key);
}
