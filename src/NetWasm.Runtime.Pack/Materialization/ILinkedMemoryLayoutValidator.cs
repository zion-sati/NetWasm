// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: MIT

namespace NetWasm.Runtime.Pack.Materialization;

internal interface ILinkedMemoryLayoutValidator
{
    void Validate(RuntimeMemoryPlan plan, RuntimeLinkedMemoryLayout observed);
    void Validate(RuntimeMemoryPlan plan, RuntimeLinkedMemoryLayout observed, RuntimeLinkedMemoryLayout expected);
}
