// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Metadata;

internal interface IMissingTypeFinder
{
    // The first type a decoded body's instructions name that the compilation lacks, where it is named, or null.
    MissingMemberReference? Find(CilMethodBody body, string method);
}

// A type token (typeof, castclass, isinst, box, newarr, sizeof, ldobj and the rest) names its type without resolving
// it while the IL is decoded, so a type the assembly lacks surfaced only when something needed it, failing the build.
// Each type an instruction names is resolved here instead, its element and type arguments with it; generic
// parameters and primitives always resolve.
internal sealed class MissingTypeFinder(ITypeDefinitionResolver definitions) : IMissingTypeFinder
{
    private readonly ITypeDefinitionResolver _definitions = definitions ?? throw new ArgumentNullException(nameof(definitions));

    public MissingMemberReference? Find(CilMethodBody body, string method)
    {
        ArgumentNullException.ThrowIfNull(body);
        foreach (var instruction in body.Instructions)
        {
            var type = instruction.Operand switch
            {
                CilOperand.TypeIdentity { Value: var named } => named,
                CilOperand.ExactType { Value: var exact } => exact,
                _ => null,
            };
            if (type is not null && Missing(type) is { } missing)
            {
                return missing with { Method = method, IlOffset = instruction.SourceOffset };
            }
        }
        return null;
    }

    private MissingMemberReference? Missing(CliTypeIdentity type)
    {
        switch (type.Shape)
        {
            case CliTypeShape.Named:
                try
                {
                    _definitions.ResolveTypeIdentity(type);
                    return null;
                }
                catch (CompilerException error) when (error.Diagnostic.MissingMember is { Kind: MissingMemberKind.Type } missing)
                {
                    return missing;
                }
            case CliTypeShape.GenericInstantiation:
                if (Missing(type.ElementType!) is { } definition) return definition;
                foreach (var argument in type.TypeArguments)
                {
                    if (Missing(argument) is { } missing) return missing;
                }
                return null;
            // Each of these shapes is made with its element type.
            case CliTypeShape.SzArray or CliTypeShape.Array or CliTypeShape.ManagedByReference or
                CliTypeShape.UnmanagedPointer or CliTypeShape.Modified:
                return Missing(type.ElementType!);
            default:
                return null;
        }
    }
}
