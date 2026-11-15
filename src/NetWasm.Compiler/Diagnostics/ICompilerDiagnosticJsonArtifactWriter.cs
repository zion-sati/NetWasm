// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Text.Json.Serialization.Metadata;

namespace NetWasm.Compiler.Diagnostics;

internal interface ICompilerDiagnosticJsonArtifactWriter
{
    void WriteJson<T>(string path, T value, JsonTypeInfo<T> typeInfo);
}
