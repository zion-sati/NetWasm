// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

namespace NetWasm.Compiler.Analysis;

internal readonly record struct TypeRelationship(TypeRelationshipCharacteristics Characteristics)
{
    public bool IsAssignmentCompatible => Characteristics is not TypeRelationshipCharacteristics.None;

    public bool RequiresVariantMethodResolution =>
        (Characteristics & TypeRelationshipCharacteristics.GenericVariance) is not 0;

    public bool IsSzArrayGenericInterface =>
        (Characteristics & TypeRelationshipCharacteristics.SzArrayGenericInterface) is not 0;

    // The candidate is a shared array (SharedArrayShapes), assignable to the target only where the element in its
    // header is: a type test checks that element at run time, and the runtime's casting lists leave the target out.
    public bool RequiresElementCheck =>
        (Characteristics & TypeRelationshipCharacteristics.ElementChecked) is not 0;
}

[System.Flags]
internal enum TypeRelationshipCharacteristics
{
    None = 0,
    Hierarchy = 1 << 0,
    GenericVariance = 1 << 1,
    SzArrayGenericInterface = 1 << 2,
    ElementChecked = 1 << 3,
}
