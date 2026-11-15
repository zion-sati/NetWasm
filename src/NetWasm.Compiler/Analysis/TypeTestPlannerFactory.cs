// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.Core.Types;

namespace NetWasm.Compiler.Analysis;

internal sealed class TypeTestPlannerFactory(
    INullableTypeResolver nullableTypes) : ITypeTestPlannerFactory
{
    public ITypeTestPlanner Create(
        ITypeOperandResolver typeOperands,
        ITypeRelationshipClassifier relationships,
        ITypeRelationshipKeyResolver relationshipKeys) =>
        new TypeTestPlanner(typeOperands, relationships, relationshipKeys, nullableTypes);
}
