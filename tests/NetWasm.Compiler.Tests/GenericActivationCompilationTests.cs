using NetWasm.Compiler.ComponentModel;
using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

public sealed class GenericActivationCompilationTests
{
    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void ClosedActivationPreservesConstructorAndFailureSemantics(WasmTarget target)
    {
        using var assets = TestAssets.Create();
        var executor = (ICompilationScenarioExecutor)new CompilationScenarioExecutor(assets);
        var result = executor.Execute(new CompilationScenario(
            "ActivationFixture", Source, "ActivationFixture.EntryPoint", false, target, 0, [])
        { Optimization = FinalWasmOptimization.None });
        Assert.Equal(42, result);
    }

    private const string Source = """
        using System;
        using System.Reflection;
        namespace ActivationFixture;
        public static class EntryPoint { public static int Run(int input) {
        var passed = 0;
        try { _ = Activator.CreateInstance<IFoo>(); }
        catch (MissingMethodException) { passed++; }
        try { _ = Activator.CreateInstance<AbstractFoo>(); }
        catch (MissingMethodException) { passed++; }
        try { _ = Activator.CreateInstance<PrivateFoo>(); }
        catch (MissingMethodException) { passed++; }
        try { _ = Activator.CreateInstance<ThrowingFoo>(); }
        catch (TargetInvocationException error) when (error.InnerException is InvalidOperationException) { passed++; }
        if (Activator.CreateInstance<int?>() is null) passed++;
        if (Activator.CreateInstance<ConstructedValue>().Value == 42) passed++;
        if (Activator.CreateInstance<int>() == 0) passed++;
        if (new Lazy<IFoo>(() => new Foo()).Value.Value == 42) passed++;
        if (((Func<Foo>)Activator.CreateInstance<Foo>)().Value == 42) passed++;
        return passed == 9 ? 42 : -passed;
        
        }
        }
        interface IFoo { int Value { get; } }
        class Foo : IFoo { public Foo() {} public int Value => 42; }
        abstract class AbstractFoo { public AbstractFoo() {} }
        class PrivateFoo { private PrivateFoo() {} }
        class ThrowingFoo { public ThrowingFoo() => throw new InvalidOperationException(); }
        struct ConstructedValue { public int Value; public ConstructedValue() => Value = 42; }
        """;
}
