// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

namespace NetWasm.Compiler.Analysis;

internal interface ITypeTestPlannerFactory
{
    ITypeTestPlanner Create(
        ITypeOperandResolver typeOperands,
        ITypeRelationshipClassifier relationships,
        ITypeRelationshipKeyResolver relationshipKeys);
}
