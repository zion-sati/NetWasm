using System.Collections.Immutable;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.GarbageCollection.Tests;

public sealed class RuntimeAllocationSafepointClassifierTests
{
    private static readonly AssemblyIdentity CoreLib = new("NetWasm.CoreLib");

    public static TheoryData<string, string> Safepoints => new()
    {
        { "System.Runtime.InteropServices.NativeMemory", "Alloc" },
        { "System.Runtime.InteropServices.NativeMemory", "Realloc" },
        { "System.Runtime.InteropServices.NativeMemory", "AlignedAllocCore" },
        { "System.Runtime.InteropServices.NativeMemory", "AlignedReallocCore" },
        { "System.Runtime.InteropServices.WebAssembly.CanonicalAbi", "Reallocate" },
        { "System.Runtime.InteropServices.WebAssembly.CanonicalAbi", "CreateResourceHandle" },
        { "System.Runtime.CompilerServices.ObjectArrayDelegateAdapter", "Create" },
        { "System.Runtime.CompilerServices.RuntimeMemberExecution", "InvokeMethod" },
        { "System.Runtime.CompilerServices.RuntimeMemberExecution", "ReadField" }
    };

    [Theory]
    [MemberData(nameof(Safepoints))]
    [InlineData("System.WeakReferenceRuntime", "Create")]
    [InlineData("System.WeakReferenceRuntime", "Set")]
    [InlineData("System.GCHandleRuntime", "Create")]
    [InlineData("System.GCHandleRuntime", "Set")]
    [InlineData("System.GCCollectionRuntime", "Collect")]
    [InlineData("System.GCFinalizerRuntime", "WaitForPending")]
    // Issue 144 and its class: clones and conversions the runtime allocates.
    [InlineData("System.Array", "InternalElementType")]
    [InlineData("System.Array", "InternalClone")]
    [InlineData("System.Array", "InternalCloneRange")]
    [InlineData("System.Array", "InternalCopy")]
    [InlineData("System.Runtime.CompilerServices.RuntimeHelpersRuntime", "CloneBoxedValue")]
    [InlineData("System.DelegateRuntime", "Clone")]
    [InlineData("System.Object", "MemberwiseClone")]
    [InlineData("System.Array", "InternalCreateInstance")]
    [InlineData("System.Array", "InternalCreateRectangularInstance")]
    [InlineData("System.Runtime.CompilerServices.RuntimeHelpersRuntime", "AllocateUninitializedObject")]
    [InlineData("System.Runtime.CompilerServices.RuntimeHelpersRuntime", "RunClassConstructor")]
    [InlineData("System.Nullable", "GetUnderlyingType")]
    [InlineData("System.Enum", "InternalGetValues")]
    [InlineData("System.Enum", "InternalGetValuesAsUnderlyingType")]
    [InlineData("System.Enum", "InternalGetNames")]
    [InlineData("System.Enum", "InternalToObject")]
    [InlineData("System.Enum", "InternalGetUnderlyingType")]
    [InlineData("System.Enum", "ToString")]
    [InlineData("System.Enum", "InternalToString")]
    [InlineData("System.Enum", "InternalFormat")]
    // A value type's default equality and hash run each reference field's own Equals and
    // GetHashCode.
    [InlineData("System.ValueType", "Equals")]
    [InlineData("System.ValueType", "GetHashCode")]
    public void ClassifyRecognizesEveryRuntimeAllocationBoundary(
        string typeName,
        string methodName)
    {
        var classifier = new RuntimeAllocationSafepointClassifier();
        var method = CreateMethod(typeName, methodName);

        Assert.True(Classify(classifier, method));
        Assert.True(Classify(classifier,
            method.Definition,
            new SingleTypeRepository(method.Definition.DeclaringType, typeName)));
    }

    [Theory]
    [InlineData("System.Runtime.InteropServices.NativeMemory", "Free")]
    [InlineData("System.Runtime.InteropServices.WebAssembly.CanonicalAbi", "Free")]
    [InlineData("Example.NativeMemory", "Alloc")]
    [InlineData("System.Enum", "InternalGetMetadata")]
    [InlineData("System.Enum", "InternalToInt32")]
    [InlineData("Example.Enum", "InternalGetValues")]
    public void ClassifyRejectsNonAllocatingAndUnrelatedMethods(
        string typeName,
        string methodName)
    {
        var classifier = new RuntimeAllocationSafepointClassifier();
        var method = CreateMethod(typeName, methodName);

        Assert.False(Classify(classifier, method));
        Assert.False(Classify(classifier,
            method.Definition,
            new SingleTypeRepository(method.Definition.DeclaringType, typeName)));
    }

    [Fact]
    public void ClassifyRejectsMissingInputs()
    {
        var classifier = new RuntimeAllocationSafepointClassifier();
        var method = CreateMethod(
            "System.Runtime.InteropServices.NativeMemory",
            "Alloc");
        var types = new SingleTypeRepository(
            method.Definition.DeclaringType,
            "System.Runtime.InteropServices.NativeMemory");

        Assert.Throws<ArgumentNullException>(() => Classify(classifier, null!));
        Assert.Throws<ArgumentNullException>(() => Classify(classifier, null!, types));
        Assert.Throws<ArgumentNullException>(() => Classify(
            classifier,
            method.Definition,
            null!));
    }

    private static MethodInstanceModel CreateMethod(string typeName, string methodName)
    {
        var separator = typeName.LastIndexOf('.');
        var declaringType = CliTypeIdentity.Named(
            CoreLib,
            typeName[..separator],
            typeName[(separator + 1)..],
            false);
        var key = new EntityKey(CoreLib, 1);
        var signature = MethodSignatureModel.Create(
            CliTypeIdentity.Primitive("System.Int32", CliValueKind.I4));
        var definition = new MethodDefinitionModel(
            key,
            key,
            methodName,
            true,
            signature,
            0);
        return new MethodInstanceModel(
            definition,
            declaringType,
            ImmutableArray<CliTypeIdentity>.Empty,
            signature);
    }

    private static bool Classify<TClassifier>(
        TClassifier classifier,
        MethodInstanceModel method)
        where TClassifier : IRuntimeAllocationSafepointClassifier =>
        classifier.Classify(method);

    private static bool Classify<TClassifier>(
        TClassifier classifier,
        MethodDefinitionModel method,
        ITypeRepository types)
        where TClassifier : IRuntimeAllocationSafepointClassifier =>
        classifier.Classify(method, types);

    // String's and Object's identities are primitive and carry no full name, so the type's name is passed.
    private sealed class SingleTypeRepository(
        EntityKey key,
        string fullName) : ITypeRepository
    {
        public TypeDefinitionModel GetTypeDefinition(EntityKey requestedKey)
        {
            Assert.Equal(key, requestedKey);
            var separator = fullName.LastIndexOf('.');
            return new TypeDefinitionModel(
                key,
                fullName[..separator],
                fullName[(separator + 1)..],
                false,
                ImmutableArray<EntityKey>.Empty,
                ImmutableArray<EntityKey>.Empty);
        }
    }
}
