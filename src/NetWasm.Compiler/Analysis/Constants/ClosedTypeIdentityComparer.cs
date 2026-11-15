// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Analysis.Constants;

internal sealed class ClosedTypeIdentityComparer(ITypeDefinitionResolver definitions) : IClosedTypeIdentityComparer
{
    private readonly ITypeDefinitionResolver _definitions = definitions ?? throw new ArgumentNullException(nameof(definitions));

    public bool? Compare(CliTypeIdentity left, CliTypeIdentity right)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);
        if (left.ContainsGenericParameters || right.ContainsGenericParameters ||
            left.Shape is CliTypeShape.Modified or CliTypeShape.FunctionPointer ||
            right.Shape is CliTypeShape.Modified or CliTypeShape.FunctionPointer)
        {
            return null;
        }

        if (left.Shape is CliTypeShape.Named or CliTypeShape.Primitive &&
            right.Shape is CliTypeShape.Named or CliTypeShape.Primitive)
        {
            return _definitions.ResolveTypeIdentity(left).Key == _definitions.ResolveTypeIdentity(right).Key;
        }

        if (left.Shape != right.Shape)
        {
            return false;
        }

        if (left.Shape == CliTypeShape.GenericInstantiation)
        {
            var definitionEqual = Compare(left.ElementType!, right.ElementType!);
            if (definitionEqual != true)
            {
                return definitionEqual;
            }
            if (left.TypeArguments.Length != right.TypeArguments.Length)
            {
                return false;
            }
            var unknown = false;
            for (var index = 0; index < left.TypeArguments.Length; index++)
            {
                var equal = Compare(left.TypeArguments[index], right.TypeArguments[index]);
                if (equal == false) return false;
                unknown |= equal is null;
            }
            return unknown ? null : true;
        }

        return left.ArrayRank != right.ArrayRank
            ? false
            : Compare(left.ElementType!, right.ElementType!);
    }
}
