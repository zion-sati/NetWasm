using System;
using System.Linq;
using NetWasm.Compiler.Layout;

namespace NetWasm.Compiler.Diagnostics;

internal sealed class CompilerLayoutSnapshotBuilder : ICompilerLayoutSnapshotBuilder
{
    public CompilerLayoutsArtifact BuildLayouts(ManagedLayoutSnapshot layouts)
    {
        ArgumentNullException.ThrowIfNull(layouts);
        return new CompilerLayoutsArtifact(
            layouts.Target.Target,
            layouts.Target.AddressSize,
            layouts.Target.ObjectReferenceSize,
            layouts.StaticDataEnd,
            [.. layouts.StaticRootAddresses.Order()],
            [.. layouts.TypeDescriptors.OrderBy(value => value.TypeId)],
            [.. layouts.ConstructedTypeDescriptors.OrderBy(value => value.TypeId)],
            [.. layouts.ValueTypeDescriptors.OrderBy(value => value.TypeId)],
            [.. layouts.DataSegments.OrderBy(value => value.Address)
                .Select(value => new CompilerDataSegmentArtifact(value.Address, value.Data.Length))]);
    }
}
