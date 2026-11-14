using NetWasm.Compiler.ComponentModel;
using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

public sealed class SerializationContractCompilationTests
{
    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void PortableContextAndUnsupportedFormatterContracts(WasmTarget target)
    {
        using var assets = TestAssets.Create();
        var executor = (ICompilationScenarioExecutor)new CompilationScenarioExecutor(assets);
        Assert.Equal(42, executor.Execute(new CompilationScenario(
            "SerializationFixture", Source, "SerializationFixture.EntryPoint", false, target, 41, [])
        { Optimization = FinalWasmOptimization.None }));
    }

    private const string Source = """
        #pragma warning disable SYSLIB0050, SYSLIB0051, CS0672
        using System;
        using System.IO;
        using System.Runtime.Serialization;
        namespace SerializationFixture;
        public static class EntryPoint
        {
            public static int Run(int input)
            {
                if (Portable(input) != 42) return 1;
                var caught = 0;
                try { _ = new SerializationInfo(typeof(EntryPoint), null); return 2; }
                catch (PlatformNotSupportedException ex) { if (!ex.Message.Contains("SerializationInfo")) return 3; caught++; }
                try { _ = new SerializationInfo(typeof(EntryPoint), null, false); return 4; }
                catch (PlatformNotSupportedException) { caught++; }
                // FileSystemInfo's formatter members are upstream's, which throw PlatformNotSupportedException with
                // the default message.
                try { _ = new FileEntry(null, default); return 5; }
                catch (PlatformNotSupportedException) { caught++; }
                ISerializable file = new FileEntry();
                try { file.GetObjectData(null, default); return 7; }
                catch (PlatformNotSupportedException) { caught++; }
                return caught == 4 ? input + 1 : 9;
            }
            public static int Portable(int input)
            {
                var marker = new object();
                var context = new StreamingContext(StreamingContextStates.File | StreamingContextStates.Persistence, marker);
                var same = new StreamingContext(StreamingContextStates.File | StreamingContextStates.Persistence, marker);
                var other = new StreamingContext(context.State, new object());
                if (!ReferenceEquals(context.Context, marker) || !context.Equals(same) || context.Equals(other) || context.Equals(null)) return 10;
                if (context.GetHashCode() != same.GetHashCode() || context.GetHashCode() != (int)context.State) return 11;
                var noContext = new StreamingContext(StreamingContextStates.All);
                if (noContext.Context != null || (int)noContext.State != 255) return 12;
                var custom = new Custom();
                ISerializable contract = custom;
                contract.GetObjectData(null, context);
                return ReferenceEquals(custom.Seen, marker) ? input + 1 : 13;
            }
        }
        public sealed class Custom : ISerializable
        {
            public object Seen;
            public void GetObjectData(SerializationInfo info, StreamingContext context) { Seen = context.Context; }
        }
        public sealed class FileEntry : FileSystemInfo
        {
            public FileEntry() { FullPath = "/file"; OriginalPath = "file"; }
            public FileEntry(SerializationInfo info, StreamingContext context) : base(info, context) { }
            public override string Name => "file";
            public override bool Exists => false;
            public override void Delete() { }
        }
        """;
}
