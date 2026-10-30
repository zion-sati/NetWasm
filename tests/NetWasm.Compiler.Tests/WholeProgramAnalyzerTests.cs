using Microsoft.Extensions.DependencyInjection;
using NetWasm.Compiler;
using NetWasm.Compiler.Analysis;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.ControlFlow;
using NetWasm.Compiler.ComponentModel;
using NetWasm.Compiler.ComponentModel.Optimization;
using NetWasm.Compiler.Interop;
using NetWasm.Compiler.Metadata;
using NetWasm.TestInfrastructure;
using System.Text;

namespace NetWasm.Compiler.Tests;

public sealed class WholeProgramAnalyzerTests
{
    [Fact]
    public void VoidDelegateInvocationRetainsItsDescriptorWithoutABoxedVoidDispatchTarget()
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSource("VoidDelegateReachability", """
            using System;
            public static class EntryPoint
            {
                public static int Run(int input)
                {
                    Action action = () => { };
                    action.DynamicInvoke(null);
                    return input;
                }
            }
            """);
        using var compilation = MetadataCompilationTestFactory.Load(assembly, [assets.CoreLib]);
        var program = Analyze(compilation, "EntryPoint", "Run");

        Assert.NotNull(program.MemberExecution.DelegateInvocation);
        Assert.Contains(program.MemberExecution.DelegateInvocation.Methods.Values,
            method => method.Signature.ReturnType == CliValueKind.Void);
        Assert.DoesNotContain(program.DispatchCallSites.Values.SelectMany(site => site.Targets),
            target => target.ReceiverType.StackKind == CliValueKind.Void);
    }

    [Fact]
    public void IntegerConsoleOutputInitializesOnlyTheSmallNumberCache()
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileOptimizedSource("ConsoleReachability", """
            namespace ConsoleReachability;
            public static class EntryPoint
            {
                public static int Run(int input) { System.Console.WriteLine(42); return input; }
            }
            """);
        using var compilation = MetadataCompilationTestFactory.Load(assembly, [assets.CoreLib]);
        var program = Analyze(compilation, "ConsoleReachability.EntryPoint", "Run");
        var number = compilation.Snapshot.Types.Single(type => type.FullName == "System.Number");
        var holder = compilation.Snapshot.Types.Single(type => type.FullName == "System.Number+SmallNumberCache");

        // Number keeps no initializer of its own: its formatting tables are
        // switches, and the cache lives in a holder class that only small
        // integer formatting initializes.
        Assert.DoesNotContain(program.Methods.Values,
            body => body.Method.Definition.DeclaringType == number.Key && body.Method.Definition.Name == ".cctor");
        Assert.True(holder.IsBeforeFieldInit);
        var initializer = Assert.Single(program.Methods.Values,
            body => body.Method.Definition.DeclaringType == holder.Key && body.Method.Definition.Name == ".cctor");
        var original = MetadataCompilationTestActors.MethodBodies(compilation.Snapshot).ReadMethodBody(initializer.Method);
        var fields = MetadataCompilationTestActors.Fields(compilation.Snapshot);
        var cache = holder.Fields.Select(fields.GetField).Single(field => field.Name == "Value");
        Assert.Contains(cache.Key, program.Fields);
        Assert.Contains(initializer.Body.Instructions, instruction =>
            instruction.Operation == CilOperation.StoreStaticField &&
            instruction.Operand is CilOperand.Entity field && field.Key == cache.Key);
        // The holder's initializer allocates only the cache. The floating-point
        // tables live in holder classes that only floating-point formatting and
        // math initialize, so integer output never runs them (pay-for-play
        // formatting). StaticArrayPopulationCompilationTests covers dropping
        // unobserved population.
        Assert.Equal(1, original.Instructions.Count(instruction => instruction.Operation == CilOperation.NewArray));
        Assert.Equal(1, initializer.Body.Instructions.Count(instruction => instruction.Operation == CilOperation.NewArray));
        Assert.DoesNotContain(original.Instructions, instruction =>
            instruction.Operation is CilOperation.StoreArrayElement or CilOperation.NewObject);
        // The IEEE decimal arithmetic and its coefficient tables, read-only
        // spans of static data, belong to decimal floating-point formatting.
        var decimalArithmetic = compilation.Snapshot.Types
            .Where(type => type.FullName.StartsWith("System.Number+DiyFp128", StringComparison.Ordinal))
            .Select(type => type.Key)
            .ToHashSet();
        Assert.NotEmpty(decimalArithmetic);
        Assert.DoesNotContain(program.Methods.Values, body => decimalArithmetic.Contains(body.Method.Definition.DeclaringType));
    }

    [Fact]
    public void ExpressionMethodExecutionRetainsBodiesAndStaticInitializationOnlyWhenDemanded()
    {
        using var assets = TestAssets.Create();
        var descriptorAssembly = assets.CompileSource(
            "ExpressionDescriptorOnlyReachability",
            ExpressionSource(compile: false));
        using var descriptorCompilation = MetadataCompilationTestFactory.Load(
            descriptorAssembly,
            [assets.CoreLib]);
        var descriptorProgram = Analyze(
            descriptorCompilation,
            "ExpressionReachability.EntryPoint",
            "Run");
        var descriptorTarget = descriptorCompilation.Snapshot.Types.Single(type =>
            type.FullName == "ExpressionReachability.Target");

        Assert.Contains(
            descriptorProgram.MethodDescriptors.Values,
            method => method.Definition.DeclaringType == descriptorTarget.Key &&
                method.Definition.Name == "Add");
        Assert.DoesNotContain(
            descriptorProgram.MethodInstances.Values,
            method => method.Definition.DeclaringType == descriptorTarget.Key &&
                method.Definition.Name is "Add" or ".cctor");
        Assert.Empty(descriptorProgram.MemberExecution.Methods);

        var executableAssembly = assets.CompileSource(
            "ExpressionExecutableReachability",
            ExpressionSource(compile: true));
        using var executableCompilation = MetadataCompilationTestFactory.Load(
            executableAssembly,
            [assets.CoreLib]);
        var executableProgram = Analyze(
            executableCompilation,
            "ExpressionReachability.EntryPoint",
            "Run");
        var executableTarget = executableCompilation.Snapshot.Types.Single(type =>
            type.FullName == "ExpressionReachability.Target");

        Assert.Contains(
            executableProgram.MemberExecution.Methods.Values,
            execution =>
                execution.Descriptor.Definition.DeclaringType == executableTarget.Key &&
                execution.Descriptor.Definition.Name == "Add");
        Assert.Contains(
            executableProgram.MethodInstances.Values,
            method => method.Definition.DeclaringType == executableTarget.Key &&
                method.Definition.Name == "Add");
        Assert.Contains(
            executableProgram.MethodInstances.Values,
            method => method.Definition.DeclaringType == executableTarget.Key &&
                method.Definition.Name == ".cctor");

        static string ExpressionSource(bool compile) => $$"""
            using System;
            using System.Linq.Expressions;

            namespace ExpressionReachability;

            public static class Target
            {
                public static int State;

                static Target() => State = 1;

                public static int Add(int value, int delta) =>
                    value + delta + State;
            }

            public static class EntryPoint
            {
                public static int Run(int input)
                {
                    Expression<Func<int, int>> tree =
                        value => Target.Add(value, 1);
                    {{(compile ? "_ = tree.Compile();" : "_ = tree;")}}
                    return input;
                }
            }
            """;
    }

    [Fact]
    public void AnalyzerDoesNotImposeAConstructedIdentityCeiling()
    {
        using var assets = TestAssets.Create();
        var source = new StringBuilder(
            "namespace ConstructedIdentityCapacity; public sealed class Box<T> {} ");
        for (var index = 0; index < 1_030; index++)
        {
            source.Append("public sealed class C");
            source.Append(index);
            source.Append(" {} ");
        }
        source.Append("public static class EntryPoint { public static int Run(int input) { " +
            "var values = new object[] {");
        for (var index = 0; index < 1_030; index++)
        {
            source.Append("new Box<C");
            source.Append(index);
            source.Append(">(),");
        }
        source.Append("}; return input + values.Length; } }");
        var assembly = assets.CompileSource("ConstructedIdentityCapacity", source.ToString());
        using var compilation = MetadataCompilationTestFactory.Load(assembly, [assets.CoreLib]);
        var program = Analyze(
            compilation,
            "ConstructedIdentityCapacity.EntryPoint",
            "Run");

        Assert.True(program.ConstructedTypes.Count > 1_024);
    }

    [Fact]
    public void AnalyzerValidatesFoundationalValueTypeCil()
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSource(
            "ValueAnalyzerFixture",
            """
            namespace ValueAnalyzerFixture;

            public struct Pair
            {
                public int Left;
                public int Right;

                public Pair(int left, int right)
                {
                    Left = left;
                    Right = right;
                }

                public int Sum() => Left + Right;
            }

            public static class EntryPoint
            {
                public static int Run(int input)
                {
                    Pair value = new Pair(input, input + 1);
                    return value.Sum();
                }
            }
            """);
        using var compilation = MetadataCompilationTestFactory.Load(
            assembly,
            [assets.CoreLib]);
        var program = Analyze(compilation, "ValueAnalyzerFixture.EntryPoint", "Run");

        Assert.Contains(
            program.Methods.Values,
            method => method.Method.Definition.Name == ".ctor" &&
                      IsValueType(
                          compilation,
                          method.Method.Definition.DeclaringType));
        Assert.Contains(
            program.Methods.Values,
            method => method.Method.Definition.Name == "Sum");
    }

    [Fact]
    public void AnalyzerDiscoversAndSubstitutesClosedGenericMethods()
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSource(
            "GenericAnalyzerFixture",
            """
            namespace GenericAnalyzerFixture;

            public static class EntryPoint
            {
                public static T Identity<T>(T value)
                {
                    T copy = value;
                    return copy;
                }

                public static int Run(int input) => Identity(input);
            }
            """);
        using var compilation = MetadataCompilationTestFactory.Load(
            assembly,
            [assets.CoreLib]);
        var program = Analyze(compilation, "GenericAnalyzerFixture.EntryPoint", "Run");

        var identity = Assert.Single(
            program.MethodInstances,
            pair => pair.Value.Definition.Name == "Identity");
        Assert.True(identity.Value.IsConstructed);
        Assert.Equal("primitive:i4", Assert.Single(identity.Value.MethodArguments).CanonicalName);
        var managed = program.ConstructedMethods[identity.Key];
        Assert.Contains(
            managed.Body.LocalSignatureTypes,
            type => type.CanonicalName == "primitive:i4");
        Assert.Empty(program.ConstructedTypes);
    }

    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void TypeFactsPreserveClosedGenericBasesWithoutPhysicalOpenLayouts(
        WasmTarget target)
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSource(
            "TypeFactsGenericClosure",
            """
            namespace TypeFactsGenericClosure;

            public class Base<T>
            {
                public T Value = default!;
            }

            public sealed class Wrapper<T>
            {
                public T Value = default!;
            }

            public sealed class Derived<T> : Base<Wrapper<T>>
            {
            }

            public static class EntryPoint
            {
                public static int Run(int input)
                {
                    var value = new Derived<int>();
                    return value.GetType().ToString().Length + input;
                }
            }
            """);
        var result = NetWasmCompiler.Compile(new CompilerOptions(
            assembly,
            [assets.CoreLib],
            "TypeFactsGenericClosure.EntryPoint",
            "Run",
            [],
            Target: target));
        using var compilation = MetadataCompilationTestFactory.Load(
            assembly,
            [assets.CoreLib]);
        var definitions = compilation.Snapshot.Types;
        var identities = MetadataCompilationTestActors.TypeIdentities(compilation.Snapshot);
        var baseDefinition = definitions.Single(type =>
            type.FullName == "TypeFactsGenericClosure.Base`1");
        var wrapperDefinition = definitions.Single(type =>
            type.FullName == "TypeFactsGenericClosure.Wrapper`1");
        var derivedDefinition = definitions.Single(type =>
            type.FullName == "TypeFactsGenericClosure.Derived`1");
        var intType = CliTypeIdentity.Primitive("i4", CliValueKind.I4);
        var wrapperOfInt = CliTypeIdentity.GenericInstantiation(
            identities.GetTypeIdentity(wrapperDefinition.Key),
            [intType]);
        var baseOfWrapperOfInt = CliTypeIdentity.GenericInstantiation(
            identities.GetTypeIdentity(baseDefinition.Key),
            [wrapperOfInt]);
        var derivedOfInt = CliTypeIdentity.GenericInstantiation(
            identities.GetTypeIdentity(derivedDefinition.Key),
            [intType]);

        Assert.Contains(derivedOfInt, result.Program.ConstructedTypes);
        Assert.Contains(wrapperOfInt, result.Program.ConstructedTypes);
        Assert.Contains(baseOfWrapperOfInt, result.Program.ConstructedTypes);
        Assert.DoesNotContain(
            result.Program.ConstructedTypes,
            type => type.ContainsGenericParameters);
    }

    [Fact]
    public void OpenGenericTypeTokensRemainDescriptorOnly()
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSource(
            "OpenGenericTypeToken",
            """
            using System;

            namespace OpenGenericTypeToken;

            public sealed class Open<T>
            {
            }

            public static class EntryPoint
            {
                public static int Run(int input) =>
                    typeof(Open<>).Equals(null) ? 0 : input;
            }
            """);
        using var compilation = MetadataCompilationTestFactory.Load(
            assembly,
            [assets.CoreLib]);
        var definition = compilation.Snapshot.Types.Single(type =>
            type.FullName == "OpenGenericTypeToken.Open`1");
        var program = Analyze(compilation, "OpenGenericTypeToken.EntryPoint", "Run");

        Assert.Contains(definition.Key, program.RuntimeTypeDefinitions);
        Assert.DoesNotContain(
            program.ConstructedTypes,
            type => type.ContainsGenericParameters);
    }

    private static ReachableProgram Analyze(
        IMetadataCompilationLease compilation,
        string entryType,
        string entryMethod)
    {
        using var services = new ServiceCollection()
            .AddCompilerControlFlow()
            .AddNetWasmCompiler()
            .BuildServiceProvider();
        var snapshot = compilation.Snapshot;
        var types = MetadataCompilationTestActors.Types(snapshot);
        var fields = MetadataCompilationTestActors.Fields(snapshot);
        var methods = MetadataCompilationTestActors.Methods(snapshot);
        var symbols = MetadataCompilationTestActors.Symbols(snapshot);
        var typeFinder = MetadataCompilationTestActors.TypeFinder(snapshot);
        var typeDefinitions = MetadataCompilationTestActors.TypeDefinitions(snapshot);
        var identities = MetadataCompilationTestActors.TypeIdentities(snapshot);
        var methodInstances = MetadataCompilationTestActors.MethodInstances(snapshot);
        var baseTypeIdentities = MetadataCompilationTestActors.BaseTypeIdentities(snapshot);
        var implementedInterfaces = MetadataCompilationTestActors.ImplementedInterfaces(snapshot);
        var methodImplementations = MetadataCompilationTestActors.MethodImplementations(snapshot);
        var methodBodies = MetadataCompilationTestActors.MethodBodies(snapshot);
        var methodFinder = MetadataCompilationTestActors.MethodFinder(snapshot);
        var baseTypes = new BaseTypeResolverFactory().Create(
            typeFinder,
            identities,
            baseTypeIdentities);
        var asyncBindings = new JavaScriptAsyncBindingResolverFactory().Create(
            typeFinder,
            typeDefinitions,
            identities,
            fields,
            methods,
            methodInstances,
            symbols,
            PlatformWorldCapabilities.None);
        var analyzer = services.GetRequiredService<IWholeProgramAnalyzerFactory>().Create(
            snapshot,
            types,
            fields,
            methods,
            typeFinder,
            typeDefinitions,
            identities,
            baseTypeIdentities,
            implementedInterfaces,
            methodBodies,
            methodInstances,
            methodImplementations,
            symbols,
            asyncBindings.Resolve,
            new RuntimeIntrinsicRegistryFactory().Create(symbols, snapshot.Methods),
            new WasmOptimizationSettingsResolver().Resolve(
                FinalWasmOptimization.Oz),
            PlatformWorldCapabilities.None);
        return analyzer.Analyze(
            methodFinder.FindMethod(snapshot.EntryAssemblyIdentity, entryType, entryMethod),
            []);
    }

    private static bool IsValueType(
        IMetadataCompilationLease compilation,
        EntityKey type) => MetadataCompilationTestActors.Types(compilation.Snapshot)
            .GetTypeDefinition(type).IsValueType;
}
