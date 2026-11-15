// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Analysis;

internal sealed class DelegateMethodClassifier(
    IDelegateTypeRecognizer delegateTypes) : IDelegateMethodClassifier
{
    public DelegateMethodKind Classify(MethodInstanceModel method)
    {
        if (!delegateTypes.Recognize(method.DeclaringType))
        {
            return DelegateMethodKind.None;
        }

        return method.Definition.Name switch
        {
            ".ctor" => DelegateMethodKind.Constructor,
            "Invoke" => DelegateMethodKind.Invoke,
            _ => DelegateMethodKind.None,
        };
    }
}
