// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Analysis.AddressTakenValues;

internal interface IVectorLaneFields
{
    // The lane a field of the System.Numerics vector's own type is: X, Y, Z or W, a float within the vector.
    int? Lane(CilOperand field, CliTypeIdentity vector);
}

// System.Numerics' float vectors hold their lanes as fields, named by a field instance of the type or, the type
// being non-generic, by the field's own token.
internal sealed class VectorLaneFields(IFieldRepository fields, ITypeDefinitionResolver types) : IVectorLaneFields
{
    private readonly IFieldRepository _fields = fields ?? throw new ArgumentNullException(nameof(fields));
    private readonly ITypeDefinitionResolver _types = types ?? throw new ArgumentNullException(nameof(types));

    public int? Lane(CilOperand field, CliTypeIdentity vector)
    {
        ArgumentNullException.ThrowIfNull(field);
        ArgumentNullException.ThrowIfNull(vector);
        if (Lanes(vector) is not { } lanes) return null;
        var definition = field switch
        {
            CilOperand.FieldInstance { Value: var instance } when instance.DeclaringType.Equals(vector) => instance.Definition,
            CilOperand.Entity { Key: var key } when _types.ResolveTypeIdentity(vector).Fields.Contains(key) =>
                _fields.GetField(key),
            _ => null,
        };
        return definition is { IsStatic: false, FieldType: CliValueKind.F4 } &&
            definition.Name switch { "X" => 0, "Y" => 1, "Z" => 2, "W" => 3, _ => -1 } is var lane &&
            lane >= 0 && lane < lanes
                ? lane
                : null;
    }

    // How many lanes each System.Numerics float vector a v128 holds has.
    internal static int? Lanes(CliTypeIdentity vector) =>
        vector is { Shape: CliTypeShape.Named, StackKind: CliValueKind.V128 }
            ? vector.FullName switch
            {
                "System.Numerics.Vector2" => 2,
                "System.Numerics.Vector3" => 3,
                "System.Numerics.Vector4" or "System.Numerics.Quaternion" => 4,
                _ => null,
            }
            : null;
}
