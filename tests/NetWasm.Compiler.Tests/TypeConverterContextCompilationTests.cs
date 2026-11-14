using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

public sealed class TypeConverterContextCompilationTests
{
    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void ContextAndClosedDescriptorsRetainTheirContracts(WasmTarget target)
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSource("ConverterFixture", Source);
        var result = NetWasmCompiler.Compile(new(assembly, [assets.CoreLib], "ConverterFixture.EntryPoint",
            "Run", [new("run", "ConverterFixture.EntryPoint", "Run")], Target: target,
            WitPath: Path.Combine(assets.Root, "wit", "netwasm-platform-1.0.0"),
            WitWorld: "netwasm:platform/platform@1.0.0"));
        CompilerTestSupport.ValidateWithNode(result.ApplicationModule, assets.Directory);
        Assert.Equal(42, CompilerTestSupport.ExecuteWithStandardWasiNode(result.ApplicationModule,
            assets.Directory, 41, target, result.StaticDataEnd));
    }

    private const string Source = """
        using System;
        using System.Collections;
        using System.ComponentModel;
        using System.ComponentModel.Design.Serialization;
        using System.Globalization;
        using System.Linq.Expressions;
        namespace ConverterFixture;
        public sealed class Context : ITypeDescriptorContext
        {
            public int Changes;
            public IContainer Container => null;
            public object Instance => this;
            public PropertyDescriptor PropertyDescriptor => null;
            public bool OnComponentChanging() => true;
            public void OnComponentChanged() { Changes++; }
            public object GetService(Type type) => type == typeof(Context) ? this : null;
        }
        public sealed class Converter : TypeConverter
        {
            public ITypeDescriptorContext Seen;
            public CultureInfo Culture;
            public Attribute[] Filter;
            public override bool CanConvertFrom(ITypeDescriptorContext context, Type type) { Seen = context; return type == typeof(string); }
            public override object ConvertFrom(ITypeDescriptorContext context, CultureInfo culture, object value) { Seen = context; Culture = culture; return value; }
            public override object ConvertTo(ITypeDescriptorContext context, CultureInfo culture, object value, Type type) { Seen = context; Culture = culture; return value; }
            public override PropertyDescriptorCollection GetProperties(ITypeDescriptorContext context, object value, Attribute[] attributes) { Seen = context; Filter = attributes; return PropertyDescriptorCollection.Empty; }
            public override StandardValuesCollection GetStandardValues(ITypeDescriptorContext context) { Seen = context; return new StandardValuesCollection(new object[] { 42 }); }
            public override object CreateInstance(ITypeDescriptorContext context, IDictionary values) { Seen = context; return values; }
        }
        public static class EntryPoint
        {
            public static int Increment(int value) => value + 1;
            public static int Run(int input)
            {
                var concrete = new Context(); ITypeDescriptorContext context = concrete;
                if (!ReferenceEquals(context.Instance, context) || context.Container != null || context.PropertyDescriptor != null || !context.OnComponentChanging()) return 1;
                context.OnComponentChanged(); if (concrete.Changes != 1 || !ReferenceEquals(context.GetService(typeof(Context)), context) || context.GetService(typeof(string)) != null) return 2;
                var converter = new Converter();
                if ((string)converter.ConvertFromString(context, "x") != "x" || converter.Seen != context || converter.Culture != CultureInfo.CurrentCulture) return 3;
                if ((string)converter.ConvertFromInvariantString(context, "x") != "x" || converter.Culture != CultureInfo.InvariantCulture) return 4;
                if (converter.ConvertToInvariantString(context, "y") != "y" || converter.Seen != context || converter.Culture != CultureInfo.InvariantCulture) return 5;
                if (converter.GetProperties(context, new object()) != PropertyDescriptorCollection.Empty || converter.Seen != context || converter.Filter.Length != 1 || converter.Filter[0] != BrowsableAttribute.Yes) return 6;
                if ((int)converter.GetStandardValues(context)[0] != 42 || converter.Seen != context) return 7;
                if (!converter.IsValid(context, "x") || converter.Seen != context || converter.Culture != CultureInfo.InvariantCulture) return 8;
                var values = new Hashtable(); if (!ReferenceEquals(converter.CreateInstance(context, values), values) || converter.Seen != context) return 9;
                Expression<Func<int, int>> expression = value => Increment(value);
                var method = ((MethodCallExpression)expression.Body).Method;
                var descriptor = new InstanceDescriptor(method, new object[] { 41 });
                if (descriptor.MemberInfo != method || !descriptor.IsComplete || descriptor.Arguments.Count != 1) return 10;
                var caught = 0;
                try { new InstanceDescriptor(method, new object[0]); } catch (ArgumentException) { caught++; }
                try { descriptor.Invoke(); } catch (PlatformNotSupportedException e) { if (!string.IsNullOrEmpty(e.Message)) caught++; }
                return caught == 2 ? input + 1 : 100 + caught;
            }
        }
        """;
}
