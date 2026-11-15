// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using System.Text.Json;

namespace NetWasm.Compiler.ComponentModel.Browser.Plans;

public interface IBrowserLinkPlanSerializer
{
    string Serialize(BrowserLinkPlan plan);
    string Serialize(BrowserLinkExecutionPlan plan);
}

public sealed class BrowserLinkPlanSerializer : IBrowserLinkPlanSerializer
{
    public string Serialize(BrowserLinkExecutionPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        return JsonSerializer.Serialize(plan, BrowserLinkPlanJsonContext.Default.BrowserLinkExecutionPlan);
    }

    public string Serialize(BrowserLinkPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        return JsonSerializer.Serialize(plan, BrowserLinkPlanJsonContext.Default.BrowserLinkPlan);
    }
}
