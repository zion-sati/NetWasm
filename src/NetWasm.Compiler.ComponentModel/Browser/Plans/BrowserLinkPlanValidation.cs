// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using NetWasm.Compiler.ComponentModel.Node;

namespace NetWasm.Compiler.ComponentModel.Browser.Plans;

/// <summary>Composition for the closed browser core-link capability set.</summary>
public static class BrowserLinkPlanValidation
{
    internal static IBinaryenToolRunner CreateToolCapture(IBrowserLinkOperationRecorder recorder) =>
        new BrowserLinkNativeToolCapture(recorder, new Dictionary<string, IBrowserLinkNativeToolBuilder>(StringComparer.Ordinal)
        {
            ["wasm-merge"] = new BrowserLinkNativeMergeBuilder(CreateInspector()),
            ["wasm-opt"] = new BrowserLinkNativeOptimizationBuilder(CreateInspector()),
        }.ToFrozenDictionary(StringComparer.Ordinal));

    public static IBrowserLinkPlanValidator Create() => new BrowserLinkPlanValidator(
        new BrowserLinkPathValidator(), CreateInspector());

    public static IBrowserLinkPlanBuilder CreateBuilder() => new BrowserLinkPlanBuilder(CreateInspector(), Create());

    public static IBrowserLinkExecutionPlanBuilder CreateExecutionBuilder() =>
        new BrowserLinkExecutionPlanBuilder(Create(), CreateInspector(), new BrowserLinkToolMaterializer(CreateInspector()));

    public static IBrowserLinkOperationInspector CreateInspector() =>
        new BrowserLinkOperationInspector(new KeyValuePair<Type, IBrowserLinkOperationInspector>[]
        {
            new(typeof(BrowserLinkTextOperation), new BrowserLinkTextInspector()),
            new(typeof(BrowserLinkInlineOperation), new BrowserLinkInlineInspector()),
            new(typeof(BrowserLinkExportOperation), new BrowserLinkExportInspector()),
            new(typeof(BrowserLinkValidationOperation), new BrowserLinkValidationInspector()),
            new(typeof(BrowserLinkMoveOperation), new BrowserLinkMoveInspector()),
            new(typeof(BrowserLinkToolOperation), new BrowserLinkToolInspector(
                new KeyValuePair<string, IBrowserLinkOperationInspector>[]
                {
                    new("wasm-merge", new BrowserLinkMergeInspector()),
                    new("wasm-opt", new BrowserLinkOptimizationInspector()),
                })),
        });
}
