
using NetWasm.Compiler.ComponentModel;
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
            TestAssets.RepositorySdkVersion,
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
                    // Without names a parameter reads as its type ID, as every type does (issue 225).
                    if (open.Length == 1 &&
                        open[0].Name.StartsWith("type#") &&
                        open[0].ContainsGenericParameters &&
                        !open[0].IsGenericType &&
                        open[0].Namespace == null) passed |= 8;

                    System.Type[] openPair = typeof(Pair<,>).GetGenericArguments();
                    if (openPair.Length == 2 &&
                        openPair[0].Name != openPair[1].Name) passed |= 16;

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
            TestAssets.RepositorySdkVersion,
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

    // The type table's flag word carries the array rank, TypeDefinition and the generic parameter
    // bits; each answer below is .NET 11's.
    [Theory]
    [InlineData(false, WasmTarget.Wasm32)]
    [InlineData(true, WasmTarget.Wasm64)]
    public void CompilerExecutesRankTypeDefinitionAndGenericParameterFlags(bool optimized, WasmTarget target)
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSourceWithCompiler(
            "RuntimeTypeFlagsFixture",
            """
            namespace RuntimeTypeFlagsFixture;

            public sealed class Box<T> { }

            public static class EntryPoint
            {
                public static int Run(int input)
                {
                    object cube = new int[2, 3, 4];
                    object vector = new int[1];
                    int passed = 0;
                    if (cube.GetType().GetArrayRank() == 3) passed |= 1;
                    if (vector.GetType().GetArrayRank() == 1) passed |= 2;
                    if (typeof(int).IsTypeDefinition) passed |= 4;
                    if (!typeof(Box<int>).IsTypeDefinition) passed |= 8;
                    if (typeof(Box<>).IsTypeDefinition) passed |= 16;
                    if (!vector.GetType().IsTypeDefinition) passed |= 32;
                    System.Type parameter = typeof(Box<>).GetGenericArguments()[0];
                    if (parameter.IsGenericParameter && parameter.IsGenericTypeParameter &&
                        !parameter.IsGenericMethodParameter) passed |= 64;
                    if (!typeof(int).IsGenericParameter) passed |= 128;
                    return passed;
                }
            }
            """,
            TestAssets.RepositorySdkVersion,
            "latest",
            optimized);

        var compilation = NetWasmCompiler.Compile(new CompilerOptions(
            assembly, [assets.CoreLib], "RuntimeTypeFlagsFixture.EntryPoint", "Run", [], Target: target));

        Assert.Equal(255, ExecuteWithStandardWasiNode(
            compilation.ApplicationModule, assets.Directory, 0, target, compilation.StaticDataEnd));
    }

    // DeclaringType from the type table, as .NET 11 answers it: a nested type of a constructed
    // generic reports the open definition, a generic parameter its owner, and arrays, pointers,
    // primitives and Nullable<T> nothing.
    [Theory]
    [InlineData(false, WasmTarget.Wasm32)]
    [InlineData(true, WasmTarget.Wasm64)]
    public void CompilerExecutesDeclaringTypes(bool optimized, WasmTarget target)
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSourceWithCompiler(
            "DeclaringTypesFixture",
            """
            namespace DeclaringTypesFixture;

            public sealed class Outer
            {
                public sealed class Inner { }
                public struct Value { }
            }

            public sealed class GenericOuter<T>
            {
                public sealed class Inner { }
                public sealed class GenericInner<U> { }
            }

            public static class EntryPoint
            {
                public static unsafe int Run(int input)
                {
                    object inner = new Outer.Inner();
                    int passed = 0;
                    if (inner.GetType().DeclaringType == typeof(Outer) &&
                        inner.GetType().ReflectedType == typeof(Outer) && inner.GetType().IsNested) passed |= 1;
                    if (typeof(Outer).DeclaringType == null && !typeof(Outer).IsNested) passed |= 2;
                    if (typeof(GenericOuter<int>.Inner).DeclaringType == typeof(GenericOuter<>)) passed |= 4;
                    if (typeof(GenericOuter<int>.GenericInner<string>).DeclaringType == typeof(GenericOuter<>)) passed |= 8;
                    System.Type parameter = typeof(GenericOuter<>).GetGenericArguments()[0];
                    if (parameter.DeclaringType == typeof(GenericOuter<>) && parameter.IsNested) passed |= 16;
                    if (typeof(Outer.Inner[]).DeclaringType == null && typeof(int).DeclaringType == null) passed |= 32;
                    if (typeof(Outer.Value?).DeclaringType == null && typeof(int*).DeclaringType == null) passed |= 64;
                    if (typeof(Outer.Value).DeclaringType == typeof(Outer)) passed |= 128;
                    return passed;
                }
            }
            """,
            TestAssets.RepositorySdkVersion,
            "latest",
            optimized,
            allowUnsafe: true);

        var compilation = NetWasmCompiler.Compile(new CompilerOptions(
            assembly, [assets.CoreLib], "DeclaringTypesFixture.EntryPoint", "Run", [], Target: target));

        Assert.Equal(255, ExecuteWithStandardWasiNode(
            compilation.ApplicationModule, assets.Directory, 0, target, compilation.StaticDataEnd));
        Assert.True(compilation.Program.RequiresDeclaringTypes);
    }

    // TypeAttributes and the queries built on them answer as .NET 11 does: visibility through nesting,
    // generic arguments and array elements, layout and string format, and IsSerializable through the
    // enum and delegate base chains; arrays, pointers and generic parameters take .NET's fixed values.
    [Theory]
    [InlineData(false, WasmTarget.Wasm32)]
    [InlineData(true, WasmTarget.Wasm64)]
    public void CompilerExecutesTypeAttributes(bool optimized, WasmTarget target)
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSourceWithCompiler(
            "TypeAttributesFixture",
            """
            using System;
            using System.Collections.Generic;
            using System.Reflection;
            using System.Runtime.InteropServices;

            namespace TypeAttributesFixture;

            public sealed class Sample
            {
                private sealed class Secret { }
                public sealed class Open { }
                public static Type SecretType => typeof(Secret);
            }
            internal sealed class Hidden { }
            [StructLayout(LayoutKind.Explicit)] public struct Overlay { [FieldOffset(0)] public int Value; }
            public enum Color { Red }
            public delegate int Operation(int value);
            public interface IShape { }

            public static class EntryPoint
            {
                public static unsafe int Run(int input)
                {
                    int passed = 0;
                    Type sample = typeof(Sample);
                    if (sample.IsPublic && !sample.IsNotPublic && sample.IsAutoLayout && sample.IsAnsiClass && sample.IsVisible &&
                        (sample.Attributes & TypeAttributes.Sealed) != 0) passed |= 1;
                    if (Sample.SecretType.IsNestedPrivate && !Sample.SecretType.IsVisible &&
                        typeof(Sample.Open).IsNestedPublic && typeof(Sample.Open).IsVisible) passed |= 2;
                    if (typeof(Hidden).IsNotPublic && !typeof(Hidden).IsVisible && !typeof(List<Hidden>).IsVisible &&
                        !typeof(Hidden[]).IsVisible && typeof(List<int>).IsVisible) passed |= 4;
                    if (typeof(Overlay).IsExplicitLayout && typeof(int).IsLayoutSequential && !typeof(Overlay).IsSerializable) passed |= 8;
                    if (typeof(Color).IsSerializable && typeof(Operation).IsSerializable && !typeof(IShape).IsSerializable &&
                        typeof(int).IsSerializable) passed |= 16;
                    if (typeof(int[]).Attributes == (TypeAttributes.Public | TypeAttributes.Sealed | TypeAttributes.Serializable) &&
                        typeof(int*).Attributes == TypeAttributes.Public) passed |= 32;
                    Type parameter = typeof(List<>).GetGenericArguments()[0];
                    if (parameter.Attributes == TypeAttributes.Public && parameter.IsVisible &&
                        typeof(List<int>).Attributes == typeof(List<>).Attributes) passed |= 64;
                    if ((typeof(IShape).Attributes & (TypeAttributes.Interface | TypeAttributes.Abstract)) ==
                        (TypeAttributes.Interface | TypeAttributes.Abstract)) passed |= 128;
                    return passed;
                }
            }
            """,
            TestAssets.RepositorySdkVersion,
            "latest",
            optimized,
            allowUnsafe: true);

        var compilation = NetWasmCompiler.Compile(new CompilerOptions(
            assembly, [assets.CoreLib], "TypeAttributesFixture.EntryPoint", "Run", [], Target: target));

        Assert.Equal(255, ExecuteWithStandardWasiNode(
            compilation.ApplicationModule, assets.Directory, 0, target, compilation.StaticDataEnd));
        Assert.True(compilation.Program.RequiresTypeAttributes);
    }

    // MakeArrayType, MakePointerType and MakeGenericType return the type the program has, and throw for
    // one it does not use; GetNestedTypes lists the program's nested types by visibility, also from a
    // constructed generic; GetNestedType throws without type names.
    [Theory]
    [InlineData(false, WasmTarget.Wasm32)]
    [InlineData(true, WasmTarget.Wasm64)]
    public void CompilerExecutesTypeLookups(bool optimized, WasmTarget target)
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSourceWithCompiler(
            "TypeLookupsFixture",
            """
            using System;
            using System.Collections.Generic;
            using System.Reflection;

            namespace TypeLookupsFixture;

            public sealed class Outer
            {
                public sealed class Open { }
                private sealed class Secret { }
                public static Type SecretType => typeof(Secret);
            }
            public sealed class Holder<T> { public sealed class Item { } }
            public sealed class Lonely { }

            public static class EntryPoint
            {
                public static unsafe int Run(int input)
                {
                    int passed = 0;
                    if (typeof(int).MakeArrayType() == typeof(int[]) && typeof(int).MakeArrayType(2) == typeof(int[,]) &&
                        typeof(int).MakePointerType() == typeof(int*)) passed |= 1;
                    if (typeof(List<>).MakeGenericType(typeof(int)) == typeof(List<int>) &&
                        typeof(Dictionary<,>).MakeGenericType(typeof(string), typeof(int)) == typeof(Dictionary<string, int>)) passed |= 2;
                    try { typeof(Lonely).MakeArrayType(); } catch (PlatformNotSupportedException) { passed |= 4; }
                    try { typeof(List<>).MakeGenericType(typeof(int), typeof(int)); } catch (ArgumentException) { passed |= 8; }
                    try { typeof(int).MakeArrayType(0); } catch (IndexOutOfRangeException) { passed |= 16; }
                    Type[] open = typeof(Outer).GetNestedTypes();
                    Type[] all = typeof(Outer).GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic);
                    if (open.Length == 1 && open[0] == typeof(Outer.Open) && all.Length == 2 &&
                        Array.IndexOf(all, Outer.SecretType) >= 0) passed |= 32;
                    if (typeof(Holder<int>).GetNestedTypes()[0] == typeof(Holder<>.Item) && typeof(int[]).GetNestedTypes().Length == 0) passed |= 64;
                    try { typeof(Outer).GetNestedType(typeof(Outer.Open).Name); } catch (PlatformNotSupportedException) { passed |= 128; }
                    return passed;
                }
            }
            """,
            TestAssets.RepositorySdkVersion,
            "latest",
            optimized,
            allowUnsafe: true);

        var compilation = NetWasmCompiler.Compile(new CompilerOptions(
            assembly, [assets.CoreLib], "TypeLookupsFixture.EntryPoint", "Run", [], Target: target));

        Assert.Equal(255, ExecuteWithStandardWasiNode(
            compilation.ApplicationModule, assets.Directory, 0, target, compilation.StaticDataEnd));
    }

    // GetInterfaces lists the program's interfaces a type implements, its own and its base types', and
    // through interface inheritance, never one it reaches only through variance; a vector its generic
    // collection interfaces of its own element and System.Array's. GetInterface throws without type names.
    [Theory]
    [InlineData(false, WasmTarget.Wasm32)]
    [InlineData(true, WasmTarget.Wasm64)]
    public void CompilerExecutesInterfaces(bool optimized, WasmTarget target)
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSourceWithCompiler(
            "InterfacesFixture",
            """
            using System;
            using System.Collections;
            using System.Collections.Generic;

            namespace InterfacesFixture;

            public interface IShape { }
            public interface IRound : IShape { }
            public class Base : IShape { }
            public sealed class Circle : Base, IRound, IComparable<Circle> { public int CompareTo(Circle? other) => 0; }
            public sealed class Words : IEnumerable<string>
            {
                public IEnumerator<string> GetEnumerator() { yield break; }
                IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
            }
            public sealed class Plain { }

            public static class EntryPoint
            {
                private static bool Has(Type[] types, Type type) => Array.IndexOf(types, type) >= 0;

                public static int Run(int input)
                {
                    int passed = 0;
                    Type[] circle = typeof(Circle).GetInterfaces();
                    if (circle.Length == 3 && Has(circle, typeof(IShape)) && Has(circle, typeof(IRound)) &&
                        Has(circle, typeof(IComparable<Circle>))) passed |= 1;
                    if (typeof(Base).GetInterfaces().Length == 1 && typeof(Base).GetInterfaces()[0] == typeof(IShape)) passed |= 2;
                    if (typeof(IRound).GetInterfaces().Length == 1 && typeof(IRound).GetInterfaces()[0] == typeof(IShape)) passed |= 4;
                    Type[] words = typeof(Words).GetInterfaces();
                    if (Has(words, typeof(IEnumerable<string>)) && Has(words, typeof(IEnumerable)) &&
                        !Has(words, typeof(IEnumerable<object>))) passed |= 8;
                    Type[] strings = typeof(string[]).GetInterfaces();
                    if (Has(strings, typeof(IList<string>)) && Has(strings, typeof(IEnumerable<string>)) && Has(strings, typeof(IList)) &&
                        !Has(strings, typeof(IEnumerable<object>))) passed |= 16;
                    if (typeof(Plain).GetInterfaces().Length == 0) passed |= 32;
                    try { typeof(Circle).GetInterface(typeof(IRound).Name); } catch (PlatformNotSupportedException) { passed |= 64; }
                    object boxed = new Words();
                    if (boxed is IEnumerable<object> && Has(boxed.GetType().GetInterfaces(), typeof(IEnumerable<string>))) passed |= 128;
                    return passed;
                }
            }
            """,
            TestAssets.RepositorySdkVersion,
            "latest",
            optimized);

        var compilation = NetWasmCompiler.Compile(new CompilerOptions(
            assembly, [assets.CoreLib], "InterfacesFixture.EntryPoint", "Run", [], Target: target));

        Assert.Equal(255, ExecuteWithStandardWasiNode(
            compilation.ApplicationModule, assets.Directory, 0, target, compilation.StaticDataEnd));
        Assert.True(compilation.Program.RequiresInterfaces);
    }

    // An assembly's module answers as NativeAOT's does; GetTypes and GetExportedTypes list the program's
    // types of the assembly; without type names, Type.GetType, Assembly.GetType and Module.GetType throw
    // PlatformNotSupportedException, also with throwOnError and for the type#<id> a type's name reads.
    [Theory]
    [InlineData(false, WasmTarget.Wasm32)]
    [InlineData(true, WasmTarget.Wasm64)]
    public void CompilerExecutesAssemblyTypeLookups(bool optimized, WasmTarget target)
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSourceWithCompiler(
            "AssemblyTypesFixture",
            """
            using System;
            using System.Reflection;

            namespace AssemblyTypesFixture;

            public sealed class Sample { }
            internal sealed class Hidden { }

            public static class EntryPoint
            {
                public static int Run(int input)
                {
                    Assembly own = typeof(Sample).Assembly;
                    Assembly core = typeof(int).Assembly;
                    Module module = typeof(Sample).Module;
                    int passed = 0;
                    if (module.Assembly == own && own.ManifestModule == module && own.GetModules().Length == 1 &&
                        module.Name == "<Unknown>" && module.ScopeName == "<Unknown>" && typeof(int).Module != module) passed |= 1;
                    try { _ = module.MetadataToken; } catch (InvalidOperationException) { passed |= 2; }
                    Type[] types = own.GetTypes();
                    if (Array.IndexOf(types, typeof(Sample)) >= 0 && Array.IndexOf(types, typeof(Hidden)) >= 0 &&
                        Array.IndexOf(types, typeof(int)) < 0 && Array.IndexOf(core.GetTypes(), typeof(int)) >= 0) passed |= 4;
                    Type[] exported = own.GetExportedTypes();
                    if (Array.IndexOf(exported, typeof(Sample)) >= 0 && Array.IndexOf(exported, typeof(Hidden)) < 0) passed |= 8;
                    try { Type.GetType(typeof(Sample).FullName!); } catch (PlatformNotSupportedException) { passed |= 16; }
                    try { Type.GetType("NoSuch.Type", throwOnError: true); } catch (PlatformNotSupportedException) { passed |= 32; }
                    try { own.GetType(typeof(Sample).FullName!); }
                    catch (PlatformNotSupportedException)
                    {
                        try { module.GetType(typeof(Hidden).FullName!); } catch (PlatformNotSupportedException) { passed |= 64; }
                    }
                    try { Type.GetType(typeof(Sample).AssemblyQualifiedName!, false, ignoreCase: true); } catch (PlatformNotSupportedException) { passed |= 128; }
                    return passed;
                }
            }
            """,
            TestAssets.RepositorySdkVersion,
            "latest",
            optimized);

        var compilation = NetWasmCompiler.Compile(new CompilerOptions(
            assembly, [assets.CoreLib], "AssemblyTypesFixture.EntryPoint", "Run", [], Target: target));

        Assert.Equal(255, ExecuteWithStandardWasiNode(
            compilation.ApplicationModule, assets.Directory, 0, target, compilation.StaticDataEnd));
    }

    // IsAssignableTo, IsFunctionPointer, GetEnumValuesAsUnderlyingType and IsCollectible answer as .NET 11's
    // runtime types and assemblies do.
    [Theory]
    [InlineData(false, WasmTarget.Wasm32)]
    [InlineData(true, WasmTarget.Wasm64)]
    public void CompilerExecutesTypeConveniences(bool optimized, WasmTarget target)
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSourceWithCompiler(
            "ConveniencesFixture",
            """
            using System;

            namespace ConveniencesFixture;

            public enum Level : byte { Low = 1, High = 9 }
            public class Base { }
            public sealed class Leaf : Base { }

            public static class EntryPoint
            {
                public static int Run(int input)
                {
                    int passed = 0;
                    if (typeof(Leaf).IsAssignableTo(typeof(Base)) && !typeof(Base).IsAssignableTo(typeof(Leaf)) &&
                        !typeof(Leaf).IsAssignableTo(null)) passed |= 1;
                    if (!typeof(int).IsFunctionPointer && !typeof(int*).IsUnmanagedFunctionPointer) passed |= 2;
                    Array values = typeof(Level).GetEnumValuesAsUnderlyingType();
                    if (values.Length == 2 && values is byte[] bytes && bytes[0] == 1 && bytes[1] == 9) passed |= 4;
                    try { typeof(int).GetEnumValuesAsUnderlyingType(); } catch (ArgumentException) { passed |= 8; }
                    if (!typeof(Leaf).Assembly.IsCollectible && !typeof(int).IsCollectible) passed |= 16;
                    return passed;
                }
            }
            """,
            TestAssets.RepositorySdkVersion,
            "latest",
            optimized,
            allowUnsafe: true);

        var compilation = NetWasmCompiler.Compile(new CompilerOptions(
            assembly, [assets.CoreLib], "ConveniencesFixture.EntryPoint", "Run", [], Target: target));

        Assert.Equal(31, ExecuteWithStandardWasiNode(
            compilation.ApplicationModule, assets.Directory, 0, target, compilation.StaticDataEnd));
    }

    // Every Type object is a RuntimeType, a TypeInfo, as on .NET 11: GetTypeInfo returns the type itself,
    // AsType returns it back, TypeInfo's members forward to Type's, Type is abstract, and an assembly's
    // DefinedTypes are its types.
    [Theory]
    [InlineData(false, WasmTarget.Wasm32)]
    [InlineData(true, WasmTarget.Wasm64)]
    public void CompilerExecutesTypeInfo(bool optimized, WasmTarget target)
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSourceWithCompiler(
            "TypeInfoFixture",
            """
            using System;
            using System.Collections.Generic;
            using System.Reflection;

            namespace TypeInfoFixture;

            public interface IShape { }
            public sealed class Circle : IShape { public sealed class Part { } }

            public static class EntryPoint
            {
                public static int Run(int input)
                {
                    int passed = 0;
                    Type type = typeof(Circle);
                    TypeInfo info = type.GetTypeInfo();
                    if (ReferenceEquals(info, type) && info.AsType() == type && type is TypeInfo &&
                        new Circle().GetType().GetTypeInfo() == info) passed |= 1;
                    if (info.IsSealed && !info.IsValueType && info.IsAssignableFrom(typeof(Circle).GetTypeInfo())) passed |= 2;
                    foreach (Type implemented in info.ImplementedInterfaces)
                    {
                        if (implemented == typeof(IShape)) passed |= 4;
                    }
                    if (typeof(List<>).GetTypeInfo().GenericTypeParameters.Length == 1 &&
                        typeof(List<int>).GetTypeInfo().GenericTypeParameters.Length == 0) passed |= 8;
                    foreach (TypeInfo nested in info.DeclaredNestedTypes)
                    {
                        if (nested == typeof(Circle.Part)) passed |= 16;
                    }
                    if (typeof(Type).IsAbstract && typeof(TypeInfo).IsAssignableFrom(type.GetType())) passed |= 32;
                    foreach (TypeInfo defined in type.Assembly.DefinedTypes)
                    {
                        if (defined == info) passed |= 64;
                    }
                    if (typeof(int).GetTypeInfo() == typeof(int) && typeof(int[]).GetTypeInfo().AsType() == typeof(int[])) passed |= 128;
                    return passed;
                }
            }
            """,
            TestAssets.RepositorySdkVersion,
            "latest",
            optimized);

        var compilation = NetWasmCompiler.Compile(new CompilerOptions(
            assembly, [assets.CoreLib], "TypeInfoFixture.EntryPoint", "Run", [], Target: target));

        Assert.Equal(255, ExecuteWithStandardWasiNode(
            compilation.ApplicationModule, assets.Directory, 0, target, compilation.StaticDataEnd));
    }

    // Generic parameters and ref structs answer as .NET 11 does: a parameter's position, its variance
    // and special constraints, a struct-constrained parameter is a value type based on ValueType and
    // any other a class based on Object; a ref struct is by-ref-like.
    [Theory]
    [InlineData(false, WasmTarget.Wasm32)]
    [InlineData(true, WasmTarget.Wasm64)]
    public void CompilerExecutesGenericParameterFacts(bool optimized, WasmTarget target)
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSourceWithCompiler(
            "GenericParameterFixture",
            """
            using System;
            using System.Reflection;

            namespace GenericParameterFixture;

            public interface IShape { }
            public interface IVariant<in TIn, out TOut> { }
            public sealed class Holder<TFree, TStruct, TClass, TIface, TUnmanaged>
                where TStruct : struct where TClass : class where TIface : IShape where TUnmanaged : unmanaged { }
            public ref struct Window { public int Start; }
            public struct Plain { public int Value; }

            public static class EntryPoint
            {
                public static int Run(int input)
                {
                    Type[] parameters = typeof(Holder<,,,,>).GetGenericArguments();
                    int passed = 0;
                    bool positions = true;
                    for (int index = 0; index < parameters.Length; index++)
                    {
                        positions &= parameters[index].GenericParameterPosition == index;
                    }
                    if (positions && parameters.Length == 5) passed |= 1;
                    if (parameters[0].IsClass && !parameters[0].IsValueType && parameters[0].BaseType == typeof(object)) passed |= 2;
                    if (!parameters[1].IsClass && parameters[1].IsValueType && parameters[1].BaseType == typeof(ValueType)) passed |= 4;
                    if (parameters[2].GenericParameterAttributes == GenericParameterAttributes.ReferenceTypeConstraint &&
                        parameters[1].GenericParameterAttributes == (GenericParameterAttributes.NotNullableValueTypeConstraint |
                            GenericParameterAttributes.DefaultConstructorConstraint) &&
                        parameters[4].GenericParameterAttributes == parameters[1].GenericParameterAttributes &&
                        parameters[0].GenericParameterAttributes == GenericParameterAttributes.None) passed |= 8;
                    Type[] variant = typeof(IVariant<,>).GetGenericArguments();
                    if (variant[0].GenericParameterAttributes == GenericParameterAttributes.Contravariant &&
                        variant[1].GenericParameterAttributes == GenericParameterAttributes.Covariant) passed |= 16;
                    if (typeof(Window).IsByRefLike && typeof(Span<int>).IsByRefLike && !typeof(Plain).IsByRefLike &&
                        !typeof(string).IsByRefLike) passed |= 32;
                    try { _ = typeof(int).GenericParameterPosition; } catch (InvalidOperationException) { passed |= 64; }
                    if (parameters[3].IsClass && parameters[3].BaseType == typeof(object) && parameters[3].DeclaringMethod == null) passed |= 128;
                    return passed;
                }
            }
            """,
            TestAssets.RepositorySdkVersion,
            "latest",
            optimized);

        var compilation = NetWasmCompiler.Compile(new CompilerOptions(
            assembly, [assets.CoreLib], "GenericParameterFixture.EntryPoint", "Run", [], Target: target));

        Assert.Equal(255, ExecuteWithStandardWasiNode(
            compilation.ApplicationModule, assets.Directory, 0, target, compilation.StaticDataEnd));
    }

    // GetElementType from the type table, as .NET 11 answers it: an array's, a jagged array's and a
    // pointer's element, also beside DeclaringType, which shares the slot; nothing for other types.
    [Theory]
    [InlineData(false, WasmTarget.Wasm32)]
    [InlineData(true, WasmTarget.Wasm64)]
    public void CompilerExecutesElementTypes(bool optimized, WasmTarget target)
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSourceWithCompiler(
            "ElementTypesFixture",
            """
            namespace ElementTypesFixture;

            public sealed class Outer
            {
                public sealed class Inner { }
            }

            public static class EntryPoint
            {
                public static unsafe int Run(int input)
                {
                    int passed = 0;
                    if (new int[1].GetType().GetElementType() == typeof(int)) passed |= 1;
                    if (typeof(Outer.Inner[]).GetElementType() == typeof(Outer.Inner) &&
                        typeof(Outer.Inner[]).DeclaringType == null && typeof(Outer.Inner).DeclaringType == typeof(Outer)) passed |= 2;
                    if (typeof(int[][]).GetElementType() == typeof(int[]) && typeof(string[,]).GetElementType() == typeof(string)) passed |= 4;
                    if (typeof(int*).GetElementType() == typeof(int) && new int?[1].GetType().GetElementType() == typeof(int?)) passed |= 8;
                    if (typeof(int).GetElementType() == null && typeof(Outer).GetElementType() == null) passed |= 16;
                    return passed;
                }
            }
            """,
            TestAssets.RepositorySdkVersion,
            "latest",
            optimized,
            allowUnsafe: true);

        var compilation = NetWasmCompiler.Compile(new CompilerOptions(
            assembly, [assets.CoreLib], "ElementTypesFixture.EntryPoint", "Run", [], Target: target));

        Assert.Equal(31, ExecuteWithStandardWasiNode(
            compilation.ApplicationModule, assets.Directory, 0, target, compilation.StaticDataEnd));
        Assert.True(compilation.Program.RequiresElementTypes);
    }

    // GetGenericTypeDefinition from the type table, as .NET 11 answers it: a constructed type's
    // open definition, also where code never names the definition, and the definition itself for
    // a definition. The arguments still read past the definition in the type's generic list.
    // MakeGenericType on a type that is not a definition throws as .NET's does.
    [Theory]
    [InlineData(false, WasmTarget.Wasm32)]
    [InlineData(true, WasmTarget.Wasm64)]
    public void CompilerExecutesGenericTypeDefinitions(bool optimized, WasmTarget target)
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSourceWithCompiler(
            "GenericDefinitionsFixture",
            """
            using System;
            using System.Collections.Generic;

            namespace GenericDefinitionsFixture;

            public sealed class Outer<T>
            {
                public sealed class Inner<U> { }
            }

            public sealed class Sample { }

            public static class EntryPoint
            {
                // TUnit's data source helper: a Func<T> value runs for its result.
                private static object? InvokeIfFunc(object? value)
                {
                    if (value == null) return null;
                    var type = value.GetType();
                    if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Func<>))
                    {
                        return ((Delegate)value).DynamicInvoke();
                    }
                    return value;
                }

                public static int Run(int input)
                {
                    object list = new List<int>();
                    object map = new Dictionary<string, int>();
                    int passed = 0;
                    if (list.GetType().GetGenericTypeDefinition() == typeof(List<>)) passed |= 1;
                    Type definition = map.GetType().GetGenericTypeDefinition();
                    if (definition.IsGenericTypeDefinition && definition.GetGenericArguments().Length == 2 &&
                        map.GetType().GetGenericArguments()[0] == typeof(string)) passed |= 2;
                    if (typeof(List<>).GetGenericTypeDefinition() == typeof(List<>)) passed |= 4;
                    if (typeof(int?).GetGenericTypeDefinition() == typeof(Nullable<>)) passed |= 8;
                    if (typeof(Outer<int>.Inner<string>).GetGenericTypeDefinition() == typeof(Outer<>.Inner<>)) passed |= 16;
                    try
                    {
                        typeof(int).GetGenericTypeDefinition();
                    }
                    catch (InvalidOperationException)
                    {
                        passed |= 32;
                    }
                    if (InvokeIfFunc(new Func<Sample>(() => new Sample())) is Sample && InvokeIfFunc("text") is "text") passed |= 64;
                    if (new List<string>().GetType().GetGenericTypeDefinition() != typeof(Dictionary<,>)) passed |= 128;
                    try { typeof(List<int>).MakeGenericType(typeof(int)); } catch (InvalidOperationException) { passed |= 256; }
                    try { typeof(List<>).MakeGenericType(null!); } catch (ArgumentNullException) { passed |= 512; }
                    return passed;
                }
            }
            """,
            TestAssets.RepositorySdkVersion,
            "latest",
            optimized);

        var compilation = NetWasmCompiler.Compile(new CompilerOptions(
            assembly, [assets.CoreLib], "GenericDefinitionsFixture.EntryPoint", "Run", [], Target: target));

        Assert.Equal(1023, ExecuteWithStandardWasiNode(
            compilation.ApplicationModule, assets.Directory, 0, target, compilation.StaticDataEnd));
        Assert.True(compilation.Program.RequiresGenericDefinitions);
    }

    // A RuntimeTypeHandle holds its whole type ID through every copy, so a program with more than
    // 255 types round-trips handles stored in arrays and fields, and builds a lambda whose delegate
    // type's ID is past 255; the handle was a 1-byte struct whose copies kept only the ID's low byte.
    [Theory]
    [InlineData(FinalWasmOptimization.O0, WasmTarget.Wasm32)]
    [InlineData(FinalWasmOptimization.Oz, WasmTarget.Wasm64)]
    public void TypeHandlesKeepTheirWholeIdPastTheFirst256Types(FinalWasmOptimization optimization, WasmTarget target)
    {
        const int count = 300;
        var classes = string.Join("\n", Enumerable.Range(0, count).Select(index => $"public sealed class C{index} {{ }}"));
        // Each class also gives a lambda its delegate type, some of whose IDs pass 255.
        var handles = string.Join(", ", Enumerable.Range(0, count).Select(index => $"typeof(C{index}).TypeHandle"));
        var types = string.Join(", ", Enumerable.Range(0, count).Select(index => $"typeof(C{index})"));
        var lambdas = string.Join(" | ", Enumerable.Range(0, count).Select(index => $"Lambda<C{index}>(input)"));
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSourceWithCompiler(
            "TypeHandlesFixture",
            "using System;\nusing System.Linq.Expressions;\nnamespace TypeHandlesFixture;\n" + classes + $$"""

            public static class EntryPoint
            {
                private static RuntimeTypeHandle s_last;

                // 16 where the lambda's delegate type has an ID past 255, 8 where it does not run.
                private static int Lambda<T>(int input) where T : new()
                {
                    Expression<Func<T, int>> lambda = value => input;
                    return (typeof(Func<T, int>).TypeHandle.Value > 255 ? 16 : 0) |
                        (lambda.Compile()(new T()) == input ? 0 : 8);
                }

                public static int Run(int input)
                {
                    RuntimeTypeHandle[] handles = { {{handles}} };
                    Type[] types = { {{types}} };
                    int wrong = 0, past255 = 0;
                    for (int index = 0; index < handles.Length; index++)
                    {
                        if (Type.GetTypeFromHandle(handles[index]) != types[index] ||
                            handles[index].Value != types[index].TypeHandle.Value) wrong |= 1;
                        if (handles[index].Value > 255) past255++;
                    }
                    s_last = handles[handles.Length - 1];
                    if (Type.GetTypeFromHandle(s_last) != typeof(C{{count - 1}})) wrong |= 2;
                    var lambdas = {{lambdas}};
                    if ((lambdas & 16) == 0) wrong |= 4;
                    return past255 > 0 ? wrong | (lambdas & 8) : -1;
                }
            }
            """,
            TestAssets.RepositorySdkVersion,
            "latest",
            optimize: false);

        // At O0 the expression program reaches a WASI 0.3 platform import, which needs the world an SDK build has.
        var compilation = NetWasmCompiler.Compile(new CompilerOptions(
            assembly, [assets.CoreLib], "TypeHandlesFixture.EntryPoint", "Run",
            [new("run", "TypeHandlesFixture.EntryPoint", "Run")], Target: target,
            WitPath: Path.Combine(assets.Root, "wit", "netwasm-platform-1.0.0"),
            WitWorld: "netwasm:platform/platform@1.0.0",
            Optimization: optimization));

        Assert.Equal(0, ExecuteWithStandardWasiNode(
            compilation.ApplicationModule, assets.Directory, 7, target, compilation.StaticDataEnd));
    }

    // Type.Assembly returns one placeholder per assembly, from the type table: equal for types of one
    // assembly (an array's and a constructed type's are their element's and definition's), different across
    // assemblies; GetExecutingAssembly is the calling method's and GetEntryAssembly the entry point's.
    // Without the type-symbols sidecar every assembly name reads as unavailable.
    [Theory]
    [InlineData(false, WasmTarget.Wasm32)]
    [InlineData(true, WasmTarget.Wasm64)]
    public void CompilerExecutesTypeAssembly(bool optimized, WasmTarget target)
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSourceWithCompiler(
            "AssembliesFixture",
            """
            using System;
            using System.Collections.Generic;
            using System.Reflection;

            namespace AssembliesFixture;

            public sealed class Sample { }

            public static class EntryPoint
            {
                public static int Run(int input)
                {
                    Assembly own = typeof(Sample).Assembly;
                    Assembly core = typeof(int).Assembly;
                    int passed = 0;
                    if (own == typeof(EntryPoint).Assembly && own == typeof(Sample[]).Assembly) passed |= 1;
                    if (core == typeof(string).Assembly && core == typeof(List<Sample>).Assembly && core != own) passed |= 2;
                    if (Assembly.GetExecutingAssembly() == own && Assembly.GetEntryAssembly() == own) passed |= 4;
                    var byAssembly = new Dictionary<Assembly, int> { [own] = 1, [core] = 2 };
                    if (byAssembly[new Sample().GetType().Assembly] == 1 && byAssembly[(5).GetType().Assembly] == 2) passed |= 8;
                    if (own.FullName == "<Assembly name unavailable>" && own.GetName().Name == "<Assembly name unavailable>") passed |= 16;
                    if (typeof(Sample).AssemblyQualifiedName!.EndsWith(", <Assembly name unavailable>", StringComparison.Ordinal)) passed |= 32;
                    if (own.Equals(typeof(Sample).Assembly) && own.ToString() == own.FullName) passed |= 64;
                    if (Assembly.GetAssembly(typeof(Sample)) == own && Assembly.GetAssembly(typeof(int)) == core) passed |= 128;
                    try { Assembly.GetAssembly(null!); } catch (ArgumentNullException) { passed |= 256; }
            #pragma warning disable SYSLIB0005
                    if (!own.ReflectionOnly && !own.GlobalAssemblyCache && own.HostContext == 0) passed |= 512;
            #pragma warning restore SYSLIB0005
                    if (own.Location.Length == 0 && own.ImageRuntimeVersion == "v4.0.30319") passed |= 1024;
                    return passed;
                }
            }
            """,
            TestAssets.RepositorySdkVersion,
            "latest",
            optimized);

        var compilation = NetWasmCompiler.Compile(new CompilerOptions(
            assembly, [assets.CoreLib], "AssembliesFixture.EntryPoint", "Run", [], Target: target));

        Assert.Equal(2047, ExecuteWithStandardWasiNode(
            compilation.ApplicationModule, assets.Directory, 0, target, compilation.StaticDataEnd));
        Assert.True(compilation.Program.RequiresAssemblies);
    }

    // The type table carries generic arguments, generic definitions (in its shapes), assemblies and
    // declaring or element types only for a program that can ask for them; every program's table
    // registers its types.
    [Theory]
    [InlineData("typeof(int).IsValueType && input.GetType().IsValueType", false, false, false, false, false)]
    [InlineData("input.GetType().GetGenericArguments().Length == 0", true, false, false, false, false)]
    [InlineData("input.GetType().DeclaringType == null", false, true, false, false, false)]
    [InlineData("!input.GetType().IsGenericType || input.GetType().GetGenericTypeDefinition() == null", false, false, true, false, false)]
    [InlineData("input.GetType().Assembly == typeof(string).Assembly", false, false, false, true, false)]
    [InlineData("input.GetType().GetElementType() == null", false, false, false, false, true)]
    [InlineData("input.GetType().IsPublic", false, false, false, false, false, true)]
    public void TheTypeTableCarriesOnlyTheSectionsAProgramCanQuery(
        string query, bool generic, bool declaring, bool definitions, bool assemblies, bool elements, bool attributes = false)
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSource("TypeTableSections", $$"""
            public static class EntryPoint
            {
                public static int Run(int input) => {{query}} ? input + 1 : 0;
            }
            """);
        var compilation = NetWasmCompiler.Compile(new CompilerOptions(
            assembly, [assets.CoreLib], "EntryPoint", "Run", [], Target: WasmTarget.Wasm32));
        var data = compilation.Layouts.StaticData;
        var table = new NetWasm.Compiler.Tests.Layout.RuntimeTypeTableReader([.. Assert.Single(data.Segments,
            segment => segment.Address == data.TypeTableAddress).Data, .. new byte[8]]);

        Assert.Equal(42, ExecuteWithStandardWasiNode(
            compilation.ApplicationModule, assets.Directory, 41, WasmTarget.Wasm32, compilation.StaticDataEnd));
        Assert.Equal(generic, table.Header(17) != 0);
        Assert.Equal(declaring || elements, table.Header(19) != 0);
        Assert.Equal(elements, compilation.Program.RequiresElementTypes);
        Assert.Equal(attributes, compilation.Program.RequiresTypeAttributes);
        Assert.Equal(attributes, table.Header(5) > 640 + 32 * table.Header(4));
        Assert.Equal(definitions, table.DefinitionBits != 0);
        Assert.Equal(assemblies, table.AssemblyBits != 0);
        Assert.True(table.Capacity > 1);
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
                VoidRuntimeTypeSource, cancellationToken: TestContext.Current.CancellationToken)],
            references,
            new global::Microsoft.CodeAnalysis.CSharp.CSharpCompilationOptions(
                global::Microsoft.CodeAnalysis.OutputKind.DynamicallyLinkedLibrary));
        using var assemblyStream = new MemoryStream();
        var emit = compilation.Emit(assemblyStream, cancellationToken: TestContext.Current.CancellationToken);
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

        Assert.Equal(65535, ExecuteWithStandardWasiNode(result.ApplicationModule, assets.Directory, 41, WasmTarget.Wasm32, result.StaticDataEnd));
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
            TestAssets.RepositorySdkVersion,
            "latest",
            optimized);
        var compilation = NetWasmCompiler.Compile(new CompilerOptions(
            assembly,
            [assets.CoreLib],
            "RuntimeTypeNamesFixture.EntryPoint",
            "Run",
            [new RequestedExport("run", "RuntimeTypeNamesFixture.EntryPoint", "Run")],
            Target: target,
            WitPath: Path.Combine(assets.Root, "wit", "netwasm-platform-1.0.0"),
            WitWorld: "netwasm:platform/platform@1.0.0",
            EmitTypeNames: true));

        // The names come from the type-symbols sidecar; the module carries none (issue 225), so
        // without the sidecar it keeps running with every name read as a type ID.
        Assert.Equal(63, RunWithTypeSymbols(compilation, assets.Directory, target));
        Assert.Equal(0, RunWithTypeSymbols(compilation, assets.Directory, target, loadSidecar: false));
    }

    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void BaseVirtualCallsReadTheSidecarNames(WasmTarget target)
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
            [new RequestedExport("run", "RuntimeTypeNamesViaBaseCallsFixture.EntryPoint", "Run")],
            Target: target,
            WitPath: Path.Combine(assets.Root, "wit", "netwasm-platform-1.0.0"),
            WitWorld: "netwasm:platform/platform@1.0.0",
            EmitTypeNames: true));

        Assert.Equal(
            (expectedName.Length * 100) + expectedDisplayName.Length,
            RunWithTypeSymbols(compilation, assets.Directory, target));
        Assert.DoesNotContain(expectedName, compilation.Layouts.StaticData.Strings.Keys);
        Assert.DoesNotContain(expectedDisplayName, compilation.Layouts.StaticData.Strings.Keys);
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

        // Without names a type has no namespace, as a global type has none.
        Assert.Equal(
            1,
            ExecuteWithStandardWasiNode(
                compilation.ApplicationModule,
                assets.Directory,
                0,
                target,
                compilation.StaticDataEnd));
        Assert.Equal(1, RunWithTypeSymbols(NetWasmCompiler.Compile(new CompilerOptions(
            assembly,
            [assets.CoreLib],
            "EntryPoint",
            "Run",
            [new RequestedExport("run", "EntryPoint", "Run")],
            Target: target,
            WitPath: Path.Combine(assets.Root, "wit", "netwasm-platform-1.0.0"),
            WitWorld: "netwasm:platform/platform@1.0.0",
            EmitTypeNames: true)), assets.Directory, target));
        Assert.DoesNotContain("EntryPoint", compilation.Layouts.StaticData.Strings.Keys);
    }

    // Reading the sidecar takes wasi:filesystem, which the platform world provides.
    private static int RunWithTypeSymbols(
        CompilationResult compilation, string directory, WasmTarget target, bool loadSidecar = true) =>
        ExecuteWithStandardWasiNode(
            compilation.ApplicationModule,
            directory,
            0,
            target,
            compilation.StaticDataEnd,
            System.Collections.Immutable.ImmutableDictionary<string, string>.Empty,
            timeZoneAssetPath: null,
            typeSymbols: loadSidecar ? compilation.TypeSymbols!.Bytes : null);


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
