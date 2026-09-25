using System;
using System.Collections.Frozen;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.GarbageCollection;

public sealed class RuntimeAllocationSafepointClassifier :
    IRuntimeAllocationSafepointClassifier
{
    private static readonly FrozenSet<(string Type, string Method)> Safepoints =
        new[]
        {
            ("System.Runtime.InteropServices.NativeMemory", "Alloc"),
            ("System.Runtime.InteropServices.NativeMemory", "Realloc"),
            ("System.Runtime.InteropServices.NativeMemory", "AlignedAllocCore"),
            ("System.Runtime.InteropServices.NativeMemory", "AlignedReallocCore"),
            ("System.Runtime.InteropServices.WebAssembly.CanonicalAbi", "Reallocate"),
            ("System.Runtime.InteropServices.WebAssembly.CanonicalAbi", "CreateResourceHandle"),
            ("System.WeakReferenceRuntime", "Create"),
            ("System.WeakReferenceRuntime", "Set"),
            ("System.GCHandleRuntime", "Create"),
            ("System.GCHandleRuntime", "Set"),
            ("System.GCCollectionRuntime", "Collect"),
            ("System.GCFinalizerRuntime", "WaitForPending"),
            ("System.Array", "InternalGetValue"),
        }.ToFrozenSet();

    public bool Classify(MethodDefinitionModel method, ITypeRepository types)
    {
        ArgumentNullException.ThrowIfNull(method);
        ArgumentNullException.ThrowIfNull(types);
        var declaringType = types.GetTypeDefinition(method.DeclaringType);
        return Safepoints.Contains((declaringType.FullName, method.Name));
    }

    public bool Classify(MethodInstanceModel method)
    {
        ArgumentNullException.ThrowIfNull(method);
        return Safepoints.Contains((method.DeclaringType.FullName!, method.Definition.Name));
    }
}
