using System.Linq;
using NetWasm.Compiler.ComponentModel;

namespace NetWasm.Compiler.Tests.Correctness;

// Which method a virtual, interface or constrained call runs, as ECMA-335
// II.12.2 defines it: a new slot hides the one above it, an interface maps to
// the most derived type that declares it and then to that slot's override, a
// covariant override replaces every slot it preserves, and a default interface
// method is the most specific one the receiver's interfaces provide. Each case
// returns a hash of the names of the methods it ran.
public sealed class MethodResolutionCorpusTests(CorrectnessTestRunner runner)
{
    [Theory]
    [InlineData(FinalWasmOptimization.Oz)]
    [InlineData(FinalWasmOptimization.O3)]
    public void CallsRunTheMethodDesktopRuns(FinalWasmOptimization optimization) =>
        runner.Run(new(
            "MethodResolutionCorpus",
            "NetWasm.Correctness.MethodResolution",
            Source,
            [.. Enumerable.Range(0, 33)])
        {
            Optimization = optimization,
            ExecuteWasm64 = true,
            ExecuteOptimizedWasm = true,
        });

    internal const string Source = """
            using System;
            using System.Collections;
            using System.Collections.Generic;

            namespace NetWasm.Correctness.MethodResolution;

            public interface I { string M(); }
            public class B : I { public string M() => "B"; }
            public class HidesB : B { public new string M() => "HidesB"; }
            public sealed class SealedHidesB : B { public new string M() => "SealedHidesB"; }
            public class ReimplementsI : B, I { public new string M() => "ReimplementsI"; }

            public class VirtualI : I { public virtual string M() => "VirtualI"; }
            public class OverridesVirtualI : VirtualI { public override string M() => "OverridesVirtualI"; }
            public class HidesOverride : OverridesVirtualI { public new virtual string M() => "HidesOverride"; }

            public class ExplicitI : I { string I.M() => "ExplicitI"; public string M() => "ExplicitI.public"; }
            public class HidesExplicit : ExplicitI { public new string M() => "HidesExplicit"; }
            public class RedeclaresOverExplicit : ExplicitI, I { public new string M() => "RedeclaresOverExplicit"; }

            public class SlotFromBase { public virtual string M() => "SlotFromBase"; }
            public class DeclaresIOnBaseSlot : SlotFromBase, I { }
            public class OverridesBaseSlot : DeclaresIOnBaseSlot { public override string M() => "OverridesBaseSlot"; }

            public interface IProducer<out T> { T Get(); }
            public class StringProducer : IProducer<string> { public string Get() => "StringProducer"; }
            public class HidesProducer : StringProducer { public new string Get() => "HidesProducer"; }

            public struct ValueI : I { public string M() => "ValueI"; }

            public class Counting : List<int>
            {
                public static int Calls;
                public new void Add(int item) { Calls++; base.Add(item); }
            }

            public class A1 { public virtual string M() => "A1"; }
            public class B1 : A1 { public new virtual string M() => "B1"; }
            public class C1 : B1 { public override string M() => "C1"; }

            public class A2 { public virtual string M() => "A2"; }
            public class B2 : A2 { public override string M() => "B2"; }
            public class C2 : B2 { public new virtual string M() => "C2"; }
            public class D2 : C2 { public override string M() => "D2"; }

            public class A3 { public virtual string M() => "A3"; }
            public class B3 : A3 { public sealed override string M() => "B3"; }
            public class C3 : B3 { public new virtual string M() => "C3"; }
            public class D3 : C3 { public override string M() => "D3"; }

            public class P { public override string ToString() => "P"; }
            public class Q : P { public new virtual string ToString() => "Q"; }
            public class R : Q { public override string ToString() => "R"; }

            public class CovA { public virtual CovA Get() => new CovA(); public virtual string Tag => "CovA"; }
            public class CovB : CovA { public override CovB Get() => new CovB(); public override string Tag => "CovB"; }
            public class CovC : CovB { public override CovC Get() => new CovC(); public override string Tag => "CovC"; }
            public class CovD : CovC { public override CovD Get() => new CovD(); public override string Tag => "CovD"; }

            public abstract class Shape { public abstract Shape Copy(); public abstract string Name { get; } }
            public class Square : Shape { public override Square Copy() => new Square(); public override string Name => "Square"; }
            public sealed class Unit : Square { public override Unit Copy() => new Unit(); public override string Name => "Unit"; }

            public record R1(int X);
            public record R2(int X, int Y) : R1(X);
            public record R3(int X, int Y, int Z) : R2(X, Y);

            public interface I1 { string M() => "I1"; }
            public interface I2 : I1 { string I1.M() => "I2"; }
            public interface I3 : I2 { string I1.M() => "I3"; }
            public class UsesI2 : I2 { }
            public class UsesI3 : I3 { }
            public class ClassBeatsDefault : I2 { public string M() => "ClassBeatsDefault"; }
            public class DefaultInBase : I1 { }
            public class ReimplementsThroughI2 : DefaultInBase, I2 { }

            public interface IG<T> { int Value() => 1; }
            public interface IH<T> : IG<T> { int IG<T>.Value() => 2; }
            public class UsesIH : IH<int> { }

            public class GBase<T>
            {
                public virtual string M(T value) => "GBase.T";
                public virtual string M(int value) => "GBase.int";
            }
            public class GDerived<T> : GBase<T>
            {
                public override string M(T value) => "GDerived.T";
                public override string M(int value) => "GDerived.int";
            }

            public interface IStatic { static virtual string S() => "IStatic"; }
            public struct OwnStatic : IStatic { public static string S() => "OwnStatic"; }
            public struct DefaultStatic : IStatic { }

            public static class EntryPoint
            {
                public static int Run(int input) => input switch
                {
                    0 => Hash(((I)new HidesB()).M()),
                    1 => Hash(Constrained(new SealedHidesB())),
                    2 => Hash(Constrained(new HidesB())),
                    3 => Hash(((I)new ReimplementsI()).M()),
                    4 => Hash(((I)new OverridesVirtualI()).M()),
                    5 => Hash(((I)new HidesOverride()).M()),
                    6 => Hash(((I)new HidesExplicit()).M()),
                    7 => Hash(((I)new RedeclaresOverExplicit()).M()),
                    8 => Hash(((I)new OverridesBaseSlot()).M()),
                    9 => Hash(((IProducer<object>)new HidesProducer()).Get().ToString()!),
                    10 => Hash(Constrained(new ValueI())),
                    11 => CountingAdd(),
                    12 => Hash(((A1)new C1()).M() + ((B1)new C1()).M()),
                    13 => Hash(((A2)new D2()).M() + ((B2)new D2()).M() + ((C2)new D2()).M()),
                    14 => Hash(((A3)new D3()).M() + ((B3)new D3()).M() + ((C3)new D3()).M()),
                    15 => Hash(((object)new R()).ToString()! + ((P)new R()).ToString() + ((Q)new R()).ToString()),
                    16 => Hash(((CovA)new CovC()).Get().Tag + ((CovB)new CovC()).Get().Tag),
                    17 => Hash(((CovA)new CovD()).Get().Tag + ((CovB)new CovD()).Get().Tag + ((CovC)new CovD()).Get().Tag),
                    18 => Hash(((Shape)new Unit()).Copy().Name),
                    19 => Records(),
                    20 => Hash(((I1)new UsesI2()).M()),
                    21 => Hash(((I1)new UsesI3()).M() + ((I2)new UsesI3()).M()),
                    22 => Hash(((I1)new ClassBeatsDefault()).M()),
                    23 => Hash(((I1)new DefaultInBase()).M() + ((I1)new ReimplementsThroughI2()).M()),
                    24 => ((IG<int>)new UsesIH()).Value(),
                    25 => Hash(Static<OwnStatic>() + Static<DefaultStatic>()),
                    26 => Hash(ConstrainedProducer(new HidesProducer())),
                    27 => Hash(Constrained(new OverridesBaseSlot()) + Constrained(new HidesOverride())),
                    28 => Hash(DefaultThrough<UsesI3>(new UsesI3())),
                    29 => Hash(((CovA)new CovB()).Get().Tag + new CovB().Get().Tag),
                    30 => Hash(((GBase<int>)new GDerived<int>()).M(1)),
                    31 => Hash(Generic<int>(new GDerived<int>(), 1) + Generic<string>(new GDerived<string>(), "x")),
                    32 => NonGenericListSet(),
                    _ => -1,
                };

                public static int Trace() => 0;

                private static string Constrained<T>(T value) where T : I => value.M();

                private static string ConstrainedProducer<T>(T value) where T : IProducer<string> => value.Get();

                private static string DefaultThrough<T>(T value) where T : I1 => value.M();

                private static string Static<T>() where T : IStatic => T.S();

                private static string Generic<T>(GBase<T> receiver, T value) => receiver.M(value);

                // List<T>'s explicit IList indexer setter catches an
                // InvalidCastException; at O3 it once failed to compile.
                private static int NonGenericListSet()
                {
                    IList list = new List<int> { 1, 2, 3 };
                    list[0] = 5;
                    return (int)list[0]! * 10 + list.Count;
                }

                private static int CountingAdd()
                {
                    ICollection<int> collection = new Counting();
                    collection.Add(1);
                    return Counting.Calls * 10 + collection.Count;
                }

                private static int Records()
                {
                    R1 record = new R3(1, 2, 3);
                    var copy = record with { X = 5 };
                    return Hash(copy.ToString() + (copy is R3 ? "R3" : "base") + (copy == new R3(5, 2, 3) ? "equal" : "different"));
                }

                private static int Hash(string text)
                {
                    var hash = 17;
                    foreach (var character in text)
                    {
                        hash = unchecked(hash * 31 + character);
                    }
                    return hash;
                }
            }
            """;
}
