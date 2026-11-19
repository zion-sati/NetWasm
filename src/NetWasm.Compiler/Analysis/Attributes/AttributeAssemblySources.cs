// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Generic;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Analysis.Attributes;

// The calls whose result is an assembly the compile knows: RuntimeAssembly.Of<T>(), which Assembly.GetExecutingAssembly()
// becomes (ExecutingAssemblyCallRewriter), and Type.Assembly on a type the analysis has proven.
internal sealed class AttributeAssemblySources(
    ITypeDefinitionResolver definitions,
    EntityKey assemblyOf,
    EntityKey typeAssembly) : IAttributeAssemblySources
{
    public AttributeValueProof? Prove(CilOperation operation, MethodInstanceModel method, List<AttributeValueProof> stack)
    {
        if (operation == CilOperation.Call && method.Definition.Key == assemblyOf && method.MethodArguments is [var declaring])
        {
            return AssemblyOf(declaring);
        }
        if (operation is CilOperation.Call or CilOperation.CallVirtual && method.Definition.Key == typeAssembly &&
            stack[^1] is { Kind: AttributeValueKind.Type, Type: { } type })
        {
            return AssemblyOf(type);
        }
        return null;
    }

    private AttributeValueProof? AssemblyOf(CliTypeIdentity type) => type.ContainsGenericParameters ||
        type.Shape is not (CliTypeShape.Named or CliTypeShape.GenericInstantiation or CliTypeShape.Primitive)
            ? null
            : new(AttributeValueKind.Assembly, Assembly: definitions.ResolveTypeIdentity(type).Key.Assembly);
}
