using System.Collections.Immutable;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.GarbageCollection;

namespace NetWasm.Compiler.GarbageCollection.Tests;

// Only a direct call to RuntimeMemberExecution.InvokeMethod allocates before it runs managed code.
public sealed class MemberExecutionAllocationCallClassifierTests
{
    private static readonly AssemblyIdentity Assembly = new("Classifier");
    private static readonly EntityKey TypeKey = new(Assembly, 0x02000001);
    private static readonly EntityKey MethodKey = new(Assembly, 0x06000001);

    [Theory]
    [InlineData("System.Runtime.CompilerServices.RuntimeMemberExecution", "InvokeMethod", true, CilOperation.Call, true)]
    [InlineData("System.Runtime.CompilerServices.RuntimeMemberExecution", "InvokeDelegate", true, CilOperation.Call, false)]
    [InlineData("System.Runtime.CompilerServices.RuntimeMemberExecution", "ReadField", true, CilOperation.Call, false)]
    [InlineData("Other.RuntimeMemberExecution", "InvokeMethod", true, CilOperation.Call, false)]
    [InlineData("System.Runtime.CompilerServices.RuntimeMemberExecution", "InvokeMethod", false, CilOperation.Call, false)]
    [InlineData("System.Runtime.CompilerServices.RuntimeMemberExecution", "InvokeMethod", true, CilOperation.NewObject, false)]
    public void ClassifiesOnlyInvokeMethod(string typeName, string methodName, bool isStatic, CilOperation operation,
        bool expected)
    {
        var program = new Program(typeName, methodName, isStatic);
        var classifier = new MemberExecutionAllocationCallClassifier(program, program);

        Assert.Equal(expected, classifier.Classify(
            new CilInstruction(0, 5, operation, new CilOperand.Entity(MethodKey))));
    }

    // A call names its method by definition or by instance; a call without a method operand is none.
    [Fact]
    public void ClassifiesAnInstantiatedCallAndNoOtherOperand()
    {
        var program = new Program("System.Runtime.CompilerServices.RuntimeMemberExecution", "InvokeMethod", true);
        var classifier = new MemberExecutionAllocationCallClassifier(program, program);
        var definition = program.GetMethod(MethodKey);
        var instance = new MethodInstanceModel(definition, CliTypeIdentity.FromDefinition(program.GetTypeDefinition(TypeKey)),
            [], definition.Signature);

        Assert.True(classifier.Classify(new CilInstruction(0, 5, CilOperation.Call, new CilOperand.MethodInstance(instance))));
        Assert.False(classifier.Classify(new CilInstruction(0, 5, CilOperation.Call, new CilOperand.None())));
    }

    [Fact]
    public void RejectsNullDependenciesAndInstruction()
    {
        var program = new Program("A.B", "C", true);
        Assert.Throws<ArgumentNullException>(() => new MemberExecutionAllocationCallClassifier(null!, program));
        Assert.Throws<ArgumentNullException>(() => new MemberExecutionAllocationCallClassifier(program, null!));
        Assert.Throws<ArgumentNullException>(() =>
            new MemberExecutionAllocationCallClassifier(program, program).Classify(null!));
    }

    private sealed class Program(string typeName, string methodName, bool isStatic) : ITypeRepository, IMethodRepository
    {
        public TypeDefinitionModel GetTypeDefinition(EntityKey key)
        {
            var separator = typeName.LastIndexOf('.');
            return new(key, typeName[..separator], typeName[(separator + 1)..], false,
                ImmutableArray<EntityKey>.Empty, ImmutableArray<EntityKey>.Empty);
        }

        public MethodDefinitionModel GetMethod(EntityKey key) => new(
            key,
            TypeKey,
            methodName,
            isStatic,
            MethodSignatureModel.Create(CliTypeIdentity.FromStackKind(CliValueKind.ManagedReference), []),
            1);
    }
}
