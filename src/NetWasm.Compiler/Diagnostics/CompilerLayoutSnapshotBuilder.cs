using System;
using System.Linq;
using NetWasm.Compiler.Layout;

namespace NetWasm.Compiler.Diagnostics;

internal sealed class CompilerLayoutSnapshotBuilder : ICompilerLayoutSnapshotBuilder
{
    public object BuildLayouts(ManagedLayoutSnapshot layouts)
    {
        ArgumentNullException.ThrowIfNull(layouts);
        return new
        {
            layouts.Target.Target,
            layouts.Target.AddressSize,
            layouts.Target.ObjectReferenceSize,
            layouts.StaticDataEnd,
            StaticRoots = layouts.StaticRootAddresses.Order().ToArray(),
            TypeDescriptors = layouts.TypeDescriptors.OrderBy(value => value.TypeId),
            ConstructedTypeDescriptors = layouts.ConstructedTypeDescriptors
                .OrderBy(value => value.TypeId),
            ValueTypeDescriptors = layouts.ValueTypeDescriptors
                .OrderBy(value => value.TypeId),
            DataSegments = layouts.DataSegments.OrderBy(value => value.Address)
                .Select(value => new { value.Address, Length = value.Data.Length }),
        };
    }
}
