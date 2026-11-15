// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Immutable;

namespace NetWasm.Compiler.Core;

/// <summary>
/// The structure-marshalling descriptor published for one runtime type. Address is the
/// static descriptor that Marshal's structure members read; it is never reflection metadata.
/// </summary>
public readonly record struct StructMarshallingLayout(CliTypeIdentity Type, int TypeId, int Address);

/// <summary>Descriptors for the types Marshal's structure members can receive.</summary>
public interface IStructMarshallingSource
{
    ImmutableArray<StructMarshallingLayout> StructMarshalling { get; }
}

/// <summary>The allocated types RuntimeHelpers.GetUninitializedObject may create.</summary>
public interface IUninitializedObjectSource
{
    ImmutableArray<CliTypeIdentity> UninitializedObjectTypes { get; }
}

/// <summary>A source for layouts that publish no structure-marshalling or allocation data.</summary>
public sealed class EmptyRuntimeTypeDataSource : IStructMarshallingSource, IUninitializedObjectSource
{
    public static EmptyRuntimeTypeDataSource Instance { get; } = new();

    public ImmutableArray<StructMarshallingLayout> StructMarshalling => [];

    public ImmutableArray<CliTypeIdentity> UninitializedObjectTypes => [];
}
