// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Immutable;

namespace NetWasm.Compiler.Core;

/// <summary>
/// CoreLib's shared arrays (System.SharedArrayElement[], [*], [,] and [,,]): Array.CreateInstance's arrays of a
/// reference element the program has no array type of. Each keeps its element in its header, as any array of
/// references does, so a cast, store or copy checks that element there; a program pays once per shape it creates, not
/// once per element type it could pass, as Native AOT's one canonical form serves every array of references.
/// </summary>
public static class SharedArrayShapes
{
    public const string ElementName = "System.SharedArrayElement";

    // Whether the type is a shared array.
    public static bool IsShared(CliTypeIdentity type) =>
        type.Shape is CliTypeShape.SzArray or CliTypeShape.Array && type.ElementType is { } element &&
        IsElement(element);

    // Whether the type is the shared arrays' element type, of which no object exists.
    public static bool IsElement(CliTypeIdentity type) =>
        type.Shape == CliTypeShape.Named && type.FullName == ElementName;
}

/// <summary>
/// The shared arrays a program allocates, and CoreLib's GetType for an object of one (SharedArrays.TypeOf), which
/// answers the array type of its element instead of the shared type.
/// </summary>
public sealed record SharedArrayPlan(ImmutableArray<CliTypeIdentity> Types, MethodInstanceModel TypeOf);
