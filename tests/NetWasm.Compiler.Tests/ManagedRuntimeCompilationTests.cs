// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Immutable;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Metadata;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

using static NetWasm.Compiler.Tests.CompilerTestSupport;

public sealed class ManagedRuntimeCompilationTests
{
    [Fact]
    public void CompilerDiscoversAndLowersCheckedOverflowAndInvalidCastPaths()
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSource(
            "CheckedCastFixture",
            """
            namespace CheckedCastFixture;

            public class Target
            {
            }

            public sealed class Other
            {
            }

            public static class EntryPoint
            {
                public static int Run(int input)
                {
                    object value = input == 0 ? new Target() : new Other();
                    try
                    {
                        Target target = (Target)value;
                        return checked(input + 2147483647);
                    }
                    catch (System.InvalidCastException)
                    {
                        return 1;
                    }
                    catch (System.OverflowException)
                    {
                        return 2;
                    }
                }
            }
            """);
        var result = NetWasmCompiler.Compile(new CompilerOptions(
            assembly,
            [assets.CoreLib],
            "CheckedCastFixture.EntryPoint",
            "Run",
            []));

        Assert.Contains(ManagedExceptionKind.InvalidCast, result.Program.ImplicitExceptions);
        Assert.Contains(ManagedExceptionKind.Overflow, result.Program.ImplicitExceptions);
        Assert.Contains(
            result.Program.Methods.Values
                .SelectMany(method => method.Body.Instructions),
            instruction => instruction.Operation == CilOperation.CastClass);
        Assert.Contains(
            result.Program.Methods.Values
                .SelectMany(method => method.Body.Instructions),
            instruction => instruction.Operation == CilOperation.AddChecked);
        Assert.True(result.ApplicationModule.AsSpan().IndexOf("is_assignable"u8) >= 0);
    }

    [Fact]
    public void AnalyzerPreservesExactClosedFinalizersForAllocatedTypes()
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSource(
            "FinalizerFixture",
            """
            namespace FinalizerFixture;

            public class GenericFinalizable<T>
            {
                public static int Runs;

                ~GenericFinalizable()
                {
                    Runs = Runs + 1;
                }
            }

            public sealed class DerivedFinalizable : GenericFinalizable<int>
            {
            }

            public sealed class NeverAllocated
            {
                ~NeverAllocated()
                {
                }
            }

            public static class EntryPoint
            {
                public static int Run(int input)
                {
                    _ = new GenericFinalizable<long>();
                    _ = new DerivedFinalizable();
                    return input;
                }
            }
            """);
        using var compilation = MetadataCompilationTestFactory.Load(
            assembly,
            [assets.CoreLib]);
        var snapshot = compilation.Snapshot;
        var types = MetadataCompilationTestActors.Types(snapshot);
        var fields = MetadataCompilationTestActors.Fields(snapshot);
        var methods = MetadataCompilationTestActors.Methods(snapshot);
        var symbols = MetadataCompilationTestActors.Symbols(snapshot);
        var typeFinder = MetadataCompilationTestActors.TypeFinder(snapshot);
        var typeDefinitions = MetadataCompilationTestActors.TypeDefinitions(snapshot);
        var typeIdentities = MetadataCompilationTestActors.TypeIdentities(snapshot);
        var methodInstances = MetadataCompilationTestActors.MethodInstances(snapshot);
        var baseTypes = MetadataCompilationTestActors.EntityBaseTypes(snapshot);
        var baseTypeIdentities = MetadataCompilationTestActors.BaseTypeIdentities(snapshot);
        var identityBaseTypes = MetadataCompilationTestActors.IdentityBaseTypes(snapshot);
        var implementedInterfaces = MetadataCompilationTestActors.ImplementedInterfaces(snapshot);
        var methodImplementations = MetadataCompilationTestActors.MethodImplementations(snapshot);
        var methodBodies = MetadataCompilationTestActors.MethodBodies(snapshot);
        var methodFinder = MetadataCompilationTestActors.MethodFinder(snapshot);
        var entry = methodFinder.FindMethod(
            snapshot.EntryAssemblyIdentity,
            "FinalizerFixture.EntryPoint",
            "Run");
        var program = CompilerTestSupport.CreateAnalyzer(
            snapshot,
            types,
            fields,
            methods,
            typeFinder,
            typeDefinitions,
            typeIdentities,
            baseTypeIdentities,
            implementedInterfaces,
            methodBodies,
            methodInstances,
            methodImplementations,
            symbols).Analyze(entry, []);
        var layouts = CompilerTestSupport.CreateLayouts(
            snapshot,
            types,
            fields,
            typeFinder,
            typeDefinitions,
            typeIdentities,
            baseTypes,
            identityBaseTypes,
            program);
        var genericFinalizable = snapshot.Types
            .Single(candidate => candidate.FullName == "FinalizerFixture.GenericFinalizable`1");
        var derivedFinalizable = snapshot.Types
            .Single(candidate => candidate.FullName == "FinalizerFixture.DerivedFinalizable");
        var neverAllocated = snapshot.Types
            .Single(candidate => candidate.FullName == "FinalizerFixture.NeverAllocated");
        var genericDefinition = typeIdentities.GetTypeIdentity(genericFinalizable.Key);
        var directIdentity = CliTypeIdentity.GenericInstantiation(
            genericDefinition,
            [CliTypeIdentity.Primitive("i8", CliValueKind.I8)]);
        var inheritedOwnerIdentity = CliTypeIdentity.GenericInstantiation(
            genericDefinition,
            [CliTypeIdentity.Primitive("i4", CliValueKind.I4)]);
        var derivedIdentity = typeIdentities.GetTypeIdentity(derivedFinalizable.Key);
        var neverAllocatedIdentity = typeIdentities.GetTypeIdentity(neverAllocated.Key);
        var directFinalizer = program.Finalizers[directIdentity];
        var inheritedFinalizer = program.Finalizers[derivedIdentity];

        Assert.Contains(directIdentity, program.ConstructedTypes);
        Assert.Contains(directIdentity, layouts.TypeLayouts.ConstructedObjects.Keys);
        Assert.Equal("Finalize", directFinalizer.Definition.Name);
        Assert.Equal(directIdentity, directFinalizer.DeclaringType);
        Assert.Equal("Finalize", inheritedFinalizer.Definition.Name);
        Assert.Equal(inheritedOwnerIdentity, inheritedFinalizer.DeclaringType);
        Assert.DoesNotContain(neverAllocatedIdentity, program.Finalizers.Keys);
        Assert.Equal(
            directFinalizer,
            layouts.ConstructedTypeDescriptors.Single(descriptor =>
                    descriptor.Type.Equals(directIdentity))
                .Finalizer);
        Assert.Equal(
            inheritedFinalizer,
            layouts.TypeDescriptors.Single(descriptor => descriptor.Type == derivedFinalizable.Key)
                .Finalizer);
    }

    [Fact]
    public void CompilerEmitsTheManagedRuntimeSemanticSurface()
    {
        using var assets = TestAssets.Create();
        var source = File.ReadAllText(Path.Combine(
            assets.Root,
            "tests",
            "end-to-end",
            "managed-runtime",
            "fixtures",
            "ManagedHeapWorkload.cs"));
        var assembly = assets.CompileSource("ManagedRuntimeProductionFixture", source);
        var type = "NetWasm.Fixtures.ManagedHeap.EntryPoint";
        string[] methods =
        [
            "ValidateRoots",
            "GetNodes",
            "ReadNodes",
            "ClearRoots",
            "RunTypedCatch",
            "RunRootedUnwind",
            "RunSequentialHandlers",
            "RunMultipleLeaves",
            "RunAllocationStress",
            "CatchNullField",
            "CatchArrayBounds",
            "CatchDivision",
            "CatchArrayTypeMismatch",
            "StoreNullInCovariantArray",
            "CatchStringBounds",
            "CatchNegativeArrayLength",
            "CatchNegativeStringLength",
            "CatchThrowNull",
            "CatchOutOfMemory",
            "CatchCheckedAdd",
            "CatchCheckedSubtract",
            "CatchCheckedMultiply",
            "CatchInvalidCast",
            "CastNull",
            "RunFinallyAcrossCall",
            "RunRethrow",
            "RunCleanupReplacement",
            "RunNestedFinally",
            "ThrowUnhandled",
            "CreateDerivedFinalizable",
            "CreateSuppressedFinalizable",
            "CreateResurrectedFinalizable",
            "ReleaseResurrectedFinalizable",
            "CreateReregisteredFinalizable",
            "ReleaseReregisteredFinalizable",
        ];
        var exports = methods
            .Select(method => new RequestedExport(
                method.ToLowerInvariant(),
                type,
                method))
            .ToImmutableArray();

        var result = NetWasmCompiler.Compile(new CompilerOptions(
            assembly,
            [assets.CoreLib],
            type,
            "Run",
            exports));

        Assert.Equal(exports.Length, result.Program.Exports.Length);
        Assert.NotEmpty(result.Program.Finalizers);
        Assert.Contains(ManagedExceptionKind.InvalidCast, result.Program.ImplicitExceptions);
        Assert.Contains(ManagedExceptionKind.ArrayTypeMismatch, result.Program.ImplicitExceptions);
        Assert.True(result.ApplicationModule.AsSpan().IndexOf("exception_frame_enter"u8) >= 0);
        Assert.True(result.ApplicationModule.AsSpan().IndexOf("finalizer_safepoint"u8) >= 0);
        Assert.True(result.ApplicationModule.AsSpan().IndexOf("netwasm.finalize"u8) >= 0);
    }

    [Fact]
    public void CompilerExecutesAFilterUsingCapturedScalarState()
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSource(
            "FilterFixture",
            """
            namespace FilterFixture;

            public static class EntryPoint
            {
                public static int Run(int input)
                {
                    try
                    {
                        throw new System.Exception();
                    }
                    catch (System.Exception) when (input != 0)
                    {
                        return 1;
                    }
                    catch (System.Exception)
                    {
                        return 0;
                    }
                }
            }
            """);
        CompilerOptions options = new(
            assembly,
            [assets.CoreLib],
            "FilterFixture.EntryPoint",
            "Run",
            []);

        var first = NetWasmCompiler.Compile(options);
        var second = NetWasmCompiler.Compile(options);

        Assert.Equal(first.ApplicationModule, second.ApplicationModule);
        Assert.Equal(1, ExecuteWithNode(first.ApplicationModule, assets.Directory, 41));
        Assert.Equal(0, ExecuteWithNode(first.ApplicationModule, assets.Directory, 0));
    }

    [Fact]
    public void CompilerPreservesIdiomaticExceptionFilterSemantics()
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSource(
            "FilterSemanticsFixture",
            """
            namespace FilterSemanticsFixture;

            public sealed class SpikeException : System.Exception
            {
                public int Value;

                public SpikeException(int value)
                {
                    Value = value;
                }
            }

            public sealed class OtherException : System.Exception
            {
            }

            public static class EntryPoint
            {
                private static int Trace;
                private static SpikeException? Primary;

                public static int Run(int scenario)
                {
                    Trace = 0;
                    return scenario == 1 ? CapturesAndIdentity()
                        : scenario == 2 ? RejectedTypedFilter()
                        : scenario == 3 ? SearchBeforeFinally()
                        : scenario == 4 ? LexicalFilterOrder()
                        : scenario == 5 ? ThrowingFilterIsFalse()
                        : scenario == 6 ? InternallyHandledPredicateException()
                        : scenario == 7 ? ImplicitPredicateExceptionIsFalse()
                        : scenario == 8 ? UnrelatedTypeSkipsPredicate()
                        : RecursiveFilteredThrow();
                }

                private static int CapturesAndIdentity()
                {
                    int expected = 41;
                    object captured = new object();
                    var exception = new SpikeException(expected);
                    Primary = exception;
                    try
                    {
                        throw exception;
                    }
                    catch (SpikeException candidate) when (
                        CheckCaptures(candidate, expected, captured))
                    {
                        return object.ReferenceEquals(candidate, Primary) ? 111 : -1;
                    }
                    catch (System.Exception)
                    {
                        return 0;
                    }
                }

                private static bool CheckCaptures(
                    SpikeException candidate,
                    int expected,
                    object captured)
                {
                    return candidate.Value == expected && captured != null;
                }

                private static int RejectedTypedFilter()
                {
                    try
                    {
                        throw new SpikeException(2);
                    }
                    catch (SpikeException) when (false)
                    {
                        return -1;
                    }
                    catch (SpikeException candidate)
                    {
                        return candidate.Value;
                    }
                }

                private static int SearchBeforeFinally()
                {
                    try
                    {
                        try
                        {
                            throw new SpikeException(3);
                        }
                        finally
                        {
                            Trace = Trace * 10 + 2;
                        }
                    }
                    catch (SpikeException candidate) when (Record(1, candidate.Value == 3))
                    {
                        Trace = Trace * 10 + 3;
                        return Trace;
                    }
                }

                private static int LexicalFilterOrder()
                {
                    try
                    {
                        throw new SpikeException(4);
                    }
                    catch (SpikeException) when (Record(1, false))
                    {
                        return -1;
                    }
                    catch (SpikeException) when (Record(2, true))
                    {
                        Trace = Trace * 10 + 3;
                        return Trace;
                    }
                }

                private static int ThrowingFilterIsFalse()
                {
                    var exception = new SpikeException(42);
                    Primary = exception;
                    try
                    {
                        throw exception;
                    }
                    catch (SpikeException candidate) when (ThrowFromPredicate(candidate))
                    {
                        return -1;
                    }
                    catch (SpikeException candidate)
                    {
                        return (object.ReferenceEquals(candidate, Primary) ? 100 : 0) +
                            Trace * 10 + candidate.Value;
                    }
                }

                private static bool ThrowFromPredicate(SpikeException candidate)
                {
                    Trace = Trace + 1;
                    throw new OtherException();
                }

                private static int InternallyHandledPredicateException()
                {
                    try
                    {
                        throw new SpikeException(6);
                    }
                    catch (SpikeException) when (HandleInsidePredicate())
                    {
                        Trace = Trace * 10 + 3;
                        return Trace;
                    }
                }

                private static bool HandleInsidePredicate()
                {
                    Trace = Trace * 10 + 1;
                    try
                    {
                        throw new OtherException();
                    }
                    catch (OtherException)
                    {
                        Trace = Trace * 10 + 2;
                        return true;
                    }
                }

                private static int ImplicitPredicateExceptionIsFalse()
                {
                    try
                    {
                        throw new SpikeException(7);
                    }
                    catch (SpikeException) when (DivideByZero())
                    {
                        return -1;
                    }
                    catch (SpikeException candidate)
                    {
                        return Trace * 100 + candidate.Value;
                    }
                }

                private static int UnrelatedTypeSkipsPredicate()
                {
                    try
                    {
                        throw new OtherException();
                    }
                    catch (SpikeException) when (Record(1, true))
                    {
                        return -1;
                    }
                    catch (OtherException)
                    {
                        return Trace == 0 ? 8 : -2;
                    }
                }

                private static int RecursiveFilteredThrow()
                {
                    try
                    {
                        throw new SpikeException(9);
                    }
                    catch (SpikeException) when (NestedFilteredPredicate())
                    {
                        Trace = Trace * 10 + 4;
                        return Trace;
                    }
                }

                private static bool NestedFilteredPredicate()
                {
                    Trace = Trace * 10 + 1;
                    try
                    {
                        throw new OtherException();
                    }
                    catch (OtherException) when (Record(2, true))
                    {
                        Trace = Trace * 10 + 3;
                    }
                    return true;
                }

                private static bool DivideByZero()
                {
                    Trace = Trace + 1;
                    int zero = Trace - 1;
                    return 1 / zero != 0;
                }

                private static bool Record(int value, bool result)
                {
                    Trace = Trace * 10 + value;
                    return result;
                }
            }
            """);
        var result = NetWasmCompiler.Compile(new CompilerOptions(
            assembly,
            [assets.CoreLib],
            "FilterSemanticsFixture.EntryPoint",
            "Run",
            []));

        int[] expected = [111, 2, 123, 123, 152, 123, 107, 8, 1234];
        for (var scenario = 1; scenario <= expected.Length; scenario++)
        {
            Assert.Equal(
                expected[scenario - 1],
                ExecuteWithNode(result.ApplicationModule, assets.Directory, scenario));
        }
    }
}
