// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Metadata;

namespace NetWasm.Compiler.Analysis.ReceiverFlow;

internal interface IReflectionObjectBounds
{
    // The type bounding the reflection object the operation pushes, or null for an operation that pushes none.
    CliTypeIdentity? Of(CilOperation operation);
}

// The runtime's reflection objects, bounded by what the call each operation replaces declares (IntrinsicCallRewriter):
// Object.GetType, Type.GetTypeFromHandle and Type.GetTypeFromSemanticId give a System.Type (a RuntimeType, or a
// subclass of it such as a shared array's, so no exact class), MethodBase.GetMethodFromHandle a MethodBase (a method's
// or a constructor's descriptor) and FieldInfo.GetFieldFromHandle a FieldInfo. The other reflection objects come from
// managed code (an Assembly, a ParameterInfo) or from fields the runtime writes (an accessor's PropertyInfo), which the
// analysis bounds already. Each bound is resolved when first asked for: a program without reflection resolves none.
internal sealed class ReflectionObjectBounds(ITypeFinder types, ITypeIdentityResolver identities) : IReflectionObjectBounds
{
    private readonly ITypeFinder _types = types ?? throw new ArgumentNullException(nameof(types));
    private readonly ITypeIdentityResolver _identities = identities ?? throw new ArgumentNullException(nameof(identities));
    private CliTypeIdentity? _type;
    private CliTypeIdentity? _method;
    private CliTypeIdentity? _field;

    public CliTypeIdentity? Of(CilOperation operation) => operation switch
    {
        CilOperation.GetObjectType or CilOperation.MaterializeType => _type ??= Resolve("System.Type"),
        CilOperation.MaterializeMethod => _method ??= Resolve("System.Reflection.MethodBase"),
        CilOperation.MaterializeField => _field ??= Resolve("System.Reflection.FieldInfo"),
        _ => null,
    };

    private CliTypeIdentity Resolve(string fullName) => _identities.GetTypeIdentity(_types.FindType(fullName).Key);
}
