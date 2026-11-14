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
            // Every string comes from FastAllocateString.
            ("System.String", "FastAllocateString"),
            ("System.WeakReferenceRuntime", "Create"),
            ("System.WeakReferenceRuntime", "Set"),
            ("System.GCHandleRuntime", "Create"),
            ("System.GCHandleRuntime", "Set"),
            ("System.GCCollectionRuntime", "Collect"),
            ("System.GCFinalizerRuntime", "WaitForPending"),
            ("System.Array", "InternalGetValue"),
            // The element type is an uncached type object.
            ("System.Array", "InternalElementType"),
            // Clones and conversions the runtime allocates: an array, a range
            // of one, the boxes a copy into an object array makes, a boxed
            // value, a multicast delegate and an uncached type object.
            ("System.Array", "InternalClone"),
            ("System.Array", "InternalCloneRange"),
            ("System.Array", "InternalCopy"),
            ("System.Runtime.CompilerServices.RuntimeHelpersRuntime", "CloneBoxedValue"),
            ("System.DelegateRuntime", "Clone"),
            // An object's memberwise copy, arrays of a type known at run time and
            // an uninitialized object, which the runtime allocates, and a class
            // constructor run, which executes managed code.
            ("System.Object", "MemberwiseClone"),
            ("System.Array", "InternalCreateInstance"),
            ("System.Array", "InternalCreateRectangularInstance"),
            ("System.Runtime.CompilerServices.RuntimeHelpersRuntime", "AllocateUninitializedObject"),
            ("System.Runtime.CompilerServices.RuntimeHelpersRuntime", "RunClassConstructor"),
            ("System.Nullable", "GetUnderlyingType"),
            // Enum intrinsics allocate arrays, boxes and uncached type objects,
            // or call the allocating formatting helper outside managed CIL.
            // Wrappers and dispatch callers must inherit those safepoints.
            ("System.Enum", "InternalGetValues"),
            ("System.Enum", "InternalGetValuesAsUnderlyingType"),
            ("System.Enum", "InternalGetNames"),
            ("System.Enum", "InternalToObject"),
            ("System.Enum", "InternalGetUnderlyingType"),
            ("System.Enum", "ToString"),
            ("System.Enum", "InternalToString"),
            ("System.Enum", "InternalFormat"),
            // A value type's default equality and hash call each reference field's Equals and
            // GetHashCode, which can allocate, while the compared value is held only by the call.
            ("System.ValueType", "Equals"),
            ("System.ValueType", "GetHashCode"),
            ("System.Runtime.CompilerServices.ObjectArrayDelegateAdapter", "Create"),
            ("System.Runtime.CompilerServices.RuntimeMemberExecution", "InvokeMethod"),
            ("System.Runtime.CompilerServices.RuntimeMemberExecution", "InvokeDelegate"),
            ("System.Runtime.CompilerServices.RuntimeMemberExecution", "ReadField"),
            // A static field's write runs its type's initializer first.
            ("System.Runtime.CompilerServices.RuntimeMemberExecution", "WriteField"),
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
        // String's and Object's identities are the primitive ones, which have no full name.
        var typeName = method.DeclaringType.CanonicalName switch
        {
            "primitive:string" => "System.String",
            "primitive:object" => "System.Object",
            _ => method.DeclaringType.FullName!,
        };
        return Safepoints.Contains((typeName, method.Definition.Name));
    }
}
