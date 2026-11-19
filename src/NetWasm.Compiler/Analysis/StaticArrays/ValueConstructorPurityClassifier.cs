// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using System.Linq;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Metadata;

namespace NetWasm.Compiler.Analysis.StaticArrays;

internal sealed class ValueConstructorPurityClassifier(
    ICalleeMethodBodyReader bodies,
    IFieldRepository fields,
    IRuntimeIntrinsicRegistry intrinsics,
    IValueTypeInitializationClassifier initialization) : IValueConstructorPurityClassifier
{
    private readonly ICalleeMethodBodyReader _bodies = bodies ?? throw new ArgumentNullException(nameof(bodies));
    private readonly IFieldRepository _fields = fields ?? throw new ArgumentNullException(nameof(fields));
    private readonly IRuntimeIntrinsicRegistry _intrinsics = intrinsics ?? throw new ArgumentNullException(nameof(intrinsics));
    private readonly IValueTypeInitializationClassifier _initialization = initialization ??
        throw new ArgumentNullException(nameof(initialization));

    public bool IsPure(MethodInstanceModel method)
    {
        ArgumentNullException.ThrowIfNull(method);
        if (!method.DeclaringType.IsValueType || method.Definition.IsStatic || method.Definition.Name != ".ctor" ||
            method.Definition.IsNoInlining || !method.Definition.HasManagedBody || method.Signature.ReturnType != CliValueKind.Void ||
            method.Definition.JSImport is not null || method.Definition.WitImport is not null || method.Definition.NativeImport is not null ||
            _intrinsics.TryGetIntrinsic(method.Definition.Key, out _) ||
            !_initialization.ConstructsWithoutInitialization(method.DeclaringType)) return false;
        if (!_bodies.TryReadMethodBody(method, out var body) || !body.ExceptionRegions.IsEmpty) return false;
        var operations = body.Instructions.Where(instruction => instruction.Operation != CilOperation.Nop).ToArray();
        if (operations.Length == 0 || operations[^1].Operation != CilOperation.Return || (operations.Length - 1) % 3 != 0) return false;
        for (var index = 0; index < operations.Length - 1; index += 3)
        {
            if (operations[index] is not { Operation: CilOperation.LoadArgument, Operand: CilOperand.Index { Value: 0 } } ||
                operations[index + 1] is not { Operation: CilOperation.LoadArgument, Operand: CilOperand.Index parameter } ||
                parameter.Value <= 0 || parameter.Value > method.Signature.ParameterTypes.Length ||
                operations[index + 2].Operation != CilOperation.StoreField) return false;
            var field = operations[index + 2].Operand switch
            {
                CilOperand.Entity entity when !method.IsConstructed => _fields.GetField(entity.Key),
                CilOperand.FieldInstance instance when instance.Value.DeclaringType.Equals(method.DeclaringType) => instance.Value.Definition,
                _ => null,
            };
            if (field is null || field.IsStatic || field.DeclaringType != method.Definition.DeclaringType ||
                field.FieldType is not (CliValueKind.I4 or CliValueKind.I8 or CliValueKind.F4 or CliValueKind.F8) ||
                field.FieldType != method.Signature.ParameterTypes[parameter.Value - 1]) return false;
        }
        return true;
    }
}
