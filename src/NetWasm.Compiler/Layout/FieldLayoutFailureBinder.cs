// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using System.Collections.Immutable;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Layout;

internal interface IFieldLayoutFailureBinder
{
    CompilerException Bind(CompilerException failure, CompilerDiagnosticContext requiringField);
}

internal sealed class FieldLayoutFailureBinder : IFieldLayoutFailureBinder
{
    public CompilerException Bind(CompilerException failure, CompilerDiagnosticContext requiringField)
    {
        ArgumentNullException.ThrowIfNull(failure);
        ArgumentNullException.ThrowIfNull(requiringField);
        var chain = failure.Diagnostic.DependencyChain.IsDefault
            ? ImmutableArray<CompilerDependencyStep>.Empty : failure.Diagnostic.DependencyChain;
        return new CompilerException(failure.Diagnostic with
        {
            Context = failure.Diagnostic.Context ?? requiringField,
            DependencyChain = chain.Insert(0, new(requiringField, CompilerDependencyKind.FieldStorage)),
        }, failure);
    }
}
