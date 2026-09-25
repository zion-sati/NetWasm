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
        { "System.Runtime.InteropServices.WebAssembly.CanonicalAbi", "CreateResourceHandle" }
    };

    [Theory]
    [MemberData(nameof(Safepoints))]
    [InlineData("System.WeakReferenceRuntime", "Create")]
    [InlineData("System.WeakReferenceRuntime", "Set")]
    [InlineData("System.GCHandleRuntime", "Create")]
    [InlineData("System.GCHandleRuntime", "Set")]
    [InlineData("System.GCCollectionRuntime", "Collect")]
    [InlineData("System.GCFinalizerRuntime", "WaitForPending")]
    public void ClassifyRecognizesEveryRuntimeAllocationBoundary(
        string typeName,
        string methodName)
    {
        var classifier = new RuntimeAllocationSafepointClassifier();
        var method = CreateMethod(typeName, methodName);

        Assert.True(Classify(classifier, method));
        Assert.True(Classify(classifier,
            method.Definition,
            new SingleTypeRepository(method.Definition.DeclaringType, method.DeclaringType)));
    }

    [Theory]
    [InlineData("System.Runtime.InteropServices.NativeMemory", "Free")]
    [InlineData("System.Runtime.InteropServices.WebAssembly.CanonicalAbi", "Free")]
    [InlineData("Example.NativeMemory", "Alloc")]
    public void ClassifyRejectsNonAllocatingAndUnrelatedMethods(
        string typeName,
        string methodName)
    {
        var classifier = new RuntimeAllocationSafepointClassifier();
        var method = CreateMethod(typeName, methodName);

        Assert.False(Classify(classifier, method));
        Assert.False(Classify(classifier,
            method.Definition,
            new SingleTypeRepository(method.Definition.DeclaringType, method.DeclaringType)));
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
            method.DeclaringType);

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

    private sealed class SingleTypeRepository(
        EntityKey key,
        CliTypeIdentity identity) : ITypeRepository
    {
        public TypeDefinitionModel GetTypeDefinition(EntityKey requestedKey)
        {
            Assert.Equal(key, requestedKey);
            var separator = identity.FullName!.LastIndexOf('.');
            return new TypeDefinitionModel(
                key,
                identity.FullName[..separator],
                identity.FullName[(separator + 1)..],
                false,
                ImmutableArray<EntityKey>.Empty,
                ImmutableArray<EntityKey>.Empty);
        }
    }
}
