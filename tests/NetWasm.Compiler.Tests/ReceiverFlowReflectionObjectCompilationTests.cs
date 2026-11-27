// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Emit;
using NetWasm.Compiler.ComponentModel;
using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;
using static NetWasm.Compiler.Tests.CompilerTestSupport;

namespace NetWasm.Compiler.Tests;

// A reflection object the runtime hands out is one of its own classes, whatever the type, member or token it describes:
// a call on it through object (formatting does this) takes the overrides of its kind, not every allocated type's
// (Big's and Small's here, which only the plain ToString call reaches). Each kind comes the way the runtime gives it:
// GetType and typeof lower to type materializations, an expression tree's methods, constructors and fields to member
// materializations, a property through its accessor's descriptor, a parameter from its method's array, an assembly
// from its type's cache slot, a module from a type. C# casts a method handle's MethodBase to MethodInfo or
// ConstructorInfo before using it; IL from other compilers need not, so one program, emitted as IL, uses it uncast.
// The programs compile with the receiver flow audit and run, but the audit checks only calls whose receivers are an
// exact set; these calls are bounded, so the fixture checks their targets.
public sealed class ReceiverFlowReflectionObjectCompilationTests
{
    private const string Template = """
        using System;
        using System.Linq.Expressions;
        using System.Reflection;

        public sealed class Big { public override string ToString() => "big"; }

        public sealed class Small { public override string ToString() => "small"; }

        public sealed class Holder
        {
            public int Field;

            public int Property => Field;

            public int Method(int value) => value + Field;
        }

        public static class EntryPoint
        {
            public static int Run(int input)
            {
                object value = input > 0 ? new Big() : new Small();
                var text = value.ToString();
                object reflected = REFLECTED;
                // NetWasm records no parameter names, so a ParameterInfo's ToString throws; the call is still made.
                string name;
                try
                {
                    name = reflected.ToString();
                }
                catch (PlatformNotSupportedException)
                {
                    name = "unsupported";
                }
                return text.Length * 10 + (name.Length > 0 ? 1 : 0);
            }
        }
        """;

    public static TheoryData<string, FinalWasmOptimization> Kinds()
    {
        var data = new TheoryData<string, FinalWasmOptimization>();
        foreach (var kind in Reflected.Keys)
        {
            data.Add(kind, FinalWasmOptimization.O1);
            data.Add(kind, FinalWasmOptimization.Oz);
        }
        return data;
    }

    // Each kind's expression and the runtime class of the object it gives.
    private static readonly Dictionary<string, (string Expression, string Receiver)> Reflected = new()
    {
        ["GetType"] = ("value.GetType()", "System.RuntimeType"),
        ["typeof"] = ("typeof(Holder)", "System.RuntimeType"),
        ["MethodInfo"] = (
            "((MethodCallExpression)((Expression<Func<Holder, int>>)(holder => holder.Method(1))).Body).Method",
            "System.Reflection.RuntimeMethodInfo"),
        ["ConstructorInfo"] = (
            "((NewExpression)((Expression<Func<Holder>>)(() => new Holder())).Body).Constructor!",
            "System.Reflection.RuntimeConstructorInfo"),
        ["FieldInfo"] = (
            "((MemberExpression)((Expression<Func<Holder, int>>)(holder => holder.Field)).Body).Member",
            "System.Reflection.RuntimeFieldInfo"),
        ["PropertyInfo"] = (
            "((MemberExpression)((Expression<Func<Holder, int>>)(holder => holder.Property)).Body).Member",
            "System.Reflection.RuntimePropertyInfo"),
        ["ParameterInfo"] = (
            "((MethodCallExpression)((Expression<Func<Holder, int>>)(holder => holder.Method(1))).Body).Method" +
            ".GetParameters()[0]",
            "System.Reflection.RuntimeParameterInfo"),
        ["Assembly"] = ("typeof(Holder).Assembly", "System.Reflection.RuntimeAssembly"),
        ["Module"] = ("typeof(Holder).Module", "System.Reflection.RuntimeModule"),
    };

    [Theory]
    [MemberData(nameof(Kinds))]
    public void ACallOnAReflectionObjectTakesOnlyItsKindsOverrides(string kind, FinalWasmOptimization optimization)
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSource("ReceiverFlowReflectionObjectFixture",
            Template.Replace("REFLECTED", Reflected[kind].Expression, StringComparison.Ordinal));
        var result = NetWasmCompiler.Compile(new CompilerOptions(
            assembly, [assets.CoreLib], "EntryPoint", "Run", [], Optimization: optimization)
        {
            ReceiverFlowAudit = true,
        });

        AssertOnlyItsKindsOverrides(result, assets.Directory, Reflected[kind].Receiver);
    }

    [Theory]
    [InlineData(FinalWasmOptimization.O1)]
    [InlineData(FinalWasmOptimization.Oz)]
    public void AnUncastMethodBaseTakesOnlyMethodBasesOverrides(FinalWasmOptimization optimization)
    {
        using var assets = TestAssets.Create();
        var assembly = Path.Combine(assets.Directory, "ReceiverFlowUncastMethodBase.dll");
        File.WriteAllBytes(assembly, UncastMethodBase());
        var result = NetWasmCompiler.Compile(new CompilerOptions(
            assembly, [assets.CoreLib], "EntryPoint", "Run", [], Optimization: optimization,
            ReferenceAssemblyAliases: ImmutableDictionary<string, string>.Empty.Add(
                "System.Private.CoreLib", "NetWasm.CoreLib"),
            ProjectDirectory: assets.Directory)
        {
            ReceiverFlowAudit = true,
        });

        AssertOnlyItsKindsOverrides(result, assets.Directory, "System.Reflection.RuntimeMethodInfo");
    }

    // The program's value.ToString() takes Big's and Small's overrides, its reflected.ToString(), the next ToString
    // call, neither but the receiver's own (a bound too tight would leave it out, and the audit checks only calls
    // without bounds); it returns 31 for an input of 1.
    internal static void AssertOnlyItsKindsOverrides(CompilationResult result, string directory, string receiver)
    {
        var toStrings = result.Program.DispatchCallSites.Values
            .Where(site => site.Caller.Contains("EntryPoint", StringComparison.Ordinal) &&
                site.Declaration.Definition.Name == "ToString")
            .OrderBy(site => site.IlOffset)
            .ToArray();
        Assert.Equal(2, toStrings.Length);
        string[] ownReceivers = ["Big", "Small"];
        Assert.Equal(ownReceivers, toStrings[0].Targets.Select(target => target.ReceiverType.FullName).Order());
        Assert.DoesNotContain(toStrings[1].Targets, target => ownReceivers.Contains(target.ReceiverType.FullName));
        Assert.Contains(toStrings[1].Targets, target => target.ReceiverType.FullName == receiver);
        Assert.Equal(31, ExecuteWithStandardWasiNode(result.ApplicationModule, directory, 1,
            WasmTarget.Wasm32, result.StaticDataEnd));
    }

    // Run(input): (input > 0 ? new Big() : new Small()).ToString().Length * 10 plus 1 when
    // ((object)MethodBase.GetMethodFromHandle(ldtoken Holder.Method)).ToString() is not empty.
    private static byte[] UncastMethodBase()
    {
        var assembly = new PersistedAssemblyBuilder(new AssemblyName("ReceiverFlowUncastMethodBase"), typeof(object).Assembly);
        var module = assembly.DefineDynamicModule("ReceiverFlowUncastMethodBase.dll");
        var big = Named(module, "Big", "big");
        var small = Named(module, "Small", "small");
        var holder = module.DefineType("Holder", TypeAttributes.Public | TypeAttributes.Abstract | TypeAttributes.Sealed);
        var method = holder.DefineMethod("Method", MethodAttributes.Public | MethodAttributes.Static, typeof(int), []);
        var body = method.GetILGenerator();
        body.Emit(OpCodes.Ldc_I4_1);
        body.Emit(OpCodes.Ret);
        var type = module.DefineType("EntryPoint", TypeAttributes.Public | TypeAttributes.Abstract | TypeAttributes.Sealed);
        var run = type.DefineMethod("Run", MethodAttributes.Public | MethodAttributes.Static, typeof(int), [typeof(int)]);
        var code = run.GetILGenerator();
        var toString = typeof(object).GetMethod(nameof(ToString))!;
        var length = typeof(string).GetProperty(nameof(string.Length))!.GetMethod!;
        var smallLabel = code.DefineLabel();
        var join = code.DefineLabel();
        code.Emit(OpCodes.Ldarg_0);
        code.Emit(OpCodes.Ldc_I4_0);
        code.Emit(OpCodes.Ble, smallLabel);
        code.Emit(OpCodes.Newobj, big.Constructor);
        code.Emit(OpCodes.Br, join);
        code.MarkLabel(smallLabel);
        code.Emit(OpCodes.Newobj, small.Constructor);
        code.MarkLabel(join);
        code.Emit(OpCodes.Callvirt, toString);
        code.Emit(OpCodes.Callvirt, length);
        code.Emit(OpCodes.Ldc_I4, 10);
        code.Emit(OpCodes.Mul);
        code.Emit(OpCodes.Ldtoken, method);
        code.Emit(OpCodes.Call, typeof(MethodBase).GetMethod(nameof(MethodBase.GetMethodFromHandle), [typeof(RuntimeMethodHandle)])!);
        code.Emit(OpCodes.Callvirt, toString);
        code.Emit(OpCodes.Callvirt, length);
        code.Emit(OpCodes.Ldc_I4_0);
        code.Emit(OpCodes.Cgt);
        code.Emit(OpCodes.Add);
        code.Emit(OpCodes.Ret);
        holder.CreateType();
        type.CreateType();
        using var stream = new MemoryStream();
        assembly.Save(stream);
        return stream.ToArray();
    }

    // A sealed class whose ToString override returns the text.
    private static (TypeBuilder Type, ConstructorBuilder Constructor) Named(ModuleBuilder module, string name, string text)
    {
        var type = module.DefineType(name, TypeAttributes.Public | TypeAttributes.Sealed, typeof(object));
        var constructor = type.DefineDefaultConstructor(MethodAttributes.Public);
        var toString = type.DefineMethod(nameof(ToString),
            MethodAttributes.Public | MethodAttributes.Virtual | MethodAttributes.HideBySig, typeof(string), []);
        var code = toString.GetILGenerator();
        code.Emit(OpCodes.Ldstr, text);
        code.Emit(OpCodes.Ret);
        type.CreateType();
        return (type, constructor);
    }
}
