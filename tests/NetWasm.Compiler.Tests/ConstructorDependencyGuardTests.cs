using System.Reflection;
using System.Runtime.CompilerServices;

namespace NetWasm.Compiler.Tests;

// Every collaborator a compiler actor takes is required: its constructor rejects
// each missing one by name. A placeholder stands in for each other parameter, so
// each guard is reached in turn.
public sealed class ConstructorDependencyGuardTests
{
    private static readonly string[] Actors =
    [
        "NetWasm.Compiler.Analysis.AddressTakenValues.LocalAddressDereferenceFolder",
        "NetWasm.Compiler.Analysis.AddressTakenValues.StructLocalScalarReplacer",
        "NetWasm.Compiler.Analysis.Allocations.NonEscapingAllocationReplacer",
        "NetWasm.Compiler.Analysis.Arrays.RepeatedArrayAccessEliminator",
        "NetWasm.Compiler.Analysis.BeforeFieldInitEntryInitializer",
        "NetWasm.Compiler.Analysis.ConditionalStoreMerger",
        "NetWasm.Compiler.Analysis.Constants.ConstantCallEvaluator",
        "NetWasm.Compiler.Analysis.Constants.ConstantTypeMaterializationRewriter",
        "NetWasm.Compiler.Analysis.Constants.DeadJumpRewriter",
        "NetWasm.Compiler.Analysis.Constants.ExactObjectTypeRewriter",
        "NetWasm.Compiler.Analysis.Constants.KnownSizeRewriter",
        "NetWasm.Compiler.Analysis.Constants.ReducingReachabilityClosureBuilder",
        "NetWasm.Compiler.Analysis.DefaultComparerCallRewriter",
        "NetWasm.Compiler.Analysis.Devirtualization.ExactReceiverDevirtualizer",
        "NetWasm.Compiler.Analysis.EnumEqualsCallRewriter",
        "NetWasm.Compiler.Analysis.FieldLoadReuser",
        "NetWasm.Compiler.Analysis.Loops.ArrayLoopIdiomRewriter",
        "NetWasm.Compiler.Analysis.Loops.ElementwiseLoopVectorizer",
        "NetWasm.Compiler.Analysis.Loops.IdempotentLoopCollapser",
        "NetWasm.Compiler.Analysis.Loops.LoopInvariantCallHoister",
        "NetWasm.Compiler.Analysis.Loops.LoopStoreForwarder",
        "NetWasm.Compiler.Analysis.Loops.ReductionLoopRewriter",
        "NetWasm.Compiler.Analysis.PureCallReuser",
        "NetWasm.Compiler.Analysis.RvaSpanLocalEliminator",
        "NetWasm.Compiler.Analysis.StackValueConsumerFinder",
        "NetWasm.Compiler.Analysis.StaticArrays.ValueConstructorPurityClassifier",
        "NetWasm.Compiler.ComponentModel.Raw.RawCoreRuntimeImportDeclarationBuilder",
        "NetWasm.Compiler.EntryPoints.CompilationExportResolver",
        "NetWasm.Compiler.Layout.TypeObjectDataBuilder",
    ];

    public static TheoryData<string, int> MissingDependencies()
    {
        var data = new TheoryData<string, int>();
        foreach (var name in Actors)
        {
            var constructor = Constructor(name);
            var parameters = constructor.GetParameters();
            for (var index = 0; index < parameters.Length; index++)
            {
                if (!parameters[index].ParameterType.IsValueType)
                    data.Add(name, index);
            }
        }
        return data;
    }

    [Theory]
    [MemberData(nameof(MissingDependencies))]
    public void ConstructorsRejectEveryMissingDependency(string actor, int missing)
    {
        var constructor = Constructor(actor);
        var parameters = constructor.GetParameters();
        var arguments = parameters.Select(static parameter => Placeholder(parameter.ParameterType)).ToArray();
        arguments[missing] = null;

        var exception = Assert.Throws<TargetInvocationException>(() => constructor.Invoke(arguments));

        var guard = Assert.IsType<ArgumentNullException>(exception.InnerException);
        Assert.Equal(parameters[missing].Name, guard.ParamName);
    }

    private static ConstructorInfo Constructor(string name)
    {
        var type = typeof(INetWasmCompiler).Assembly.GetType(name, throwOnError: true)!;
        return Assert.Single(type.GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance));
    }

    private static object? Placeholder(Type type) =>
        type.IsValueType ? Activator.CreateInstance(type)
        // A sequence of collaborators holds one.
        : type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IEnumerable<>) ? Sequence(type.GetGenericArguments()[0])
        : type.IsInterface ? DispatchProxy.Create(type, typeof(UnusedDependencyProxy))
        : type == typeof(string) ? string.Empty
        : type.IsArray ? Array.CreateInstance(type.GetElementType()!, 0)
        : RuntimeHelpers.GetUninitializedObject(type);

    private static Array Sequence(Type element)
    {
        var sequence = Array.CreateInstance(element, 1);
        sequence.SetValue(Placeholder(element), 0);
        return sequence;
    }

#pragma warning disable CA1852 // DispatchProxy requires an inheritable proxy base type.
    private class UnusedDependencyProxy : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) =>
            throw new InvalidOperationException();
    }
#pragma warning restore CA1852
}
