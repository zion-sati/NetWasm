// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;

namespace NetWasm.Compiler.Core.Types;

public sealed class MethodInitializationTriggerClassifier(ITypeRepository types) :
    IMethodInitializationTriggerClassifier
{
    private readonly ITypeRepository _types = types ?? throw new ArgumentNullException(nameof(types));

    public bool Classify(MethodDefinitionModel method)
    {
        ArgumentNullException.ThrowIfNull(method);
        if (method.Name == ".cctor")
            return false;

        var owner = _types.GetTypeDefinition(method.DeclaringType);
        // ECMA-335 I.8.9.5: strict initialization also precedes constructors
        // and value-type instance/virtual calls. Default-value creation is not
        // a method invocation and must not trigger initialization here.
        return !owner.IsBeforeFieldInit &&
            (method.IsStatic || method.Name == ".ctor" || owner.IsValueType);
    }
}
