// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using System.Linq;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Metadata;

namespace NetWasm.Compiler.Analysis;

// CoreLib's shared array of a shape (SharedArrayShapes): rank 0 is the vector, SharedArrayElement[]; rank 1 is
// SharedArrayElement[*], and higher ranks the rectangular arrays.
internal interface ISharedArrayShapeResolver
{
    CliTypeIdentity Resolve(int rank);
}

internal sealed class SharedArrayShapeResolver(ITypeFinder typeFinder) : ISharedArrayShapeResolver
{
    private readonly Lazy<CliTypeIdentity> _element = new(() =>
        CliTypeIdentity.FromDefinition(typeFinder.FindType(SharedArrayShapes.ElementName)));

    public CliTypeIdentity Resolve(int rank)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(rank);
        return rank == 0 ? CliTypeIdentity.SzArray(_element.Value) : CliTypeIdentity.Array(_element.Value, rank);
    }
}

// CoreLib's GetType for an object of a shared array type (SharedArrays.TypeOf), which the compiled GetType calls for
// those types.
internal interface ISharedArrayTypeOfResolver
{
    MethodInstanceModel Resolve();
}

internal sealed class SharedArrayTypeOfResolver(
    ITypeFinder typeFinder,
    IMethodRepository methods,
    IMethodInstanceResolver methodInstances,
    ISymbolFormatter symbols) : ISharedArrayTypeOfResolver
{
    private const string HelperType = "System.SharedArrays";

    public MethodInstanceModel Resolve()
    {
        var candidates = typeFinder.FindType(HelperType).Methods
            .Select(methods.GetMethod)
            .Where(static method => method.IsStatic && method.Name == "TypeOf")
            .Take(2)
            .ToArray();
        if (candidates.Length != 1)
        {
            throw new CompilerException(new CompilerDiagnostic(
                DiagnosticCode.RuntimeContract, "shared arrays must define exactly one 'TypeOf'"));
        }
        var helper = candidates[0];
        return methodInstances.ResolveMethodInstance(
            helper.Key.Assembly, helper.Key.MetadataToken, symbols.Format(helper), 0);
    }
}
