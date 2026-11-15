// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.ModuleEncoding;

namespace NetWasm.Compiler.Wasm;

public sealed record WasmFunctionType(
    ImmutableArray<CliValueKind> Parameters,
    CliValueKind Result)
{
    public static WasmFunctionType Create(
        CliValueKind result,
        params CliValueKind[] parameters) =>
        new([.. parameters], result);
}

public sealed record WasmFunctionImport(
    string Module,
    string Name,
    WasmFunctionType Type);

public sealed record WasmFunctionDefinition(
    string Name,
    WasmFunctionType Type,
    byte[] Body)
{
    public bool IsAggressiveInlining { get; init; }
    public bool IsNoInlining { get; init; }

    // The method definition the function compiles; null for a function the compiler generates.
    public EntityKey? Method { get; init; }
}

public enum WasmExportKind : byte
{
    Function = 0,
    Memory = 2,
}

public sealed record WasmExport(
    string Name,
    int Index,
    WasmExportKind Kind = WasmExportKind.Function);

public sealed record WasmModuleBuildRequest(
    IReadOnlyList<WasmFunctionImport> FunctionImports,
    string MemoryImportModule,
    string MemoryImportName,
    IReadOnlyList<WasmFunctionDefinition> Functions,
    IReadOnlyList<WasmExport> Exports,
    IReadOnlyList<DataSegment> DataSegments,
    bool IncludeManagedExceptionTag,
    WasmTarget Target,
    bool IncludeNameSection = true);

public interface IWasmModuleBuilder
{
    byte[] Build(
        IReadOnlyList<WasmFunctionImport> functionImports,
        string memoryImportModule,
        string memoryImportName,
        IReadOnlyList<WasmFunctionDefinition> functions,
        IReadOnlyList<WasmExport> exports,
        IReadOnlyList<DataSegment> dataSegments,
        bool includeManagedExceptionTag = false,
        WasmTarget target = WasmTarget.Wasm32,
        bool includeNameSection = true);
}

public sealed class WasmModuleBuilder : IWasmModuleBuilder
{
    private readonly IModuleEncoder _encoder;

    public WasmModuleBuilder(IModuleEncoder encoder)
    {
        ArgumentNullException.ThrowIfNull(encoder);
        _encoder = encoder;
    }

    public byte[] Build(
        IReadOnlyList<WasmFunctionImport> functionImports,
        string memoryImportModule,
        string memoryImportName,
        IReadOnlyList<WasmFunctionDefinition> functions,
        IReadOnlyList<WasmExport> exports,
        IReadOnlyList<DataSegment> dataSegments,
        bool includeManagedExceptionTag = false,
        WasmTarget target = WasmTarget.Wasm32,
        bool includeNameSection = true)
    {
        return _encoder.Encode(new WasmModuleBuildRequest(
            functionImports,
            memoryImportModule,
            memoryImportName,
            functions,
            exports,
            dataSegments,
            includeManagedExceptionTag,
            target,
            includeNameSection));
    }
}
