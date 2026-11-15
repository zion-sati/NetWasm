// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Immutable;
using System.Text.Json.Serialization;
using NetWasm.Compiler.ComponentModel.Inlining;

namespace NetWasm.Compiler.ComponentModel.Browser.Plans;

[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(BrowserLinkTextOperation), "text")]
[JsonDerivedType(typeof(BrowserLinkToolOperation), "tool")]
[JsonDerivedType(typeof(BrowserLinkInlineOperation), "inline")]
[JsonDerivedType(typeof(BrowserLinkExportOperation), "exports")]
[JsonDerivedType(typeof(BrowserLinkValidationOperation), "validate")]
[JsonDerivedType(typeof(BrowserLinkMoveOperation), "move")]
public abstract record BrowserLinkOperation;

public sealed record BrowserLinkTextOperation(
    [property: JsonRequired] string OutputPath,
    [property: JsonRequired] string Text) : BrowserLinkOperation;

public sealed record BrowserLinkToolOperation(
    [property: JsonRequired] string ToolId,
    [property: JsonRequired] ImmutableArray<string> Arguments,
    [property: JsonRequired] ImmutableArray<string> InputPaths,
    [property: JsonRequired] ImmutableArray<string> OutputPaths) : BrowserLinkOperation;

public sealed record BrowserLinkInlineOperation(
    [property: JsonRequired] string InputPath,
    [property: JsonRequired] string OutputPath,
    [property: JsonRequired] WasmInlineAnnotationSelection Selection) : BrowserLinkOperation;

public sealed record BrowserLinkExportOperation(
    [property: JsonRequired] string InputPath,
    [property: JsonRequired] string OutputPath,
    [property: JsonRequired] string? ComponentPrefix,
    [property: JsonRequired] ImmutableArray<WasmInternalExport> RemovedExports) : BrowserLinkOperation;

public sealed record BrowserLinkValidationOperation(
    [property: JsonRequired] string Path,
    [property: JsonRequired] ImmutableArray<string> Arguments) : BrowserLinkOperation;

public sealed record BrowserLinkMoveOperation(
    [property: JsonRequired] string InputPath,
    [property: JsonRequired] string OutputPath) : BrowserLinkOperation;
