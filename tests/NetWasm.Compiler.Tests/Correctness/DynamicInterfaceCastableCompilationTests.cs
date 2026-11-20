// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests.Correctness;

// An object whose class implements IDynamicInterfaceCastable is cast to, stored as and called through interfaces its
// class does not implement, as desktop .NET runs it: casts ask IsInterfaceImplemented (castclass throwing what desktop
// throws when it declines, or what the implementation throws), interface calls go to the implementation interface
// GetInterfaceImplementation names (its default methods, generic ones, one calling another interface method on
// this), each invalid answer fails as desktop does, a value type implementing the interface is ignored, array stores
// ask too, and a delegate over an interface method binds as the call does. Ordinary implementers of the same
// interfaces keep their own dispatch.
public sealed class DynamicInterfaceCastableCompilationTests(CorrectnessTestRunner runner)
{
    [Fact]
    public void DynamicCastsAndDispatchMatchDesktop() =>
        runner.Run(new("DynamicInterfaceCastable", "DynamicInterfaceCastable", Source, [0, 1, 2])
        {
            ExecuteOptimizedWasm = true,
            ReportAllMismatches = true,
        });

    [Fact]
    public void AProgramWithoutACastableClassIsNotRewritten()
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSource("NoCastable", """
            using System;
            public interface IShape { int Area(); }
            public sealed class Square : IShape { public int Size; public int Area() => Size * Size; }
            public static class EntryPoint
            {
                public static int Run(int input)
                {
                    object shape = new Square { Size = input };
                    var shapes = new IShape[] { (IShape)shape };
                    return (shape is IShape typed ? typed.Area() : 0) + shapes[0].Area() + (shape as IComparable is null ? 1 : 0);
                }
            }
            """);
        var result = NetWasmCompiler.Compile(new CompilerOptions(assembly, [assets.CoreLib], "EntryPoint", "Run", []));
        Assert.DoesNotContain(result.Program.MethodInstances.Values, method =>
            method.DeclaringType.FullName == "System.Runtime.InteropServices.DynamicInterfaceCastableHelpers");
        Assert.Equal(19, CompilerTestSupport.ExecuteWithStandardWasiNode(result.ApplicationModule, assets.Directory, 3,
            WasmTarget.Wasm32, result.StaticDataEnd));
    }

    // Every interface call the program reaches goes through the splice, the core library's included, so a body whose
    // evaluation stack was at its declared maximum (the console's stream writer) still validates and runs.
    [Fact]
    public void AProgramThatWritesToTheConsoleCompilesAndRuns()
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSource("CastableConsole", """
            using System;
            using System.Runtime.InteropServices;
            namespace CastableConsole;
            public interface IGreeter { int Greet(int value); }
            [DynamicInterfaceCastableImplementation]
            public interface IGreeterImpl : IGreeter { int IGreeter.Greet(int value) => value + 1; }
            public sealed class Proxy : IDynamicInterfaceCastable
            {
                public bool IsInterfaceImplemented(RuntimeTypeHandle interfaceType, bool throwIfNotImplemented) =>
                    interfaceType.Equals(typeof(IGreeter).TypeHandle);
                public RuntimeTypeHandle GetInterfaceImplementation(RuntimeTypeHandle interfaceType) => typeof(IGreeterImpl).TypeHandle;
            }
            public static class EntryPoint
            {
                public static int Run(int input)
                {
                    Console.WriteLine(((IGreeter)(object)new Proxy()).Greet(input));
                    return 0;
                }
            }
            """);
        var result = NetWasmCompiler.Compile(new CompilerOptions(assembly, [assets.CoreLib], "CastableConsole.EntryPoint", "Run",
            [new("run", "CastableConsole.EntryPoint", "Run")],
            WitPath: Path.Combine(assets.Root, "wit", "netwasm-platform-1.0.0"), WitWorld: "netwasm:platform/platform@1.0.0"));
        Assert.Equal("42\n", CompilerTestSupport.ExecuteWithStandardWasiNodeCapturingOutput(result.ApplicationModule, assets.Directory,
            41, WasmTarget.Wasm32, result.StaticDataEnd));
    }

    private const string Source = """
        using System;
        using System.Runtime.InteropServices;
        namespace DynamicInterfaceCastable;

        public interface IGreeter
        {
            int Greet(int value);
            int Twice(int value);
            T Pick<T>(T first, T second);
        }
        public interface IAdder<T> { T Add(T left, T right); }
        public interface IUnimplemented { int Missing(); }
        public interface IOther { int Other(); }
        public interface IMarker { }

        [DynamicInterfaceCastableImplementation]
        public interface IGreeterImpl : IGreeter
        {
            int IGreeter.Greet(int value) => ((Proxy)(object)this).Seed + value;
            // Another interface call on this goes through the object again.
            int IGreeter.Twice(int value) => ((IGreeter)this).Greet(value) * 2;
            T IGreeter.Pick<T>(T first, T second) => ((Proxy)(object)this).Seed % 2 == 0 ? first : second;
        }
        [DynamicInterfaceCastableImplementation]
        public interface IIntAdderImpl : IAdder<int> { int IAdder<int>.Add(int left, int right) => left + right + ((Proxy)(object)this).Seed; }
        [DynamicInterfaceCastableImplementation]
        public interface IAdderImpl<T> : IAdder<T> { T IAdder<T>.Add(T left, T right) => left; }
        [DynamicInterfaceCastableImplementation]
        public interface IUnimplementedImpl : IUnimplemented { }
        public interface INotMarked : IOther { int IOther.Other() => 5; }

        public struct ValueCastable : IDynamicInterfaceCastable
        {
            public bool IsInterfaceImplemented(RuntimeTypeHandle interfaceType, bool throwIfNotImplemented) => true;
            public RuntimeTypeHandle GetInterfaceImplementation(RuntimeTypeHandle interfaceType) => typeof(IGreeterImpl).TypeHandle;
        }

        public sealed class Proxy : IDynamicInterfaceCastable
        {
            public int Seed;
            public int Mode;

            public bool IsInterfaceImplemented(RuntimeTypeHandle interfaceType, bool throwIfNotImplemented)
            {
                if (interfaceType.Equals(typeof(IGreeter).TypeHandle) || interfaceType.Equals(typeof(IAdder<int>).TypeHandle) ||
                    interfaceType.Equals(typeof(IAdder<long>).TypeHandle) || interfaceType.Equals(typeof(IUnimplemented).TypeHandle) ||
                    interfaceType.Equals(typeof(IOther).TypeHandle) || interfaceType.Equals(typeof(IMarker).TypeHandle))
                {
                    return true;
                }
                if (throwIfNotImplemented && Mode == 1) throw new NotSupportedException("declined by the proxy");
                return false;
            }

            public RuntimeTypeHandle GetInterfaceImplementation(RuntimeTypeHandle interfaceType)
            {
                if (interfaceType.Equals(typeof(IGreeter).TypeHandle)) return typeof(IGreeterImpl).TypeHandle;
                if (interfaceType.Equals(typeof(IAdder<int>).TypeHandle)) return typeof(IIntAdderImpl).TypeHandle;
                if (interfaceType.Equals(typeof(IAdder<long>).TypeHandle)) return typeof(IAdderImpl<long>).TypeHandle;
                if (interfaceType.Equals(typeof(IUnimplemented).TypeHandle)) return typeof(IUnimplementedImpl).TypeHandle;
                return Mode switch
                {
                    0 => default,
                    1 => typeof(Proxy).TypeHandle,
                    2 => typeof(INotMarked).TypeHandle,
                    _ => typeof(IGreeterImpl).TypeHandle,
                };
            }
        }

        public sealed class RealGreeter : IGreeter
        {
            public int Greet(int value) => value * 3;
            public int Twice(int value) => value * 4;
            public T Pick<T>(T first, T second) => second;
        }

        public static class EntryPoint
        {
            private static int _trace;
            private static int _hash = 17;

            public static int Trace() => _trace;

            private static void Mix(int value) => _hash = unchecked(_hash * 31 + value);

            private static void Mix(string text)
            {
                foreach (var character in text) Mix(character);
            }

            // An ordinary cast failure's message is not this fixture's subject, so it records only its type.
            private static void Record(Func<int> action, bool message = true)
            {
                try
                {
                    Mix(action());
                }
                catch (Exception exception)
                {
                    Mix(exception switch
                    {
                        InvalidCastException => 1,
                        InvalidOperationException => 2,
                        EntryPointNotFoundException => 3,
                        NotSupportedException => 4,
                        ArrayTypeMismatchException => 5,
                        _ => 9,
                    });
                    if (message && exception is not ArrayTypeMismatchException) Mix(Normalized(exception.Message));
                }
            }

            // A message names types as Type.ToString() does, which reads the type's id where a build carries no names;
            // each involved type's own ToString() stands in for its name, so the rest of the message is compared.
            private static string Normalized(string message)
            {
                Type[] types =
                [
                    typeof(Proxy), typeof(IGreeter), typeof(IGreeterImpl), typeof(IOther), typeof(INotMarked),
                    typeof(IComparable), typeof(IUnimplemented), typeof(IUnimplementedImpl),
                ];
                // Longer names first, as one name can begin another; each keeps its own placeholder.
                var order = new int[types.Length];
                for (var index = 0; index < order.Length; index++) order[index] = index;
                Array.Sort(order, (left, right) => types[right].ToString().Length.CompareTo(types[left].ToString().Length));
                foreach (var index in order) message = message.Replace(types[index].ToString(), "<" + index + ">");
                return message;
            }

            public static int Run(int input)
            {
                object proxy = new Proxy { Seed = input + 10 };
                Mix(proxy is IGreeter ? 1 : 0);
                Mix(proxy is IMarker ? 1 : 0);
                Mix(proxy is IComparable ? 1 : 0);
                Mix(proxy as IComparable is null ? 1 : 0);
                Record(() => ((IComparable)proxy).CompareTo(null));
                Record(() => ((IComparable)(object)new Proxy { Mode = 1 }).CompareTo(null));

                var greeter = (IGreeter)proxy;
                Record(() => greeter.Greet(3));
                Record(() => greeter.Twice(4));
                Record(() => greeter.Pick(input, 7));
                Record(() => greeter.Pick("first", "second").Length);
                Record(() => ((IAdder<int>)proxy).Add(input, 2));
                Record(() => (int)((IAdder<long>)proxy).Add(input + 5, 9));
                Record(() => ((IUnimplemented)proxy).Missing());
                for (var mode = 0; mode < 4; mode++)
                {
                    var current = mode;
                    Record(() => ((IOther)(object)new Proxy { Mode = current }).Other());
                }

                object boxed = new ValueCastable();
                Mix(boxed is IGreeter ? 1 : 0);
                Record(() => ((IGreeter)boxed).Greet(1), message: false);

                // An array store asks too: one the object admits, then one it declines.
                IGreeter[] greeters = new IGreeter[1];
                object[] slots = greeters;
                Record(() =>
                {
                    slots[0] = proxy;
                    return greeters[0].Greet(1);
                });
                IComparable[] comparables = new IComparable[1];
                object[] others = comparables;
                Record(() =>
                {
                    others[0] = proxy;
                    return 0;
                });

                // A delegate over an interface method binds to the implementation interface's method, as ldvirtftn
                // resolves it; an invalid answer or a missing method fails where desktop's does.
                Func<int, int> greet = greeter.Greet;
                Record(() => greet(3));
                Func<int, int> twice = ((IGreeter)proxy).Twice;
                Record(() => twice(5));
                Func<int, int, int> add = ((IAdder<int>)proxy).Add;
                Record(() => add(input, 4));
                Record(() =>
                {
                    Func<int> missing = ((IUnimplemented)proxy).Missing;
                    return missing();
                });
                for (var mode = 0; mode < 4; mode++)
                {
                    var current = mode;
                    Record(() =>
                    {
                        Func<int> other = ((IOther)(object)new Proxy { Mode = current }).Other;
                        return other();
                    });
                }

                IGreeter real = new RealGreeter();
                Func<int, int> realGreet = real.Greet;
                Record(() => realGreet(input));
                Record(() => real.Greet(input) + real.Twice(input) + real.Pick(1, 2));
                object text = input >= 0 ? "text" : "other";
                Record(() => ((IGreeter)text).Greet(1), message: false);
                Mix(text is IComparable ? 1 : 0);

                _trace = _hash;
                return _hash;
            }
        }
        """;
}
