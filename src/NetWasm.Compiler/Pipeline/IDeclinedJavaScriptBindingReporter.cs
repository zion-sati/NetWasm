// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.Core;
using NetWasm.Compiler.Metadata;

namespace NetWasm.Compiler.Pipeline;

internal interface IDeclinedJavaScriptBindingReporter
{
    void Report(MetadataCompilationSnapshot metadata, ISymbolFormatter symbols, bool javaScriptHost);
}
