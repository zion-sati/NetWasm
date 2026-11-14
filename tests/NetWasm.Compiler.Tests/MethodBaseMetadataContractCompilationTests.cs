using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

public sealed class MethodBaseMetadataContractCompilationTests
{
    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void MethodMetadataFlagsForwardingAndRuntimeDescriptorsRemainCompatible(WasmTarget target)
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSource("MethodMetadataFixture", Source);
        var result = NetWasmCompiler.Compile(new(assembly, [assets.CoreLib], "MethodMetadataFixture.EntryPoint",
            "Run", [new("run", "MethodMetadataFixture.EntryPoint", "Run")], Target: target,
            WitPath: Path.Combine(assets.Root, "wit", "netwasm-platform-1.0.0"),
            WitWorld: "netwasm:platform/platform@1.0.0"));
        CompilerTestSupport.ValidateWithNode(result.ApplicationModule, assets.Directory);
        Assert.Equal(42, CompilerTestSupport.ExecuteWithStandardWasiNode(result.ApplicationModule,
            assets.Directory, 41, target, result.StaticDataEnd));
    }

    private const string Source = """
        using System;
        using System.Reflection;
        using System.Linq.Expressions;
        using System.Globalization;
        namespace MethodMetadataFixture;
        public static class EntryPoint
        {
            public static int Run(int input)
            {
                var provider = new Provider();
                MethodBase method = provider;
                provider.Flags = MethodAttributes.Public | MethodAttributes.Static | MethodAttributes.Abstract | MethodAttributes.Final | MethodAttributes.HideBySig | MethodAttributes.SpecialName | MethodAttributes.Virtual;
                if (!method.IsPublic || !method.IsStatic || !method.IsAbstract || !method.IsFinal || !method.IsHideBySig || !method.IsSpecialName || !method.IsVirtual || method.IsConstructor) return 1;
                provider.Flags = MethodAttributes.Private;
                if (!method.IsPrivate || method.IsStatic || method.IsAbstract || method.IsFinal || method.IsHideBySig || method.IsSpecialName || method.IsVirtual) return 2;
                if (method.MethodImplementationFlags != MethodImplAttributes.NoInlining || method.CallingConvention != CallingConventions.Standard) return 3;
                object[] args = new object[] { input };
                object target = new object();
                if ((int)method.Invoke(target, args) != input || !ReferenceEquals(provider.Target, target) || !ReferenceEquals(provider.Args, args) || provider.Binder != null || provider.Culture != null || provider.CallFlags != BindingFlags.Default) return 4;
                var ctor = new Ctor();
                if (!ctor.IsConstructor) return 5;
                ctor.Static = true;
                if (ctor.IsConstructor) return 6;
                var missing = new Missing();
                var caught = 0;
                try { _ = missing.Attributes; } catch (PlatformNotSupportedException) { caught++; }
                try { missing.Invoke(null, null); } catch (PlatformNotSupportedException) { caught++; }
                try { missing.GetGenericArguments(); } catch (PlatformNotSupportedException) { caught++; }
                try { _ = missing.MethodHandle; } catch (PlatformNotSupportedException) { caught++; }
                try { _ = missing.MethodImplementationFlags; } catch (PlatformNotSupportedException) { caught++; }
                try { _ = missing.IsSecurityCritical; } catch (PlatformNotSupportedException) { caught++; }
                try { _ = missing.IsSecuritySafeCritical; } catch (PlatformNotSupportedException) { caught++; }
                try { _ = missing.IsSecurityTransparent; } catch (PlatformNotSupportedException) { caught++; }
                try { MethodBase.GetCurrentMethod(); } catch (PlatformNotSupportedException) { caught++; }
                try { missing.GetMethodBody(); } catch (PlatformNotSupportedException) { caught++; }
                if (caught != 10) return 7;
                Expression<Func<int, int>> expr = value => Identity<int>(value);
                MethodBase runtime = ((MethodCallExpression)expr.Body).Method;
                if (!runtime.IsStatic || !runtime.IsPublic || !runtime.IsGenericMethod || runtime.IsGenericMethodDefinition || !runtime.IsConstructedGenericMethod || runtime.ContainsGenericParameters) return 8;
                try { _ = runtime.CallingConvention; return 9; } catch (PlatformNotSupportedException) { }
                var methodInfo = new MissingMethod();
                if (methodInfo.MemberType != MemberTypes.Method || ctor.MemberType != MemberTypes.Constructor || ctor.GetGenericArguments().Length != 0) return 14;
                if (ConstructorInfo.ConstructorName != ".ctor" || ConstructorInfo.TypeConstructorName != ".cctor") return 15;
                var missingCalls = 0;
                try { _ = methodInfo.ReturnParameter; } catch (PlatformNotSupportedException) { missingCalls++; }
                try { methodInfo.GetGenericArguments(); } catch (PlatformNotSupportedException) { missingCalls++; }
                try { methodInfo.GetGenericMethodDefinition(); } catch (PlatformNotSupportedException) { missingCalls++; }
                try { methodInfo.MakeGenericMethod(typeof(int)); } catch (PlatformNotSupportedException) { missingCalls++; }
                try { methodInfo.GetBaseDefinition(); } catch (PlatformNotSupportedException) { missingCalls++; }
                try { _ = methodInfo.ReturnTypeCustomAttributes; } catch (PlatformNotSupportedException) { missingCalls++; }
                try { methodInfo.CreateDelegate(typeof(Action)); } catch (PlatformNotSupportedException) { missingCalls++; }
                try { methodInfo.CreateDelegate(typeof(Action), null); } catch (PlatformNotSupportedException) { missingCalls++; }
                try { ctor.Invoke(null); } catch (PlatformNotSupportedException) { missingCalls++; }
                if (missingCalls != 9) return 16;
                var suppliedCtor = new SuppliedCtor();
                ConstructorInfo constructor = suppliedCtor;
                if (!ReferenceEquals(constructor.Invoke(args), args) || !ReferenceEquals(suppliedCtor.Args, args) || suppliedCtor.Flags != BindingFlags.Default || suppliedCtor.Binder != null || suppliedCtor.Culture != null) return 17;
                ConstructorInfo sameCtor = suppliedCtor;
                if (constructor != sameCtor || constructor == ctor || constructor == null || null == constructor || !constructor.Equals((object)sameCtor) || constructor.GetHashCode() != sameCtor.GetHashCode()) return 18;
                var body = new Body();
                if (body.InitLocals || body.MaxStackSize != 0 || body.LocalSignatureMetadataToken != 0 || body.GetILAsByteArray() != null) return 10;
                var defaults = 0;
                try { _ = body.LocalVariables; } catch (ArgumentNullException e) { if (e.ParamName == "array") defaults++; }
                try { _ = body.ExceptionHandlingClauses; } catch (ArgumentNullException e) { if (e.ParamName == "array") defaults++; }
                var local = new Local();
                if (local.LocalType != typeof(int) || local.LocalIndex != 3 || !local.IsPinned || local.ToString() != typeof(int).ToString() + " (3) (pinned)") return 11;
                var clause = new Clause();
                if (clause.Flags != ExceptionHandlingClauseOptions.Clause || clause.TryOffset != 0 || clause.TryLength != 0 || clause.HandlerOffset != 0 || clause.HandlerLength != 0 || clause.CatchType != null) return 12;
                try { _ = clause.FilterOffset; } catch (InvalidOperationException) { defaults++; }
                return defaults == 3 ? input + 1 : 13;
            }
            public static T Identity<T>(T value) => value;
            private sealed class MissingMethod : MethodInfo { }
            private sealed class SuppliedCtor : ConstructorInfo
            {
                public object[] Args; public BindingFlags Flags; public Binder Binder; public CultureInfo Culture;
                public override object Invoke(BindingFlags flags, Binder binder, object[] args, CultureInfo culture)
                { Args = args; Flags = flags; Binder = binder; Culture = culture; return args; }
            }
            private sealed class Body : MethodBody { }
            private sealed class Clause : ExceptionHandlingClause { }
            private sealed class Local : LocalVariableInfo
            {
                public override Type LocalType => typeof(int);
                public override int LocalIndex => 3;
                public override bool IsPinned => true;
            }
            private sealed class Missing : MethodBase { }
            private sealed class Ctor : ConstructorInfo
            {
                public bool Static;
                public override MethodAttributes Attributes => MethodAttributes.RTSpecialName | (Static ? MethodAttributes.Static : 0);
            }
            private sealed class Provider : MethodBase
            {
                public MethodAttributes Flags; public object Target; public object[] Args; public BindingFlags CallFlags; public Binder Binder; public CultureInfo Culture;
                public override MethodAttributes Attributes => Flags;
                public override MethodImplAttributes GetMethodImplementationFlags() => MethodImplAttributes.NoInlining;
                public override object Invoke(object target, BindingFlags flags, Binder binder, object[] args, CultureInfo culture)
                { Target = target; CallFlags = flags; Binder = binder; Args = args; Culture = culture; return args[0]; }
            }
        }
        """;
}
