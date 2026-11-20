// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Immutable;
using NetWasm.Compiler.Analysis.AddressTakenValues;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Tests;

// A System.Numerics vector's lane is its own instance float field X, Y, Z or W within its lane count, named by a field
// instance of the vector or by the field's token.
public sealed class VectorLaneFieldsTests
{
    private static readonly AssemblyIdentity Assembly = new("System.Numerics.Vectors");
    private static readonly CliTypeIdentity Vector2 = CliTypeIdentity.Named(Assembly, "System.Numerics", "Vector2", true);
    private static readonly CliTypeIdentity Vector4 = CliTypeIdentity.Named(Assembly, "System.Numerics", "Vector4", true);
    private static readonly CliTypeIdentity Quaternion = CliTypeIdentity.Named(Assembly, "System.Numerics", "Quaternion", true);
    private static readonly CliTypeIdentity Single = CliTypeIdentity.Primitive("f4", CliValueKind.F4);

    public static TheoryData<string, int?> Fields => new()
    {
        { "Vector4.X instance", 0 },
        { "Vector4.W instance", 3 },
        { "Quaternion.W instance", 3 },
        // Vector2 has no third lane.
        { "Vector2.Z instance", null },
        { "Vector4.Y token", 1 },
        { "foreign token", null },
        { "other type's field", null },
        { "no field", null },
        { "static", null },
        { "double", null },
        { "not a lane", null },
        // Plane fills a v128 but holds no lanes; a struct is no v128.
        { "not a vector", null },
        { "not simd", null },
    };

    [Theory]
    [MemberData(nameof(Fields))]
    public void ALaneIsAnInstanceFloatNamedForItsPosition(string field, int? lane)
    {
        var fixture = new Fixture();
        (CilOperand Operand, CliTypeIdentity Vector) chosen = field switch
        {
            "Vector4.X instance" => (Instance(Vector4, Field("X")), Vector4),
            "Vector4.W instance" => (Instance(Vector4, Field("W")), Vector4),
            "Quaternion.W instance" => (Instance(Quaternion, Field("W")), Quaternion),
            "Vector2.Z instance" => (Instance(Vector2, Field("Z")), Vector2),
            "Vector4.Y token" => (new CilOperand.Entity(fixture.Vector4Y), Vector4),
            "foreign token" => (new CilOperand.Entity(new EntityKey(Assembly, 0x04000099)), Vector4),
            "other type's field" => (Instance(Vector2, Field("X")), Vector4),
            "no field" => (new CilOperand.None(), Vector4),
            "static" => (Instance(Vector4, Field("X", isStatic: true)), Vector4),
            "double" => (Instance(Vector4, Field("X", CliValueKind.F8)), Vector4),
            "not a lane" => (Instance(Vector4, Field("V")), Vector4),
            "not a vector" => (Instance(Vector4, Field("X")), CliTypeIdentity.Named(Assembly, "System.Numerics", "Plane", true)),
            _ => (Instance(Vector4, Field("X")), CliTypeIdentity.Named(Assembly, "Fixture", "Point", true)),
        };

        Assert.Equal(lane, new VectorLaneFields(fixture, fixture).Lane(chosen.Operand, chosen.Vector));
    }

    [Fact]
    public void RejectsMissingInputs()
    {
        var fixture = new Fixture();
        Assert.Throws<ArgumentNullException>(() => new VectorLaneFields(null!, fixture));
        Assert.Throws<ArgumentNullException>(() => new VectorLaneFields(fixture, null!));
        Assert.Throws<ArgumentNullException>(() => new VectorLaneFields(fixture, fixture).Lane(null!, Vector4));
        Assert.Throws<ArgumentNullException>(() => new VectorLaneFields(fixture, fixture).Lane(new CilOperand.None(), null!));
    }

    private static FieldDefinitionModel Field(string name, CliValueKind kind = CliValueKind.F4, bool isStatic = false) =>
        new(new EntityKey(Assembly, 0x04000001), new EntityKey(Assembly, 0x02000001), name, kind, isStatic) { SignatureType = Single };

    private static CilOperand.FieldInstance Instance(CliTypeIdentity vector, FieldDefinitionModel field) =>
        new(new FieldInstanceModel(field, vector, Single));

    // Vector4 declares Y by token.
    private sealed class Fixture : IFieldRepository, ITypeDefinitionResolver
    {
        public EntityKey Vector4Y { get; } = new(Assembly, 0x04000010);

        public FieldDefinitionModel GetField(EntityKey key) =>
            new(key, new EntityKey(Assembly, 0x02000002), "Y", CliValueKind.F4, false) { SignatureType = Single };

        public TypeDefinitionModel ResolveTypeIdentity(CliTypeIdentity identity) =>
            new(new EntityKey(Assembly, 0x02000002), "System.Numerics", "Vector4", true, ImmutableArray.Create(Vector4Y), []);
    }
}
