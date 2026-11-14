using NetWasm.Compiler.ComponentModel;
using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

public sealed class MarshalByRefObjectCompilationTests
{
    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void CloningAndStreamInheritanceMatchDesktop(WasmTarget target)
    {
        using var assets = TestAssets.Create();
        var executor = (ICompilationScenarioExecutor)new CompilationScenarioExecutor(assets);
        Assert.Equal(42, executor.Execute(new CompilationScenario(
            "MarshalFixture", Source, "MarshalFixture.EntryPoint", false, target, 41, [])
        { Optimization = FinalWasmOptimization.None }));
    }

    private const string Source = """
        #pragma warning disable SYSLIB0010
        using System;
        using System.IO;
        namespace MarshalFixture;
        public static class EntryPoint
        {
            public static int Run(int input)
            {
                var original = new Sample { Number = input, Values = new int[] { 7 } };
                var copy = original.Clone(false);
                var identityCopy = original.Clone(true);
                if (ReferenceEquals(original, copy) || ReferenceEquals(original, identityCopy)) return 1;
                if (copy.Number != input || identityCopy.Number != input) return 2;
                if (!ReferenceEquals(original.Values, copy.Values) || !ReferenceEquals(original.Values, identityCopy.Values)) return 3;
                copy.Number = 12; copy.Values[0] = 9;
                if (original.Number != input || original.Values[0] != 9) return 4;
                MarshalByRefObject boxed = original;
                try { boxed.GetLifetimeService(); return 5; }
                catch (PlatformNotSupportedException ex) { if (!ex.Message.Contains("Remoting")) return 6; }
                try { boxed.InitializeLifetimeService(); return 7; }
                catch (PlatformNotSupportedException ex) { if (!ex.Message.Contains("Remoting")) return 8; }
                MarshalByRefObject overridden = new CustomLifetime();
                if ((int)overridden.InitializeLifetimeService() != 42) return 9;
                using var stream = new MemoryStream();
                MarshalByRefObject streamBase = stream;
                if (!ReferenceEquals(stream, streamBase)) return 10;
                stream.WriteByte(42); stream.Position = 0;
                if (((Stream)streamBase).ReadByte() != 42) return 11;
                using var reader = new StringReader("value");
                using var writer = new StringWriter();
                MarshalByRefObject readerBase = reader, writerBase = writer;
                ((TextWriter)writerBase).Write(((TextReader)readerBase).ReadToEnd());
                if (writer.ToString() != "value") return 12;
                return input + 1;
            }
        }
        public sealed class Sample : MarshalByRefObject
        {
            public int Number;
            public int[] Values;
            public Sample Clone(bool identity) => (Sample)MemberwiseClone(identity);
        }
        public sealed class CustomLifetime : MarshalByRefObject
        {
            [Obsolete]
            public override object InitializeLifetimeService() => 42;
        }
        """;
}
