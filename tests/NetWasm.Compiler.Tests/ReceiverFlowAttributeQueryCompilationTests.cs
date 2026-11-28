// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.ComponentModel;
using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;
using static NetWasm.Compiler.Tests.CompilerTestSupport;

namespace NetWasm.Compiler.Tests;

// An attribute query on a Type known only at run time is answered after the receiver flow, by a switch over the
// program's run-time types that builds the attributes and their array. A virtual or interface call on what each form
// returns (the generic extensions' IEnumerable<T>, Type's and the extensions' arrays and enumerables, Attribute's
// statics, a single attribute) reaches the targets of those objects at every level, with and without the receiver flow
// audit; the IEnumerable forms had none at O1 and above (the flow saw no receiver, so the foreach's calls were thrown
// away) where O0 ran. The values are desktop .NET 11's.
public sealed class ReceiverFlowAttributeQueryCompilationTests
{
    private const string Source = """
        using System;
        using System.Collections.Generic;
        using System.Reflection;

        namespace AttributeResults;

        public interface IWeighed { int Weight(); }

        public abstract class WeighedAttribute : Attribute, IWeighed { public abstract int Weight(); }

        [AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
        public sealed class OneAttribute(int weight) : WeighedAttribute { public override int Weight() => weight; }

        [AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
        public sealed class TwoAttribute(int weight) : WeighedAttribute { public override int Weight() => weight * 10; }

        [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
        public sealed class SoloAttribute : WeighedAttribute { public override int Weight() => 500; }

        [One(1), One(2), Two(3), Solo]
        public class Root : IComparable<Root>, IWeighed { public int CompareTo(Root? other) => 0; public virtual int Weight() => 1; public override string ToString() => "root"; }

        public sealed class Leaf : Root { public override int Weight() => 2; public override string ToString() => "leafy"; }

        public enum Shade { Light, Dark, Darker }

        public sealed class Holder { public Type Type = typeof(object); }

        public static class EntryPoint
        {
            private static int Form(int form, Type type)
            {
                var sum = 0;
                switch (form)
                {
                    case 1:
                        foreach (OneAttribute attribute in type.GetCustomAttributes<OneAttribute>()) sum += attribute.Weight();
                        break;
                    case 2:
                        foreach (WeighedAttribute attribute in type.GetCustomAttributes<WeighedAttribute>(inherit: false)) sum += attribute.Weight();
                        break;
                    case 3:
                        foreach (IWeighed attribute in CustomAttributeExtensions.GetCustomAttributes(type, typeof(WeighedAttribute), false)) sum += attribute.Weight();
                        break;
                    case 4:
                        foreach (var attribute in type.GetCustomAttributes(typeof(WeighedAttribute), false)) sum += ((IWeighed)attribute).Weight();
                        break;
                    case 5:
                        foreach (var attribute in Attribute.GetCustomAttributes(type, typeof(WeighedAttribute))) sum += ((WeighedAttribute)attribute).Weight();
                        break;
                    case 6:
                        IWeighed? solo = type.GetCustomAttribute<SoloAttribute>(inherit: false);
                        sum += solo?.Weight() ?? 0;
                        break;
                    case 7:
                        if (type != typeof(int)) foreach (var implemented in type.GetInterfaces()) sum += implemented.IsInterface ? 1 : 100;
                        break;
                    case 8:
                        foreach (var value in Enum.GetValues(typeof(Shade))) sum += value.ToString()!.Length + ((IComparable)value).CompareTo(Shade.Light) * 10;
                        break;
                    case 9:
                        foreach (var value in Enum.GetValues<Shade>()) sum += value.ToString().Length;
                        break;
                    case 10:
                        foreach (var name in Enum.GetNames(typeof(Shade))) sum += name.GetHashCode() == 0 ? 0 : name.Length;
                        break;
                    case 12:
                        foreach (var attribute in (IEnumerable<object>)type.GetCustomAttributes(typeof(WeighedAttribute), false)) sum += ((IWeighed)attribute).Weight();
                        break;
                    case 11:
                        var made = Array.CreateInstance(type == typeof(object) ? typeof(Root) : typeof(Leaf), 2);
                        made.SetValue(type == typeof(object) ? new Root() : new Leaf(), 0);
                        foreach (var element in made) sum += element?.GetHashCode() is int hash && element.ToString()!.Length > 0 ? 1 : 0;
                        break;
                    default:
                        break;
                }
                return sum;
            }

            public static int Run(int input)
            {
                var holders = new[] { new Holder { Type = typeof(Root) }, new Holder { Type = typeof(int) }, new Holder() };
                var total = 0;
                foreach (var holder in holders) total = total * 10 + Form(input, holder.Type);
                return total;
            }
        }
        """;

    // Each query form (the input), on Root, int and object: desktop .NET 11's values. Forms 1-6 and 12 (a dispatched
    // call on Type's own query's array) are the attribute queries; 7-11 are their siblings among the queries the
    // runtime answers (a type's interfaces, an enum's values and names, an array made for a run-time element type).
    private static readonly int[] Expected = [0, 300, 53300, 53300, 53300, 53300, 50000, 200, 3885, 1665, 1665, 111, 53300];

    public static TheoryData<WasmTarget, FinalWasmOptimization, int> Cases()
    {
        var data = new TheoryData<WasmTarget, FinalWasmOptimization, int>();
        foreach (var (target, optimization) in new[]
        {
            (WasmTarget.Wasm32, FinalWasmOptimization.O0), (WasmTarget.Wasm32, FinalWasmOptimization.O1),
            (WasmTarget.Wasm32, FinalWasmOptimization.O3), (WasmTarget.Wasm32, FinalWasmOptimization.Os),
            (WasmTarget.Wasm32, FinalWasmOptimization.Oz), (WasmTarget.Wasm64, FinalWasmOptimization.O1),
            (WasmTarget.Wasm64, FinalWasmOptimization.O3), (WasmTarget.Wasm64, FinalWasmOptimization.Os),
            (WasmTarget.Wasm64, FinalWasmOptimization.Oz),
        })
        {
            for (var form = 1; form < Expected.Length; form++) data.Add(target, optimization, form);
        }
        return data;
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void VirtualCallsOnARunTimeTypesAttributesReachTheirTargets(WasmTarget target, FinalWasmOptimization optimization,
        int form)
    {
        var (module, staticDataEnd, directory) = Compiled(target, optimization);
        Assert.Equal(Expected[form], ExecuteWithStandardWasiNode(module, directory, form, target, staticDataEnd));
    }

    public static TheoryData<FinalWasmOptimization, int> AuditedCases()
    {
        var data = new TheoryData<FinalWasmOptimization, int>();
        foreach (var optimization in new[] { FinalWasmOptimization.O1, FinalWasmOptimization.Oz })
        {
            for (var form = 1; form < Expected.Length; form++) data.Add(optimization, form);
        }
        return data;
    }

    // Under the receiver flow audit, every receiver of a call whose targets the flow chose must be one of them: the
    // query's bounded result admits the array and attributes its answer builds.
    [Theory]
    [MemberData(nameof(AuditedCases))]
    public void UnderTheAuditTheirCallsReachTheAnswersObjects(FinalWasmOptimization optimization, int form)
    {
        var (module, staticDataEnd, directory) = Compiled(WasmTarget.Wasm32, optimization, audit: true);
        Assert.Equal(Expected[form], ExecuteWithStandardWasiNode(module, directory, form, WasmTarget.Wasm32, staticDataEnd));
    }

    // Each query whose answer the compile builds after the first closure, alone in its program, its result walked
    // through an interface (so the foreach dispatches GetEnumerator, then MoveNext and Current on the enumerator) and
    // a virtual call made on each element: nothing else constructs the answer's array, its enumerator or its element
    // type. Attribute retrievals (generic, the typed extension, Type's own, Attribute's statics), a type's interfaces,
    // an enum's values (by Type and generic) and names, and an array made for a run-time element type. System.Text.Json's
    // JsonPolymorphismOptions.CreateFromAttributeDeclarations has the first shape. Values: desktop .NET 11's.
    private const string GenericAlone = """
        using System;
        using System.Collections;
        using System.Collections.Generic;
        using System.Reflection;

        namespace SweepGeneric;

        [AttributeUsage(AttributeTargets.Class | AttributeTargets.Interface, AllowMultiple = true, Inherited = false)]
        public class DerivedAttribute : Attribute
        {
            public DerivedAttribute(Type type) { DerivedType = type; }
            public Type DerivedType { get; }
            public override string ToString() => DerivedType == typeof(Leaf) ? "leaf" : "root!";
        }

        public interface IShaped { int Corners(); }

        [Derived(typeof(Leaf)), Derived(typeof(Root))]
        public class Root : IShaped { public virtual int Corners() => 3; }

        public sealed class Leaf : Root { public override int Corners() => 4; }

        public enum Shade { Light, Dark, Darker }

        public sealed class Holder { public Type Type = typeof(object); }

        public static class EntryPoint
        {
            private static int Count(Type type)
            {
                var sum = 0;
                foreach (DerivedAttribute attribute in type.GetCustomAttributes<DerivedAttribute>(inherit: false)) sum += attribute.ToString()!.Length;
                return sum;
            }

            public static int Run(int input)
            {
                var holders = new[] { new Holder { Type = typeof(Root) }, new Holder(), new Holder { Type = typeof(List<int>) } };
                var total = 0;
                foreach (var holder in holders) total = total * 100 + Count(holder.Type) % 100;
                return total + input;
            }
        }
        """;
    private const string TypedAlone = """
        using System;
        using System.Collections;
        using System.Collections.Generic;
        using System.Reflection;

        namespace SweepTyped;

        [AttributeUsage(AttributeTargets.Class | AttributeTargets.Interface, AllowMultiple = true, Inherited = false)]
        public class DerivedAttribute : Attribute
        {
            public DerivedAttribute(Type type) { DerivedType = type; }
            public Type DerivedType { get; }
            public override string ToString() => DerivedType == typeof(Leaf) ? "leaf" : "root!";
        }

        public interface IShaped { int Corners(); }

        [Derived(typeof(Leaf)), Derived(typeof(Root))]
        public class Root : IShaped { public virtual int Corners() => 3; }

        public sealed class Leaf : Root { public override int Corners() => 4; }

        public enum Shade { Light, Dark, Darker }

        public sealed class Holder { public Type Type = typeof(object); }

        public static class EntryPoint
        {
            private static int Count(Type type)
            {
                var sum = 0;
                foreach (var attribute in CustomAttributeExtensions.GetCustomAttributes(type, typeof(DerivedAttribute), false)) sum += attribute.ToString()!.Length;
                return sum;
            }

            public static int Run(int input)
            {
                var holders = new[] { new Holder { Type = typeof(Root) }, new Holder(), new Holder { Type = typeof(List<int>) } };
                var total = 0;
                foreach (var holder in holders) total = total * 100 + Count(holder.Type) % 100;
                return total + input;
            }
        }
        """;
    private const string InstanceAlone = """
        using System;
        using System.Collections;
        using System.Collections.Generic;
        using System.Reflection;

        namespace SweepInstance;

        [AttributeUsage(AttributeTargets.Class | AttributeTargets.Interface, AllowMultiple = true, Inherited = false)]
        public class DerivedAttribute : Attribute
        {
            public DerivedAttribute(Type type) { DerivedType = type; }
            public Type DerivedType { get; }
            public override string ToString() => DerivedType == typeof(Leaf) ? "leaf" : "root!";
        }

        public interface IShaped { int Corners(); }

        [Derived(typeof(Leaf)), Derived(typeof(Root))]
        public class Root : IShaped { public virtual int Corners() => 3; }

        public sealed class Leaf : Root { public override int Corners() => 4; }

        public enum Shade { Light, Dark, Darker }

        public sealed class Holder { public Type Type = typeof(object); }

        public static class EntryPoint
        {
            private static int Count(Type type)
            {
                var sum = 0;
                foreach (var attribute in (IEnumerable<object>)type.GetCustomAttributes(typeof(DerivedAttribute), false)) sum += attribute.ToString()!.Length;
                return sum;
            }

            public static int Run(int input)
            {
                var holders = new[] { new Holder { Type = typeof(Root) }, new Holder(), new Holder { Type = typeof(List<int>) } };
                var total = 0;
                foreach (var holder in holders) total = total * 100 + Count(holder.Type) % 100;
                return total + input;
            }
        }
        """;
    private const string StaticsAlone = """
        using System;
        using System.Collections;
        using System.Collections.Generic;
        using System.Reflection;

        namespace SweepStatics;

        [AttributeUsage(AttributeTargets.Class | AttributeTargets.Interface, AllowMultiple = true, Inherited = false)]
        public class DerivedAttribute : Attribute
        {
            public DerivedAttribute(Type type) { DerivedType = type; }
            public Type DerivedType { get; }
            public override string ToString() => DerivedType == typeof(Leaf) ? "leaf" : "root!";
        }

        public interface IShaped { int Corners(); }

        [Derived(typeof(Leaf)), Derived(typeof(Root))]
        public class Root : IShaped { public virtual int Corners() => 3; }

        public sealed class Leaf : Root { public override int Corners() => 4; }

        public enum Shade { Light, Dark, Darker }

        public sealed class Holder { public Type Type = typeof(object); }

        public static class EntryPoint
        {
            private static int Count(Type type)
            {
                var sum = 0;
                foreach (var attribute in (IEnumerable<Attribute>)Attribute.GetCustomAttributes(type, typeof(DerivedAttribute))) sum += attribute.ToString()!.Length;
                return sum;
            }

            public static int Run(int input)
            {
                var holders = new[] { new Holder { Type = typeof(Root) }, new Holder(), new Holder { Type = typeof(List<int>) } };
                var total = 0;
                foreach (var holder in holders) total = total * 100 + Count(holder.Type) % 100;
                return total + input;
            }
        }
        """;
    private const string InterfacesAlone = """
        using System;
        using System.Collections;
        using System.Collections.Generic;
        using System.Reflection;

        namespace SweepInterfaces;

        [AttributeUsage(AttributeTargets.Class | AttributeTargets.Interface, AllowMultiple = true, Inherited = false)]
        public class DerivedAttribute : Attribute
        {
            public DerivedAttribute(Type type) { DerivedType = type; }
            public Type DerivedType { get; }
            public override string ToString() => DerivedType == typeof(Leaf) ? "leaf" : "root!";
        }

        public interface IShaped { int Corners(); }

        [Derived(typeof(Leaf)), Derived(typeof(Root))]
        public class Root : IShaped { public virtual int Corners() => 3; }

        public sealed class Leaf : Root { public override int Corners() => 4; }

        public enum Shade { Light, Dark, Darker }

        public sealed class Holder { public Type Type = typeof(object); }

        public static class EntryPoint
        {
            private static int Count(Type type)
            {
                var sum = 0;
                foreach (var implemented in (IEnumerable<Type>)type.GetInterfaces()) sum += implemented.IsInterface ? 1 : 50;
                return sum;
            }

            public static int Run(int input)
            {
                var holders = new[] { new Holder { Type = typeof(Root) }, new Holder(), new Holder { Type = typeof(Leaf) } };
                var total = 0;
                foreach (var holder in holders) total = total * 100 + Count(holder.Type) % 100;
                return total + input;
            }
        }
        """;
    private const string EnumvaluesAlone = """
        using System;
        using System.Collections;
        using System.Collections.Generic;
        using System.Reflection;

        namespace SweepEnumvalues;

        [AttributeUsage(AttributeTargets.Class | AttributeTargets.Interface, AllowMultiple = true, Inherited = false)]
        public class DerivedAttribute : Attribute
        {
            public DerivedAttribute(Type type) { DerivedType = type; }
            public Type DerivedType { get; }
            public override string ToString() => DerivedType == typeof(Leaf) ? "leaf" : "root!";
        }

        public interface IShaped { int Corners(); }

        [Derived(typeof(Leaf)), Derived(typeof(Root))]
        public class Root : IShaped { public virtual int Corners() => 3; }

        public sealed class Leaf : Root { public override int Corners() => 4; }

        public enum Shade { Light, Dark, Darker }

        public sealed class Holder { public Type Type = typeof(object); }

        public static class EntryPoint
        {
            private static int Count(Type type)
            {
                var sum = 0;
                if (type.IsEnum) foreach (var value in (IEnumerable)Enum.GetValues(type)) sum += value.ToString()!.Length;
                return sum;
            }

            public static int Run(int input)
            {
                var holders = new[] { new Holder { Type = typeof(Root) }, new Holder(), new Holder { Type = typeof(Shade) } };
                var total = 0;
                foreach (var holder in holders) total = total * 100 + Count(holder.Type) % 100;
                return total + input;
            }
        }
        """;
    private const string EnumvaluesgenericAlone = """
        using System;
        using System.Collections;
        using System.Collections.Generic;
        using System.Reflection;

        namespace SweepEnumvaluesgeneric;

        [AttributeUsage(AttributeTargets.Class | AttributeTargets.Interface, AllowMultiple = true, Inherited = false)]
        public class DerivedAttribute : Attribute
        {
            public DerivedAttribute(Type type) { DerivedType = type; }
            public Type DerivedType { get; }
            public override string ToString() => DerivedType == typeof(Leaf) ? "leaf" : "root!";
        }

        public interface IShaped { int Corners(); }

        [Derived(typeof(Leaf)), Derived(typeof(Root))]
        public class Root : IShaped { public virtual int Corners() => 3; }

        public sealed class Leaf : Root { public override int Corners() => 4; }

        public enum Shade { Light, Dark, Darker }

        public sealed class Holder { public Type Type = typeof(object); }

        public static class EntryPoint
        {
            private static int Count(Type type)
            {
                var sum = 0;
                if (type.IsEnum) foreach (var value in (IEnumerable<Shade>)Enum.GetValues<Shade>()) sum += value.ToString().Length;
                return sum;
            }

            public static int Run(int input)
            {
                var holders = new[] { new Holder { Type = typeof(Root) }, new Holder(), new Holder { Type = typeof(Shade) } };
                var total = 0;
                foreach (var holder in holders) total = total * 100 + Count(holder.Type) % 100;
                return total + input;
            }
        }
        """;
    private const string EnumnamesAlone = """
        using System;
        using System.Collections;
        using System.Collections.Generic;
        using System.Reflection;

        namespace SweepEnumnames;

        [AttributeUsage(AttributeTargets.Class | AttributeTargets.Interface, AllowMultiple = true, Inherited = false)]
        public class DerivedAttribute : Attribute
        {
            public DerivedAttribute(Type type) { DerivedType = type; }
            public Type DerivedType { get; }
            public override string ToString() => DerivedType == typeof(Leaf) ? "leaf" : "root!";
        }

        public interface IShaped { int Corners(); }

        [Derived(typeof(Leaf)), Derived(typeof(Root))]
        public class Root : IShaped { public virtual int Corners() => 3; }

        public sealed class Leaf : Root { public override int Corners() => 4; }

        public enum Shade { Light, Dark, Darker }

        public sealed class Holder { public Type Type = typeof(object); }

        public static class EntryPoint
        {
            private static int Count(Type type)
            {
                var sum = 0;
                if (type.IsEnum) foreach (var name in (IEnumerable<string>)Enum.GetNames(type)) sum += name.Length;
                return sum;
            }

            public static int Run(int input)
            {
                var holders = new[] { new Holder { Type = typeof(Root) }, new Holder(), new Holder { Type = typeof(Shade) } };
                var total = 0;
                foreach (var holder in holders) total = total * 100 + Count(holder.Type) % 100;
                return total + input;
            }
        }
        """;
    private const string CreateinstanceAlone = """
        using System;
        using System.Collections;
        using System.Collections.Generic;
        using System.Reflection;

        namespace SweepCreateinstance;

        [AttributeUsage(AttributeTargets.Class | AttributeTargets.Interface, AllowMultiple = true, Inherited = false)]
        public class DerivedAttribute : Attribute
        {
            public DerivedAttribute(Type type) { DerivedType = type; }
            public Type DerivedType { get; }
            public override string ToString() => DerivedType == typeof(Leaf) ? "leaf" : "root!";
        }

        public interface IShaped { int Corners(); }

        [Derived(typeof(Leaf)), Derived(typeof(Root))]
        public class Root : IShaped { public virtual int Corners() => 3; }

        public sealed class Leaf : Root { public override int Corners() => 4; }

        public enum Shade { Light, Dark, Darker }

        public sealed class Holder { public Type Type = typeof(object); }

        public static class EntryPoint
        {
            private static int Count(Type type)
            {
                var sum = 0;
                var made = Array.CreateInstance(type == typeof(object) ? typeof(Root) : type, 2); made.SetValue(type == typeof(Leaf) ? new Leaf() : type == typeof(Root) ? new Root() : null, 0); foreach (var element in (IEnumerable)made) sum += element is IShaped shaped ? shaped.Corners() : 1;
                return sum;
            }

            public static int Run(int input)
            {
                var holders = new[] { new Holder { Type = typeof(Root) }, new Holder(), new Holder { Type = typeof(Leaf) } };
                var total = 0;
                foreach (var holder in holders) total = total * 100 + Count(holder.Type) % 100;
                return total + input;
            }
        }
        """;

    private static readonly System.Collections.Generic.Dictionary<string, (string Source, string Entry, int Expected)> Alone = new()
    {
        ["generic"] = (GenericAlone, "SweepGeneric.EntryPoint", 90001),
        ["typed"] = (TypedAlone, "SweepTyped.EntryPoint", 90001),
        ["instance"] = (InstanceAlone, "SweepInstance.EntryPoint", 90001),
        ["statics"] = (StaticsAlone, "SweepStatics.EntryPoint", 90001),
        ["interfaces"] = (InterfacesAlone, "SweepInterfaces.EntryPoint", 10002),
        ["enumvalues"] = (EnumvaluesAlone, "SweepEnumvalues.EntryPoint", 16),
        ["enumvaluesgeneric"] = (EnumvaluesgenericAlone, "SweepEnumvaluesgeneric.EntryPoint", 16),
        ["enumnames"] = (EnumnamesAlone, "SweepEnumnames.EntryPoint", 16),
        ["createinstance"] = (CreateinstanceAlone, "SweepCreateinstance.EntryPoint", 40206),
    };

    public static TheoryData<string, WasmTarget, FinalWasmOptimization> AloneCases()
    {
        var data = new TheoryData<string, WasmTarget, FinalWasmOptimization>();
        foreach (var name in Alone.Keys)
        {
            foreach (var target in new[] { WasmTarget.Wasm32, WasmTarget.Wasm64 })
            {
                foreach (var optimization in new[]
                    { FinalWasmOptimization.O1, FinalWasmOptimization.O3, FinalWasmOptimization.Os, FinalWasmOptimization.Oz })
                {
                    data.Add(name, target, optimization);
                }
            }
        }
        return data;
    }

    [Theory]
    [MemberData(nameof(AloneCases))]
    public void AForeachOverAnAnswerBuiltAfterTheClosureReachesItsEnumerator(string program, WasmTarget target,
        FinalWasmOptimization optimization)
    {
        var (source, entry, expected) = Alone[program];
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSource($"ReceiverFlowAnswerAlone{program}{target}{optimization}", source);
        var result = NetWasmCompiler.Compile(new CompilerOptions(
            assembly, [assets.CoreLib], entry, "Run", [], Target: target, Optimization: optimization));
        Assert.Equal(expected, ExecuteWithStandardWasiNode(result.ApplicationModule, assets.Directory, 1, target,
            result.StaticDataEnd));
    }

    private static readonly System.Collections.Generic.Dictionary<(WasmTarget, FinalWasmOptimization, bool), (byte[], int, string)> s_compiled = [];
    private static readonly System.Collections.Generic.List<TestAssets> s_assets = [];
    private static readonly object Gate = new();

    // One compile per target and level serves its forms.
    private static (byte[] Module, int StaticDataEnd, string Directory) Compiled(WasmTarget target, FinalWasmOptimization optimization,
        bool audit = false)
    {
        lock (Gate)
        {
            if (s_compiled.TryGetValue((target, optimization, audit), out var cached)) return cached;
            var assets = TestAssets.Create();
            s_assets.Add(assets);
            var assembly = assets.CompileSource($"ReceiverFlowAttributeResults{target}{optimization}{audit}", Source);
            var result = NetWasmCompiler.Compile(new CompilerOptions(
                assembly, [assets.CoreLib], "AttributeResults.EntryPoint", "Run", [], Target: target, Optimization: optimization)
            {
                ReceiverFlowAudit = audit,
            });
            cached = (result.ApplicationModule, result.StaticDataEnd, assets.Directory);
            s_compiled.Add((target, optimization, audit), cached);
            return cached;
        }
    }
}
