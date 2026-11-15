// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Metadata.Tests.UnsafeAccessors;

public sealed class UnsafeAccessorFactoryIntegrationTests
{
    [Fact]
    public void BodyReaderFactoryComposesEveryAccessorKindAndRetainsPhysicalDecoding()
    {
        using var assets = TestAssets.Create();
        var path = assets.CompileSource("AccessorComposition", """
            using System.Runtime.CompilerServices;
            public sealed class Owner
            {
                private int field;
                private static int shared = 7;
                private Owner(int value) => field = value;
                private int Read() => field;
                private static int ReadShared() => shared;
            }
            public static class Accessors
            {
                [UnsafeAccessor(UnsafeAccessorKind.Constructor)] public static extern Owner Create(int value);
                [UnsafeAccessor(UnsafeAccessorKind.Method, Name = "Read")] public static extern int Read(Owner owner);
                [UnsafeAccessor(UnsafeAccessorKind.StaticMethod, Name = "ReadShared")] public static extern int ReadShared(Owner owner);
                [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "field")] public static extern ref int Field(Owner owner);
                [UnsafeAccessor(UnsafeAccessorKind.StaticField, Name = "shared")] public static extern ref int Shared(Owner owner);
                [UnsafeAccessor(UnsafeAccessorKind.Method, Name = "Absent")] public static extern void Missing(Owner owner);
                [UnsafeAccessor((UnsafeAccessorKind)99)] public static int Physical(int value) => value;
            }
            """);
        using var lease = MetadataCompilationTestFactory.Load(path, [assets.CoreLib]);
        var snapshot = lease.Snapshot;
        var assembly = snapshot.Assemblies.Single(assembly => assembly.Identity == snapshot.EntryAssemblyIdentity);
        var owner = assembly.Types.Values.Single(type => type.Name == "Accessors");
        var methods = owner.Methods.Select(key => assembly.Methods[key.MetadataToken]).ToDictionary(method => method.Name);
        var bodies = MetadataCapabilityTestData.MethodBodies(snapshot);
        var expected = new Dictionary<string, CilOperation>
        {
            ["Create"] = CilOperation.NewObject,
            ["Read"] = CilOperation.CallVirtual,
            ["ReadShared"] = CilOperation.Call,
            ["Field"] = CilOperation.LoadFieldAddress,
            ["Shared"] = CilOperation.LoadStaticFieldAddress,
        };
        foreach (var (name, operation) in expected)
        {
            var method = methods[name];
            var body = bodies.ReadMethodBody(method);
            Assert.Same(method, body.Method);
            Assert.NotNull(body.MethodInstance);
            Assert.Equal(operation, body.Instructions[^2].Operation);
            Assert.Equal(CilOperation.Return, body.Instructions[^1].Operation);
            Assert.All(body.Instructions, instruction => Assert.Null(instruction.OriginalOffset));
        }

        var missing = bodies.ReadMethodBody(methods["Missing"]);
        Assert.Equal(CilOperation.Throw, missing.Instructions[^1].Operation);
        var exception = Assert.IsType<CilOperand.MethodInstance>(missing.Instructions[0].Operand);
        Assert.Equal("System.MissingMethodException", exception.Value.DeclaringType.FullName);
        Assert.Null(methods["Physical"].UnsafeAccessor);
        Assert.True(methods["Physical"].HasBody);
        Assert.Contains(bodies.ReadMethodBody(methods["Physical"]).Instructions,
            instruction => instruction.Operation == CilOperation.LoadArgument);
    }
}
