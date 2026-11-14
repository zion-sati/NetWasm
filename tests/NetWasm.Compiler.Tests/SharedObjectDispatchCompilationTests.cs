using NetWasm.Compiler.ComponentModel;
using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

// A virtual call on object with many targets calls CoreLib's shared body for it (SharedDispatchPolicy): a program
// that hashes, compares and formats objects of 40 types plans the shared bodies and gets the overrides' results;
// a program with few types plans none.
public sealed class SharedObjectDispatchCompilationTests
{
    private const string ManyTypes = """
        public sealed class C0 { public override int GetHashCode() => 0; public override bool Equals(object? o) => o is C0; public override string ToString() => "c0"; }
        public struct S1 { public int V; public override int GetHashCode() => 1; public override bool Equals(object? o) => o is S1; public override string ToString() => "s1"; }
        public sealed class C2 { public override int GetHashCode() => 2; public override bool Equals(object? o) => o is C2; public override string ToString() => "c2"; }
        public struct S3 { public int V; public override int GetHashCode() => 3; public override bool Equals(object? o) => o is S3; public override string ToString() => "s3"; }
        public sealed class C4 { public override int GetHashCode() => 4; public override bool Equals(object? o) => o is C4; public override string ToString() => "c4"; }
        public struct S5 { public int V; public override int GetHashCode() => 5; public override bool Equals(object? o) => o is S5; public override string ToString() => "s5"; }
        public sealed class C6 { public override int GetHashCode() => 6; public override bool Equals(object? o) => o is C6; public override string ToString() => "c6"; }
        public struct S7 { public int V; public override int GetHashCode() => 7; public override bool Equals(object? o) => o is S7; public override string ToString() => "s7"; }
        public sealed class C8 { public override int GetHashCode() => 8; public override bool Equals(object? o) => o is C8; public override string ToString() => "c8"; }
        public struct S9 { public int V; public override int GetHashCode() => 9; public override bool Equals(object? o) => o is S9; public override string ToString() => "s9"; }
        public sealed class C10 { public override int GetHashCode() => 10; public override bool Equals(object? o) => o is C10; public override string ToString() => "c10"; }
        public struct S11 { public int V; public override int GetHashCode() => 11; public override bool Equals(object? o) => o is S11; public override string ToString() => "s11"; }
        public sealed class C12 { public override int GetHashCode() => 12; public override bool Equals(object? o) => o is C12; public override string ToString() => "c12"; }
        public struct S13 { public int V; public override int GetHashCode() => 13; public override bool Equals(object? o) => o is S13; public override string ToString() => "s13"; }
        public sealed class C14 { public override int GetHashCode() => 14; public override bool Equals(object? o) => o is C14; public override string ToString() => "c14"; }
        public struct S15 { public int V; public override int GetHashCode() => 15; public override bool Equals(object? o) => o is S15; public override string ToString() => "s15"; }
        public sealed class C16 { public override int GetHashCode() => 16; public override bool Equals(object? o) => o is C16; public override string ToString() => "c16"; }
        public struct S17 { public int V; public override int GetHashCode() => 17; public override bool Equals(object? o) => o is S17; public override string ToString() => "s17"; }
        public sealed class C18 { public override int GetHashCode() => 18; public override bool Equals(object? o) => o is C18; public override string ToString() => "c18"; }
        public struct S19 { public int V; public override int GetHashCode() => 19; public override bool Equals(object? o) => o is S19; public override string ToString() => "s19"; }
        public sealed class C20 { public override int GetHashCode() => 20; public override bool Equals(object? o) => o is C20; public override string ToString() => "c20"; }
        public struct S21 { public int V; public override int GetHashCode() => 21; public override bool Equals(object? o) => o is S21; public override string ToString() => "s21"; }
        public sealed class C22 { public override int GetHashCode() => 22; public override bool Equals(object? o) => o is C22; public override string ToString() => "c22"; }
        public struct S23 { public int V; public override int GetHashCode() => 23; public override bool Equals(object? o) => o is S23; public override string ToString() => "s23"; }
        public sealed class C24 { public override int GetHashCode() => 24; public override bool Equals(object? o) => o is C24; public override string ToString() => "c24"; }
        public struct S25 { public int V; public override int GetHashCode() => 25; public override bool Equals(object? o) => o is S25; public override string ToString() => "s25"; }
        public sealed class C26 { public override int GetHashCode() => 26; public override bool Equals(object? o) => o is C26; public override string ToString() => "c26"; }
        public struct S27 { public int V; public override int GetHashCode() => 27; public override bool Equals(object? o) => o is S27; public override string ToString() => "s27"; }
        public sealed class C28 { public override int GetHashCode() => 28; public override bool Equals(object? o) => o is C28; public override string ToString() => "c28"; }
        public struct S29 { public int V; public override int GetHashCode() => 29; public override bool Equals(object? o) => o is S29; public override string ToString() => "s29"; }
        public sealed class C30 { public override int GetHashCode() => 30; public override bool Equals(object? o) => o is C30; public override string ToString() => "c30"; }
        public struct S31 { public int V; public override int GetHashCode() => 31; public override bool Equals(object? o) => o is S31; public override string ToString() => "s31"; }
        public sealed class C32 { public override int GetHashCode() => 32; public override bool Equals(object? o) => o is C32; public override string ToString() => "c32"; }
        public struct S33 { public int V; public override int GetHashCode() => 33; public override bool Equals(object? o) => o is S33; public override string ToString() => "s33"; }
        public sealed class C34 { public override int GetHashCode() => 34; public override bool Equals(object? o) => o is C34; public override string ToString() => "c34"; }
        public struct S35 { public int V; public override int GetHashCode() => 35; public override bool Equals(object? o) => o is S35; public override string ToString() => "s35"; }
        public sealed class C36 { public override int GetHashCode() => 36; public override bool Equals(object? o) => o is C36; public override string ToString() => "c36"; }
        public struct S37 { public int V; public override int GetHashCode() => 37; public override bool Equals(object? o) => o is S37; public override string ToString() => "s37"; }
        public sealed class C38 { public override int GetHashCode() => 38; public override bool Equals(object? o) => o is C38; public override string ToString() => "c38"; }
        public struct S39 { public int V; public override int GetHashCode() => 39; public override bool Equals(object? o) => o is S39; public override string ToString() => "s39"; }

        public static class EntryPoint
        {
            public static int Run(int input)
            {
                object[] values = [new C0(), new S1(), new C2(), new S3(), new C4(), new S5(), new C6(), new S7(), new C8(), new S9(), new C10(), new S11(), new C12(), new S13(), new C14(), new S15(), new C16(), new S17(), new C18(), new S19(), new C20(), new S21(), new C22(), new S23(), new C24(), new S25(), new C26(), new S27(), new C28(), new S29(), new C30(), new S31(), new C32(), new S33(), new C34(), new S35(), new C36(), new S37(), new C38(), new S39()];
                var hash = 0;
                var equal = 0;
                var text = 0;
                foreach (var value in values)
                {
                    hash += value.GetHashCode();
                    equal += value.Equals(value) ? 1 : 0;
                    equal += value.Equals("other") ? 100 : 0;
                    text += value.ToString()!.Length;
                }
                // 0 + 1 + ... + 39 = 780; 40 equal to themselves; "cN"/"sN": 10 of length 2 and 30 of length 3.
                return hash == 780 && equal == 40 && text == 110 ? input + 1 : 0;
            }
        }
        """;

    private const string FewTypes = """
        public static class EntryPoint
        {
            public static int Run(int input)
            {
                object value = "text";
                return value.GetHashCode() == "text".GetHashCode() && value.Equals("text") ? input + 1 : 0;
            }
        }
        """;

    // The size presets and O0 share; the speed presets dispatch inline (ObjectDispatchPolicy).
    [Theory]
    [InlineData(FinalWasmOptimization.O0, true)]
    [InlineData(FinalWasmOptimization.O3, false)]
    [InlineData(FinalWasmOptimization.Oz, true)]
    public void ManyTargetCallsOnObjectUseTheSharedBodies(FinalWasmOptimization optimization, bool shared)
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileOptimizedSource("SharedObjectDispatchMany", ManyTypes);
        var result = NetWasmCompiler.Compile(new CompilerOptions(
            assembly, [assets.CoreLib], "EntryPoint", "Run", [], Target: WasmTarget.Wasm32, Optimization: optimization));

        Assert.Equal(
            shared ? ["DispatchEquals", "DispatchGetHashCode", "DispatchToString"] : [],
            result.Program.SharedObjectDispatch.Values.Select(helper => helper.Definition.Name).Order());
        Assert.Equal(42, CompilerTestSupport.ExecuteWithStandardWasiNode(
            result.ApplicationModule, assets.Directory, 41, WasmTarget.Wasm32, result.StaticDataEnd));
    }

    [Fact]
    public void FewTargetCallsDispatchThemselves()
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileOptimizedSource("SharedObjectDispatchFew", FewTypes);
        var result = NetWasmCompiler.Compile(new CompilerOptions(
            assembly, [assets.CoreLib], "EntryPoint", "Run", [], Target: WasmTarget.Wasm32));

        Assert.Empty(result.Program.SharedObjectDispatch);
        Assert.Equal(42, CompilerTestSupport.ExecuteWithStandardWasiNode(
            result.ApplicationModule, assets.Directory, 41, WasmTarget.Wasm32, result.StaticDataEnd));
    }
}
