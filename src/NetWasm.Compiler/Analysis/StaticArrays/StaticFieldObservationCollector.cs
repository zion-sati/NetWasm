// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Analysis.StaticArrays;

internal sealed class StaticFieldObservationCollector : IStaticFieldObservationCollector
{
    public ImmutableHashSet<EntityKey> Collect(ReachableProgram program, ReachabilityRoots roots)
    {
        ArgumentNullException.ThrowIfNull(program);
        ArgumentNullException.ThrowIfNull(roots);
        var observed = roots.Fields.ToImmutableHashSet().ToBuilder();
        observed.UnionWith(program.FieldDescriptors.Values.Select(field => field.Definition.Key));
        foreach (var body in program.Methods.Values.Concat(program.ConstructedMethods.Values))
        {
            foreach (var instruction in body.Body.Instructions)
            {
                if (instruction.Operation == CilOperation.StoreStaticField) continue;
                switch (instruction.Operand)
                {
                    case CilOperand.FieldInstance field:
                        observed.Add(field.Value.Definition.Key);
                        break;
                    case CilOperand.Entity field when instruction.Operation is
                        CilOperation.LoadStaticField or CilOperation.LoadStaticFieldAddress or CilOperation.LoadFieldToken or
                        CilOperation.LoadField or CilOperation.LoadFieldAddress or CilOperation.StoreField:
                        observed.Add(field.Key);
                        break;
                    case CilOperand.ReadOnlySpanData data:
                        observed.Add(data.Field.Definition.Key);
                        break;
                }
            }
        }
        return observed.ToImmutable();
    }
}
