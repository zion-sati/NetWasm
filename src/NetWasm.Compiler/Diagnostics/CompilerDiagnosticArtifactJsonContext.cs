// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;

namespace NetWasm.Compiler.Diagnostics;

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UseStringEnumConverter = true,
    WriteIndented = true,
    NewLine = "\n")]
[JsonSerializable(typeof(CompilerOptionsArtifact))]
[JsonSerializable(typeof(CompilerSourceArtifact[]))]
[JsonSerializable(typeof(CompilerProvenanceArtifact))]
[JsonSerializable(typeof(CompilerEntryArtifact))]
[JsonSerializable(typeof(CompilerMetadataArtifact))]
[JsonSerializable(typeof(CompilerProgramArtifact))]
[JsonSerializable(typeof(CompilerLayoutsArtifact))]
[JsonSerializable(typeof(CompilerRootMapsArtifact))]
[JsonSerializable(typeof(CompilerComplexityReport))]
[JsonSerializable(typeof(CompilerEmissionArtifact))]
[JsonSerializable(typeof(CompilerStatusArtifact))]
[JsonSerializable(typeof(CompilerFailureArtifact))]
[JsonSerializable(typeof(CompilerObservationsArtifact))]
[ExcludeFromCodeCoverage]
internal sealed partial class CompilerDiagnosticArtifactJsonContext : JsonSerializerContext;
