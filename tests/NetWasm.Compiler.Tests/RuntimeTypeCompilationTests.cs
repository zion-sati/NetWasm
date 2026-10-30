
using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

using static NetWasm.Compiler.Tests.CompilerTestSupport;

public sealed class RuntimeTypeCompilationTests
{
    [Fact]
    public void UnreachableGenericArgumentQueryDoesNotChangeHello42Output()
    {
        using var assets = TestAssets.Create();
        const string baselineSource =
            "public static class EntryPoint { public static int Run(int input) => input + 1; }";
        const string unusedSource =
            """
            public static class EntryPoint
            {
                public static int Run(int input) => input + 1;

                private static System.Type[] Unused<T>() =>
                    typeof(T).GetGenericArguments();
            }
            """;
        var output = assets.CompileOptimizedSource(
            "GenericArgumentsPayUse",
            baselineSource);
        var baselineAssembly = Path.Combine(
            assets.Directory,
            "GenericArgumentsPayUse.baseline.dll");
        File.Copy(output, baselineAssembly);
        var baseline = NetWasmCompiler.Compile(new(
            baselineAssembly,
            [assets.CoreLib],
            "EntryPoint",
            "Run",
            [],
            WasmTarget.Wasm32));
        var unusedAssembly = assets.CompileOptimizedSource(
            "GenericArgumentsPayUse",
            unusedSource);
        var unused = NetWasmCompiler.Compile(new(
            unusedAssembly,
            [assets.CoreLib],
            "EntryPoint",
            "Run",
            [],
            WasmTarget.Wasm32));

        Assert.Equal(baseline.ApplicationModule, unused.ApplicationModule);
        Assert.False(unused.Program.RequiresGenericArguments);
        Assert.Empty(unused.Layouts.StaticData.MetadataTypeDescriptors);
    }

    [Theory]
    [InlineData(false, WasmTarget.Wasm32)]
    [InlineData(false, WasmTarget.Wasm64)]
    [InlineData(true, WasmTarget.Wasm32)]
    [InlineData(true, WasmTarget.Wasm64)]
    public void CompilerExecutesBoundedRuntimeTypeFacts(
        bool optimized,
        WasmTarget target)
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSourceWithCompiler(
            "RuntimeTypeFactsFixture",
            """
            namespace RuntimeTypeFactsFixture;

            public interface IMarker { }
            public abstract class AbstractBase { }
            public class Base { }
            public sealed class Derived : Base, IMarker { }
            public struct Pair { public int Value; }
            public enum Mode : ushort { Ready = 1 }
            public sealed class Box<T> { }

            public static class EntryPoint
            {
                public static int Run(int input)
                {
                    int passed = 0;
                    System.Type derived = typeof(Derived);
                    if (derived.IsClass && derived.IsSealed &&
                        !derived.IsValueType && !derived.IsInterface &&
                        !derived.IsAbstract && typeof(AbstractBase).IsAbstract &&
                        typeof(IMarker).IsAbstract && !typeof(Pair).IsAbstract &&
                        !typeof(Derived[]).IsAbstract) passed |= 1;
                    if (typeof(Pair).IsValueType && !typeof(Pair).IsEnum) passed |= 2;
                    if (typeof(Mode).IsEnum &&
                        System.Type.GetTypeCode(typeof(Mode)) == System.TypeCode.UInt16) passed |= 4;
                    if (typeof(IMarker).IsInterface && !typeof(IMarker).IsClass) passed |= 8;
                    if (typeof(Derived[]).IsArray && typeof(Derived[]).IsSZArray &&
                        typeof(Derived[]).IsClass && typeof(Derived[]).IsSealed) passed |= 16;
                    if (derived.BaseType == typeof(Base) &&
                        typeof(Base).BaseType == typeof(object)) passed |= 32;
                    if (typeof(Base).IsAssignableFrom(derived) &&
                        typeof(IMarker).IsAssignableFrom(derived) &&
                        !derived.IsAssignableFrom(typeof(Base))) passed |= 64;
                    if (derived.IsSubclassOf(typeof(Base)) &&
                        !derived.IsSubclassOf(derived) &&
                        !derived.IsSubclassOf(typeof(IMarker)) &&
                        typeof(IMarker).IsSubclassOf(typeof(object)) &&
                        !typeof(object).IsSubclassOf(typeof(object))) passed |= 128;
                    if (derived.IsEquivalentTo(typeof(Derived)) &&
                        !derived.IsEquivalentTo(typeof(Base))) passed |= 256;
                    if (derived.IsInstanceOfType(new Derived()) &&
                        !derived.IsInstanceOfType(new Base()) &&
                        !derived.IsInstanceOfType(null)) passed |= 512;
                    bool subclassNullThrows = false;
                    try
                    {
                        _ = derived.IsSubclassOf(null!);
                    }
                    catch (System.ArgumentNullException)
                    {
                        subclassNullThrows = true;
                    }
                    if (!derived.IsAssignableFrom(null) && subclassNullThrows &&
                        System.Type.GetTypeCode(null) == System.TypeCode.Empty) passed |= 1024;
                    if (typeof(Box<>).IsGenericTypeDefinition &&
                        typeof(Box<>).IsGenericType &&
                        typeof(Box<>).ContainsGenericParameters) passed |= 2048;
                    if (typeof(Box<int>).IsGenericType &&
                        !typeof(Box<int>).IsGenericTypeDefinition &&
                        !typeof(Box<int>).ContainsGenericParameters) passed |= 4096;
                    if (!derived.IsByRef && !derived.IsPointer &&
                        System.Type.GetTypeCode(derived) == System.TypeCode.Object) passed |= 8192;
                    if (typeof(int).IsSealed && !typeof(int).IsClass &&
                        typeof(string).IsSealed && typeof(string).IsClass &&
                        typeof(object).IsAssignableFrom(typeof(IMarker))) passed |= 16384;
                    return passed;
                }
            }
            """,
            "10.0.401",
            "latest",
            optimized);

        var compilation = NetWasmCompiler.Compile(new CompilerOptions(
            assembly,
            [assets.CoreLib],
            "RuntimeTypeFactsFixture.EntryPoint",
            "Run",
            [],
            Target: target));

        Assert.Equal(
            32767,
            ExecuteWithStandardWasiNode(
                compilation.ApplicationModule,
                assets.Directory,
                0,
                target,
                compilation.StaticDataEnd));
        Assert.True(compilation.Program.RequiresTypeFacts);
        Assert.Equal(RuntimeTypeNamePayload.None, compilation.Program.TypeNamePayload);
        var factsLookups = compilation.Program.Methods.Values
            .Concat(compilation.Program.ConstructedMethods.Values)
            .SelectMany(method => method.Body.Instructions)
            .Where(instruction => instruction.Operation == CilOperation.GetTypeFacts)
            .ToArray();
        Assert.NotEmpty(factsLookups);
        foreach (var method in compilation.Program.Methods.Values)
        {
            var roots = compilation.Program.RootMaps[method.Method.Definition.Key];
            foreach (var lookup in method.Body.Instructions.Where(instruction =>
                         instruction.Operation == CilOperation.GetTypeFacts))
            {
                Assert.DoesNotContain(lookup.Offset, roots.Safepoints.Keys);
            }
        }
    }

    [Theory]
    [InlineData(false, WasmTarget.Wasm32)]
    [InlineData(false, WasmTarget.Wasm64)]
    [InlineData(true, WasmTarget.Wasm32)]
    [InlineData(true, WasmTarget.Wasm64)]
    public void CompilerExecutesBoundedGenericArgumentQueries(
        bool optimized,
        WasmTarget target)
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSourceWithCompiler(
            "RuntimeGenericArgumentsFixture",
            """
            namespace RuntimeGenericArgumentsFixture;

            public sealed class Box<T> { }
            public sealed class Pair<TFirst, TSecond> { }
            public sealed class Other<T> { }

            public static class EntryPoint
            {
                public static int Run(int input)
                {
                    int passed = 0;
                    if (typeof(int).GetGenericArguments().Length == 0) passed |= 1;

                    System.Type[] closed = typeof(Box<int>).GetGenericArguments();
                    if (closed.Length == 1 && closed[0] == typeof(int)) passed |= 2;

                    System.Type[] closedPair =
                        typeof(Pair<int, string>).GetGenericArguments();
                    if (closedPair.Length == 2 &&
                        closedPair[0] == typeof(int) &&
                        closedPair[1] == typeof(string)) passed |= 4;

                    System.Type[] open = typeof(Box<>).GetGenericArguments();
                    if (open.Length == 1 &&
                        open[0].Name == "T" &&
                        open[0].ContainsGenericParameters &&
                        !open[0].IsGenericType &&
                        open[0].Namespace == null &&
                        open[0].FullName == null) passed |= 8;

                    System.Type[] openPair = typeof(Pair<,>).GetGenericArguments();
                    if (openPair.Length == 2 &&
                        openPair[0].Name == "TFirst" &&
                        openPair[1].Name == "TSecond") passed |= 16;

                    System.Type other = typeof(Other<>).GetGenericArguments()[0];
                    if (open[0] != other) passed |= 32;

                    System.Type[] openAgain = typeof(Box<>).GetGenericArguments();
                    if (openAgain.Length == 1 &&
                        object.ReferenceEquals(open[0], openAgain[0])) passed |= 64;
                    if (!object.ReferenceEquals(open, openAgain)) passed |= 128;
                    return passed;
                }
            }
            """,
            "10.0.401",
            "latest",
            optimized);

        var compilation = NetWasmCompiler.Compile(new CompilerOptions(
            assembly,
            [assets.CoreLib],
            "RuntimeGenericArgumentsFixture.EntryPoint",
            "Run",
            [],
            Target: target));

        Assert.Equal(
            255,
            ExecuteWithStandardWasiNode(
                compilation.ApplicationModule,
                assets.Directory,
                0,
                target,
                compilation.StaticDataEnd));
        Assert.True(compilation.Program.RequiresGenericArguments);
        Assert.True(compilation.Program.RequiresTypeFacts);
        Assert.NotEmpty(compilation.Layouts.StaticData.MetadataTypeDescriptors);
        Assert.All(
            compilation.Layouts.StaticData.MetadataTypeDescriptors,
            descriptor => Assert.Equal(
                CliTypeShape.GenericTypeParameter,
                descriptor.Type.Shape));
    }

    [Fact]
    public void DesktopRuntimeTypeUsesCoreClrSubclassSemantics()
    {
        var byReference = typeof(int).MakeByRefType();
        var pointer = typeof(int).MakePointerType();

        Assert.Throws<ArgumentNullException>(() =>
            typeof(string).IsSubclassOf(null!));
        Assert.True(typeof(IDisposable).IsSubclassOf(typeof(object)));
        Assert.True(byReference.IsSubclassOf(typeof(object)));
        Assert.True(pointer.IsSubclassOf(typeof(object)));
        Assert.False(typeof(object).IsSubclassOf(typeof(object)));
        Assert.True(typeof(object).IsAssignableFrom(typeof(IDisposable)));
        Assert.False(typeof(object).IsAssignableFrom(byReference));
        Assert.False(typeof(object).IsAssignableFrom(pointer));
        Assert.True(typeof(int).IsSealed);
        Assert.False(typeof(int).IsClass);
        Assert.True(typeof(string).IsSealed);
        Assert.True(typeof(string).IsClass);
        Assert.True(byReference.IsClass);
        Assert.True(pointer.IsClass);
        Assert.False(byReference.IsSealed);
        Assert.False(pointer.IsSealed);
        Assert.Null(byReference.BaseType);
        Assert.Null(pointer.BaseType);
    }

    [Fact]
    public void DesktopMaterializesNonStorageVoidRuntimeType()
    {
        var trustedPlatformAssemblies =
            (string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES");
        Assert.False(string.IsNullOrWhiteSpace(trustedPlatformAssemblies));
        var references = trustedPlatformAssemblies!
            .Split(Path.PathSeparator)
            .Select(path =>
                global::Microsoft.CodeAnalysis.MetadataReference.CreateFromFile(path));
        var compilation = global::Microsoft.CodeAnalysis.CSharp.CSharpCompilation.Create(
            "VoidRuntimeType.Desktop",
            [global::Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree.ParseText(
                VoidRuntimeTypeSource)],
            references,
            new global::Microsoft.CodeAnalysis.CSharp.CSharpCompilationOptions(
                global::Microsoft.CodeAnalysis.OutputKind.DynamicallyLinkedLibrary));
        using var assemblyStream = new MemoryStream();
        var emit = compilation.Emit(assemblyStream);
        Assert.True(emit.Success);

        assemblyStream.Position = 0;
        var assembly = System.Runtime.Loader.AssemblyLoadContext.Default.LoadFromStream(
            assemblyStream);
        var run = assembly.GetType("VoidRuntimeType.EntryPoint")!.GetMethod("Run")!;

        Assert.Equal(42, run.Invoke(null, [41]));
    }

    [Theory]
    [InlineData(false, WasmTarget.Wasm32)]
    [InlineData(false, WasmTarget.Wasm64)]
    [InlineData(true, WasmTarget.Wasm32)]
    [InlineData(true, WasmTarget.Wasm64)]
    public void CompilerMaterializesNonStorageVoidRuntimeType(
        bool optimized,
        WasmTarget target)
    {
        using var assets = TestAssets.Create();
        ICompilationScenarioExecutor executor = new CompilationScenarioExecutor(assets);

        var result = executor.Execute(new CompilationScenario(
            "VoidRuntimeType.Application",
            VoidRuntimeTypeSource,
            "VoidRuntimeType.EntryPoint",
            optimized,
            target,
            41,
            []));

        Assert.Equal(42, result);
    }

    [Theory]
    [InlineData(false, WasmTarget.Wasm32)]
    [InlineData(false, WasmTarget.Wasm64)]
    [InlineData(true, WasmTarget.Wasm32)]
    [InlineData(true, WasmTarget.Wasm64)]
    public void CompilerExecutesCrossAssemblyGenericRuntimeTypeIdentity(bool optimized, WasmTarget target)
    {
        using var assets = TestAssets.Create();
        const string dependencySource = """
            namespace CrossAssemblyDependency;

            public static class CrossAssemblyTypeTokenProvider
            {
                public static System.Type Resolve<T>() => typeof(T);

                public static T Cast<T>(object value) => (T)value;
            }

            public static class CrossAssemblyGenericTypeCaster<T>
            {
                public static T Cast(object value) => (T)value;
            }
            """;
        var dependencyAssemblyPath = CompileCrossAssemblySource(assets, optimized,
            "CrossAssemblyGenericRuntimeTypeDependency",
            dependencySource);
        const string applicationSource = """
            using CrossAssemblyDependency;

            namespace TypeFacadeFixture;

            public interface ICrossAssemblyTypeMarker
            {
            }

            public sealed class CrossAssemblyTypeMarker : ICrossAssemblyTypeMarker
            {
            }

            public readonly struct CrossAssemblyValue(int value)
            {
                public int Value { get; } = value;
            }

            public enum CrossAssemblyMode
            {
                Ready = 7
            }

            public static class EntryPoint
            {
                private const int Passed = 32767;

                public static int Run(int input) => input switch
                {
                    1 => ResolveTypeToken(),
                    2 => CastMethodInterface(),
                    3 => CastMethodClass(),
                    4 => CastMethodInteger(),
                    5 => CastMethodNullableInteger(),
                    6 => CastMethodStruct(),
                    7 => CastMethodEnum(),
                    8 => CastMethodNestedArray(),
                    9 => CastDeclaringInterface(),
                    10 => CastDeclaringInteger(),
                    41 => RunAll(),
                    _ => 0
                };

                private static int ResolveTypeToken() =>
                    CrossAssemblyTypeTokenProvider.Resolve<ICrossAssemblyTypeMarker>() == typeof(ICrossAssemblyTypeMarker)
                        ? Passed
                        : 1;

                private static int CastMethodInterface() =>
                    CrossAssemblyTypeTokenProvider.Cast<ICrossAssemblyTypeMarker>(new CrossAssemblyTypeMarker()) is CrossAssemblyTypeMarker
                        ? Passed
                        : 2;

                private static int CastMethodClass()
                {
                    var marker = new CrossAssemblyTypeMarker();
                    return ReferenceEquals(CrossAssemblyTypeTokenProvider.Cast<CrossAssemblyTypeMarker>(marker), marker)
                        ? Passed
                        : 3;
                }

                private static int CastMethodInteger() =>
                    CrossAssemblyTypeTokenProvider.Cast<int>(42) == 42
                        ? Passed
                        : 4;

                private static int CastMethodNullableInteger() =>
                    CrossAssemblyTypeTokenProvider.Cast<int?>(42) == 42
                        ? Passed
                        : 5;

                private static int CastMethodStruct() =>
                    CrossAssemblyTypeTokenProvider.Cast<CrossAssemblyValue>(new CrossAssemblyValue(6)).Value == 6
                        ? Passed
                        : 6;

                private static int CastMethodEnum() =>
                    CrossAssemblyTypeTokenProvider.Cast<CrossAssemblyMode>(CrossAssemblyMode.Ready) == CrossAssemblyMode.Ready
                        ? Passed
                        : 7;

                private static int CastMethodNestedArray()
                {
                    var nested = new[] { new[] { new CrossAssemblyTypeMarker() } };
                    var result = CrossAssemblyTypeTokenProvider.Cast<ICrossAssemblyTypeMarker[][]>(nested);
                    return result[0][0] is CrossAssemblyTypeMarker
                        ? Passed
                        : 8;
                }

                private static int CastDeclaringInterface() =>
                    CrossAssemblyGenericTypeCaster<ICrossAssemblyTypeMarker>.Cast(new CrossAssemblyTypeMarker()) is CrossAssemblyTypeMarker
                        ? Passed
                        : 9;

                private static int CastDeclaringInteger() =>
                    CrossAssemblyGenericTypeCaster<int>.Cast(42) == 42
                        ? Passed
                        : 10;

                private static int RunAll()
                {
                    if (ResolveTypeToken() != Passed) return 1;
                    if (CastMethodInterface() != Passed) return 2;
                    if (CastMethodClass() != Passed) return 3;
                    if (CastMethodInteger() != Passed) return 4;
                    if (CastMethodNullableInteger() != Passed) return 5;
                    if (CastMethodStruct() != Passed) return 6;
                    if (CastMethodEnum() != Passed) return 7;
                    if (CastMethodNestedArray() != Passed) return 8;
                    if (CastDeclaringInterface() != Passed) return 9;
                    if (CastDeclaringInteger() != Passed) return 10;
                    return Passed;
                }
            }
            """;
        var desktopResult = ExecuteDesktopCrossAssemblyGenericRuntimeType(optimized,
            dependencySource,
            applicationSource);
        Assert.Equal(32767, desktopResult);

        var assembly = CompileCrossAssemblySource(assets, optimized,
            "TypeFacadeFixture",
            applicationSource,
            dependencyAssemblyPath);

        AssertContainsStaticEntryPointMetadata(assembly);

        var result = NetWasmCompiler.Compile(new CompilerOptions(
            assembly,
            [assets.CoreLib, dependencyAssemblyPath],
            "TypeFacadeFixture.EntryPoint",
            "Run",
            [],
            Target: target
            ));

        foreach (var input in Enumerable.Range(1, 10).Append(41))
        {
            Assert.Equal(32767, CompilerTestSupport.ExecuteWithStandardWasiNode(result.ApplicationModule, assets.Directory, input, target, result.StaticDataEnd));
        }
        Assert.Contains(
            result.Program.Methods.Values
                .Concat(result.Program.ConstructedMethods.Values)
                .SelectMany(method => method.Body.Instructions),
            instruction => instruction.Operation == CilOperation.LoadTypeToken);
        foreach (var method in result.Program.Methods.Values)
        {
            var roots = result.Program.RootMaps[method.Method.Definition.Key];
            foreach (var instruction in method.Body.Instructions
                         .Where(instruction => instruction.Operation is
                             CilOperation.MaterializeType or CilOperation.GetObjectType))
            {
                // A static Type object needs no allocation, so only the runtime lookups are safepoints.
                Assert.Equal(!CilTypeMaterialization.UsesStaticTypeObject(instruction),
                    roots.Safepoints.ContainsKey(instruction.Offset));
            }
        }
        foreach ((var identity, var method) in result.Program.ConstructedMethods)
        {
            var roots = result.Program.ConstructedRootMaps[identity];
            foreach (var instruction in method.Body.Instructions
                         .Where(instruction => instruction.Operation is
                             CilOperation.MaterializeType or CilOperation.GetObjectType))
            {
                // A static Type object needs no allocation, so only the runtime lookups are safepoints.
                Assert.Equal(!CilTypeMaterialization.UsesStaticTypeObject(instruction),
                    roots.Safepoints.ContainsKey(instruction.Offset));
            }
        }
        Assert.True(result.ApplicationModule.AsSpan().IndexOf("get_type_object"u8) >= 0);
    }


    [Fact]
    public void CompilerExecutesMinimalRuntimeTypeFacade()
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSource(
            "TypeFacadeFixture",
            """
            namespace TypeFacadeFixture;

            public interface IMarker
            {
            }

            public class Base
            {
            }

            public sealed class Derived : Base, IMarker
            {
            }

            public struct Pair
            {
                public int Value;
            }

            public sealed class Box<T>
            {
                public T Value = default(T)!;
            }

            public delegate int Compute(int value);

            public static class EntryPoint
            {
                public static int Run(int input)
                {
                    int passed = 0;
                    System.Type first = typeof(Derived);
                    System.Type second = typeof(Derived);
                    if (object.ReferenceEquals(first, second) && first == second) passed |= 1;
                    if (typeof(Base) != first && typeof(IMarker) != first) passed |= 2;

                    Base asBase = new Derived();
                    if (asBase.GetType() == typeof(Derived)) passed |= 4;
                    IMarker asInterface = (IMarker)asBase;
                    if (asInterface.GetType() == typeof(Derived)) passed |= 8;

                    object boxed = new Pair { Value = input };
                    if (boxed.GetType() == typeof(Pair)) passed |= 16;
                    if ("type".GetType() == typeof(string)) passed |= 32;

                    if (typeof(Box<int>) == typeof(Box<int>) &&
                        typeof(Box<int>) != typeof(Box<Derived>)) passed |= 64;
                    if (Token<int>() == typeof(int) &&
                        Token<Box<Derived>>() == typeof(Box<Derived>)) passed |= 128;

                    object array = new Derived[1];
                    if (array.GetType() == typeof(Derived[]) &&
                        typeof(Derived[]) != typeof(Base[])) passed |= 256;

                    System.Type? none = null;
                    if (none == null && first != null) passed |= 512;
                    try
                    {
                        object missing = null!;
                        _ = missing.GetType();
                    }
                    catch (System.NullReferenceException)
                    {
                        passed |= 1024;
                    }

                    System.Type[] roots = [typeof(Base), typeof(Derived)];
                    if (object.ReferenceEquals(roots[1], first)) passed |= 2048;
                    Compute single = Add;
                    Compute multicast = single + single;
                    if (single.GetType() == typeof(Compute) &&
                        multicast.GetType() == typeof(Compute)) passed |= 4096;
                    if (first.Equals(second) &&
                        first.GetHashCode() == second.GetHashCode()) passed |= 8192;
                    System.RuntimeTypeHandle empty = default;
                    if (System.Type.GetTypeFromHandle(empty) == null) passed |= 16384;
                    if (typeof(Box<>) == typeof(Box<>) &&
                        typeof(Box<>) != typeof(Box<int>)) passed |= 32768;
                    return passed;
                }

                private static System.Type Token<T>() => typeof(T);
                private static int Add(int value) => value + 1;
            }
            """);

        var result = NetWasmCompiler.Compile(new CompilerOptions(
            assembly,
            [assets.CoreLib],
            "TypeFacadeFixture.EntryPoint",
            "Run",
            []));

        Assert.Equal(65535, ExecuteWithNode(result.ApplicationModule, assets.Directory, 41));
        Assert.False(result.Program.RequiresTypeFacts);
        Assert.Contains(
            result.Program.Methods.Values
                .Concat(result.Program.ConstructedMethods.Values)
                .SelectMany(method => method.Body.Instructions),
            instruction => instruction.Operation == CilOperation.LoadTypeToken);
        foreach (var method in result.Program.Methods.Values)
        {
            var roots = result.Program.RootMaps[method.Method.Definition.Key];
            foreach (var instruction in method.Body.Instructions
                         .Where(instruction => instruction.Operation is
                             CilOperation.MaterializeType or CilOperation.GetObjectType))
            {
                // A static Type object needs no allocation, so only the runtime lookups are safepoints.
                Assert.Equal(!CilTypeMaterialization.UsesStaticTypeObject(instruction),
                    roots.Safepoints.ContainsKey(instruction.Offset));
            }
        }
        foreach ((var identity, var method) in result.Program.ConstructedMethods)
        {
            var roots = result.Program.ConstructedRootMaps[identity];
            foreach (var instruction in method.Body.Instructions
                         .Where(instruction => instruction.Operation is
                             CilOperation.MaterializeType or CilOperation.GetObjectType))
            {
                // A static Type object needs no allocation, so only the runtime lookups are safepoints.
                Assert.Equal(!CilTypeMaterialization.UsesStaticTypeObject(instruction),
                    roots.Safepoints.ContainsKey(instruction.Offset));
            }
        }
        Assert.True(result.ApplicationModule.AsSpan().IndexOf("get_type_object"u8) >= 0);
    }

    [Theory]
    [InlineData(false, WasmTarget.Wasm32)]
    [InlineData(false, WasmTarget.Wasm64)]
    [InlineData(true, WasmTarget.Wasm32)]
    [InlineData(true, WasmTarget.Wasm64)]
    public void CompilerExecutesDemandedRuntimeTypeNames(
        bool optimized,
        WasmTarget target)
    {
        using var assets = TestAssets.Create();

        var assembly = assets.CompileSourceWithCompiler(
            "RuntimeTypeNamesFixture",
            """
                namespace RuntimeTypeNamesFixture;

                public sealed class Box<T> { }

                public static class EntryPoint
                {
                    public static int Run(int input)
                    {
                        int result = 0;
                        System.Type type = typeof(Box<int>[]);
                        if (type.Name == "Box`1[]") result |= 1;
                        if (type.Namespace == "RuntimeTypeNamesFixture") result |= 2;
                        if (typeof(EntryPoint).FullName ==
                            "RuntimeTypeNamesFixture.EntryPoint") result |= 4;
                        if (type.ToString() ==
                            "RuntimeTypeNamesFixture.Box`1[System.Int32][]") result |= 8;
                        object boxedType = type;
                        if (boxedType.ToString() ==
                            "RuntimeTypeNamesFixture.Box`1[System.Int32][]") result |= 16;
                        System.Reflection.MemberInfo member = type;
                        if (member.Name == "Box`1[]") result |= 32;
                        return result;
                    }
                }
                """,
            "10.0.401",
            "latest",
            optimized);
        var compilation = NetWasmCompiler.Compile(new CompilerOptions(
            assembly,
            [assets.CoreLib],
            "RuntimeTypeNamesFixture.EntryPoint",
            "Run",
            [],
            Target: target));

        Assert.Equal(
            63,
            ExecuteWithStandardWasiNode(
                compilation.ApplicationModule,
                assets.Directory,
                0,
                target,
                compilation.StaticDataEnd));
        Assert.Equal(
            RuntimeTypeNamePayload.Name |
                RuntimeTypeNamePayload.Namespace |
                RuntimeTypeNamePayload.FullName |
                RuntimeTypeNamePayload.DisplayName,
            compilation.Program.TypeNamePayload);
        Assert.True(compilation.Program.RequiresTypeFacts);
        var coreLibIdentity = System.Reflection.AssemblyName
            .GetAssemblyName(assets.CoreLib)
            .FullName;
        Assert.Contains(
            "RuntimeTypeNamesFixture.Box`1[[System.Int32, " +
                coreLibIdentity + "]][]",
            compilation.Layouts.StaticData.Strings.Keys);
    }

    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void BaseVirtualCallsRetainOnlyTypeNameAndDisplayName(WasmTarget target)
    {
        using var assets = TestAssets.Create();
        const string expectedName = "Box`1";
        const string expectedDisplayName =
            "RuntimeTypeNamesViaBaseCallsFixture.Box`1[System.Int32]";
        var assembly = assets.CompileSource(
            "RuntimeTypeNamesViaBaseCallsFixture",
            """
                namespace RuntimeTypeNamesViaBaseCallsFixture;

                public sealed class Box<T> { }

                public static class EntryPoint
                {
                    public static int Run(int input)
                    {
                        object value = typeof(Box<int>);
                        System.Reflection.MemberInfo member =
                            (System.Reflection.MemberInfo)value;
                        return (member.Name.Length * 100) + value.ToString()!.Length;
                    }
                }
                """);
        var compilation = NetWasmCompiler.Compile(new CompilerOptions(
            assembly,
            [assets.CoreLib],
            "RuntimeTypeNamesViaBaseCallsFixture.EntryPoint",
            "Run",
            [],
            Target: target));

        Assert.Equal(
            (expectedName.Length * 100) + expectedDisplayName.Length,
            ExecuteWithStandardWasiNode(
                compilation.ApplicationModule,
                assets.Directory,
                0,
                target,
                compilation.StaticDataEnd));
        Assert.Equal(
            RuntimeTypeNamePayload.Name | RuntimeTypeNamePayload.DisplayName,
            compilation.Program.TypeNamePayload);
        Assert.Contains(expectedName, compilation.Layouts.StaticData.Strings.Keys);
        Assert.Contains(expectedDisplayName, compilation.Layouts.StaticData.Strings.Keys);
        Assert.DoesNotContain(
            "RuntimeTypeNamesViaBaseCallsFixture",
            compilation.Layouts.StaticData.Strings.Keys);
        Assert.DoesNotContain(
            compilation.Layouts.StaticData.Strings.Keys,
            value => value.StartsWith(
                    "RuntimeTypeNamesViaBaseCallsFixture.Box`1[[",
                    StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void CompilerReturnsNullForDemandedGlobalNamespace(WasmTarget target)
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSource(
            "RuntimeGlobalNamespaceFixture",
            """
                public static class EntryPoint
                {
                    public static int Run(int input) =>
                        typeof(EntryPoint).Namespace is null ? 1 : 0;
                }
                """);
        var compilation = NetWasmCompiler.Compile(new CompilerOptions(
            assembly,
            [assets.CoreLib],
            "EntryPoint",
            "Run",
            [],
            Target: target));

        Assert.Equal(
            1,
            ExecuteWithStandardWasiNode(
                compilation.ApplicationModule,
                assets.Directory,
                0,
                target,
                compilation.StaticDataEnd));
        Assert.Equal(
            RuntimeTypeNamePayload.Namespace,
            compilation.Program.TypeNamePayload);
        Assert.DoesNotContain("EntryPoint", compilation.Layouts.StaticData.Strings.Keys);
    }


    private static void AssertContainsStaticEntryPointMetadata(string assemblyPath)
    {
        using var stream = File.OpenRead(assemblyPath);
        using var peReader = new System.Reflection.PortableExecutable.PEReader(stream);
        var metadata =
            System.Reflection.Metadata.PEReaderExtensions.GetMetadataReader(peReader);
        var entryType = metadata.TypeDefinitions
            .Select(metadata.GetTypeDefinition)
            .Single(type =>
                metadata.GetString(type.Namespace) == "TypeFacadeFixture" &&
                metadata.GetString(type.Name) == "EntryPoint");
        var entryMethod = entryType.GetMethods()
            .Select(metadata.GetMethodDefinition)
            .Single(method => metadata.GetString(method.Name) == "Run");
        Assert.True(
            (entryMethod.Attributes & System.Reflection.MethodAttributes.Static) != 0);
    }

    private const string VoidRuntimeTypeSource = """
        namespace VoidRuntimeType;

        public static class EntryPoint
        {
            public static int Run(int input)
            {
                System.Type type = typeof(void);
                return object.ReferenceEquals(type, typeof(void))
                    ? input + 1
                    : 0;
            }
        }
        """;

    private static string CompileCrossAssemblySource(
        TestAssets assets,
        bool optimized,
        string assemblyName,
        string source,
        params string[] references)
    {
        return optimized
            ? assets.CompileOptimizedSource(assemblyName, source, references)
            : assets.CompileSource(assemblyName, source, references);
    }

    private static int ExecuteDesktopCrossAssemblyGenericRuntimeType(bool optimized,
        string dependencySource,
        string applicationSource)
    {
        var trustedPlatformAssemblies =
            (string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES");
        Assert.False(string.IsNullOrWhiteSpace(trustedPlatformAssemblies));

        var references = trustedPlatformAssemblies!
            .Split(Path.PathSeparator)
            .Select(path =>
                global::Microsoft.CodeAnalysis.MetadataReference.CreateFromFile(path))
            .ToArray();
        var dependencyCompilation = global::Microsoft.CodeAnalysis.CSharp.CSharpCompilation.Create(
            "DesktopCrossAssemblyGenericRuntimeTypeDependency",
            [global::Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree.ParseText(dependencySource)],
            references,
            new global::Microsoft.CodeAnalysis.CSharp.CSharpCompilationOptions(
                global::Microsoft.CodeAnalysis.OutputKind.DynamicallyLinkedLibrary, optimizationLevel: optimized ? global::Microsoft.CodeAnalysis.OptimizationLevel.Release : global::Microsoft.CodeAnalysis.OptimizationLevel.Debug));

        using var dependencyStream = new MemoryStream();
        Assert.True(dependencyCompilation.Emit(dependencyStream).Success);
        var dependencyBytes = dependencyStream.ToArray();

        var applicationCompilation = global::Microsoft.CodeAnalysis.CSharp.CSharpCompilation.Create(
            "DesktopCrossAssemblyGenericRuntimeTypeApplication",
            [global::Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree.ParseText(applicationSource)],
            references.Append(
                global::Microsoft.CodeAnalysis.MetadataReference.CreateFromImage(dependencyBytes)),
            new global::Microsoft.CodeAnalysis.CSharp.CSharpCompilationOptions(
                global::Microsoft.CodeAnalysis.OutputKind.DynamicallyLinkedLibrary, optimizationLevel: optimized ? global::Microsoft.CodeAnalysis.OptimizationLevel.Release : global::Microsoft.CodeAnalysis.OptimizationLevel.Debug));

        using var applicationStream = new MemoryStream();
        Assert.True(applicationCompilation.Emit(applicationStream).Success);
        var applicationBytes = applicationStream.ToArray();

        var desktopContext = new System.Runtime.Loader.AssemblyLoadContext(
            $"cross-assembly-type-token-{Guid.NewGuid():N}",
            isCollectible: true);
        try
        {
            using var dependencyLoadStream = new MemoryStream(
                dependencyBytes,
                writable: false);
            using var applicationLoadStream = new MemoryStream(
                applicationBytes,
                writable: false);
            _ = desktopContext.LoadFromStream(dependencyLoadStream);
            var desktopAssembly = desktopContext.LoadFromStream(applicationLoadStream);
            var desktopEntryType = desktopAssembly.GetType(
                "TypeFacadeFixture.EntryPoint",
                throwOnError: true)!;
            var desktopEntry = desktopEntryType.GetMethod(
                "Run",
                System.Reflection.BindingFlags.Public |
                    System.Reflection.BindingFlags.Static)!;
            return (int)desktopEntry.Invoke(null, [41])!;
        }
        finally
        {
            desktopContext.Unload();
        }
    }

}
