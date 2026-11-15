// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.Metadata.ManagedExecutables;

namespace NetWasm.Compiler.Tasks.Compilation;

internal sealed class ManagedEntryPointReader : IManagedEntryPointReader
{
    private readonly IManagedExecutableEntryPointSelector _entries;

    public ManagedEntryPointReader() : this(new ManagedExecutableEntryPointSelectorFactory().Create()) { }

    public ManagedEntryPointReader(IManagedExecutableEntryPointSelector entries) =>
        _entries = entries ?? throw new ArgumentNullException(nameof(entries));

    public ManagedEntryPoint Read(string assemblyPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(assemblyPath);
        var entry = _entries.SelectEntryPoint(assemblyPath, File.ReadAllBytes(assemblyPath));
        return new(entry.TypeName, entry.MethodName, entry.MetadataToken, entry.Abi);
    }
}
