using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

public sealed class EventInfoContractCompilationTests
{
    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void EventMetadataPreservesForwardingAndRuntimeFallback(WasmTarget target)
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSource("EventFixture", Source);
        var result = NetWasmCompiler.Compile(new(assembly, [assets.CoreLib], "EventFixture.EntryPoint",
            "Run", [new("run", "EventFixture.EntryPoint", "Run")], Target: target,
            WitPath: Path.Combine(assets.Root, "wit", "netwasm-platform-1.0.0"),
            WitWorld: "netwasm:platform/platform@1.0.0"));
        CompilerTestSupport.ValidateWithNode(result.ApplicationModule, assets.Directory);
        Assert.Equal(42, CompilerTestSupport.ExecuteWithStandardWasiNode(result.ApplicationModule,
            assets.Directory, 41, target, result.StaticDataEnd));
    }

    private const string Source = """
        using System;
        using System.Reflection;
        namespace EventFixture;
        public static class EntryPoint
        {
            public static int Run(int input)
            {
                MethodInfo first = new Method();
                MethodInfo sameMethod = first;
                MethodInfo otherMethod = new Method();
                MethodInfo missing = null;
                if (first != sameMethod || first == otherMethod || first == null || null == first || missing != null) return 10;
                if (!first.Equals((object)sameMethod) || first.Equals(null) || first.GetHashCode() != sameMethod.GetHashCode()) return 11;
                MethodInfo custom = new EqualMethod();
                if (custom != otherMethod || !custom.Equals(otherMethod)) return 12;
                var item = new Event();
                if (item.MemberType != MemberTypes.Event || !item.IsSpecialName) return 1;
                if (item.GetAddMethod() != null || !ReferenceEquals(item.AddMethod, item.Method)) return 2;
                if (item.GetRemoveMethod() != null || !ReferenceEquals(item.RemoveMethod, item.Method)) return 3;
                if (item.GetRaiseMethod() != null || !ReferenceEquals(item.RaiseMethod, item.Method)) return 4;
                if (item.EventHandlerType != typeof(Action) || !item.IsMulticast) return 5;
                EventInfo same = item;
                if (item != same || !item.Equals((object)same) || item == null || null == item) return 6;
                if (item.GetHashCode() != same.GetHashCode()) return 7;
                item.Method.DelegateParameter = false;
                if (item.EventHandlerType != null || item.IsMulticast) return 8;
                var caught = 0;
                try { item.GetOtherMethods(); } catch (PlatformNotSupportedException e) { if (e.Message.Length > 0) caught++; }
                try { item.AddEventHandler(null, null); } catch (PlatformNotSupportedException e) { if (e.Message.Length > 0) caught++; }
                try { item.RemoveEventHandler(null, null); } catch (PlatformNotSupportedException e) { if (e.Message.Length > 0) caught++; }
                if (caught != 3) return 9;
                return input + 1;
            }
            private sealed class Event : EventInfo
            {
                public readonly Method Method = new Method();
                public override EventAttributes Attributes => EventAttributes.SpecialName;
                public override MethodInfo GetAddMethod(bool nonPublic) => nonPublic ? Method : null;
                public override MethodInfo GetRemoveMethod(bool nonPublic) => nonPublic ? Method : null;
                public override MethodInfo GetRaiseMethod(bool nonPublic) => nonPublic ? Method : null;
            }
            private sealed class EqualMethod : MethodInfo
            {
                public override bool Equals(object other) => other is MethodInfo;
                public override int GetHashCode() => 1;
            }
            private sealed class Method : MethodInfo
            {
                public bool DelegateParameter = true;
                public override ParameterInfo[] GetParameters() => new[] { new Parameter(typeof(int)), new Parameter(DelegateParameter ? typeof(Action) : typeof(string)) };
            }
            private sealed class Parameter : ParameterInfo
            {
                private readonly Type type;
                public Parameter(Type type) { this.type = type; }
                public override Type ParameterType => type;
            }
        }
        """;
}
