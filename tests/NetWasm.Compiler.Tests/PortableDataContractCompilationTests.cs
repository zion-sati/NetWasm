using NetWasm.Compiler.ComponentModel;
using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

public sealed class PortableDataContractCompilationTests
{
    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void SerializationContextAndTransformInterfacePreserveContracts(WasmTarget target)
    {
        using var assets = TestAssets.Create();
        var executor = (ICompilationScenarioExecutor)new CompilationScenarioExecutor(assets);
        Assert.Equal(42, executor.Execute(new CompilationScenario(
            "PortableContractsFixture", Source, "PortableContractsFixture.EntryPoint", false, target, 0, [])
        { Optimization = FinalWasmOptimization.None }));
    }

    private const string Source = """
        #pragma warning disable SYSLIB0050
        using System;
        using System.Runtime.Serialization;
        using System.Security.Cryptography;
        namespace PortableContractsFixture;
        public sealed class Payload
        {
            public override bool Equals(object other) => other is Payload;
            public override int GetHashCode() => 1;
        }
        public sealed class CopyTransform : ICryptoTransform
        {
            public bool Disposed;
            public int InputBlockSize => 1;
            public int OutputBlockSize => 1;
            public bool CanTransformMultipleBlocks => true;
            public bool CanReuseTransform => true;
            public int TransformBlock(byte[] input, int offset, int count, byte[] output, int outputOffset)
            {
                Array.Copy(input, offset, output, outputOffset, count);
                return count;
            }
            public byte[] TransformFinalBlock(byte[] input, int offset, int count)
            {
                var output = new byte[count];
                Array.Copy(input, offset, output, 0, count);
                return output;
            }
            public void Dispose() => Disposed = true;
        }
        public class FixedBinder : SerializationBinder
        {
            public override Type BindToType(string assemblyName, string typeName) =>
                assemblyName == "fixture" && typeName == "record" ? typeof(Payload) : null;
        }
        public sealed class NamedBinder : FixedBinder
        {
            public override void BindToName(Type serializedType, out string assemblyName, out string typeName)
            {
                assemblyName = "fixture";
                typeName = "record";
            }
        }
        public static class EntryPoint
        {
            public static int Run(int input)
            {
                var payload = new Payload();
                var flags = StreamingContextStates.Clone | StreamingContextStates.Persistence;
                var context = new StreamingContext(flags, payload);
                if (!ReferenceEquals(context.Context, payload) || context.State != flags) return 1;
                if (!context.Equals(new StreamingContext(flags, payload))) return 2;
                if (context.Equals(new StreamingContext(flags, new Payload()))) return 3;
                if (context.Equals(new StreamingContext(StreamingContextStates.Other, payload))) return 4;
                if (context.Equals(null) || context.Equals(payload) || context.GetHashCode() != (int)flags) return 5;
                var empty = default(StreamingContext);
                if (empty.Context != null || (int)empty.State != 0 || empty.GetHashCode() != 0) return 6;
                if ((int)StreamingContextStates.All != 255 || (int)StreamingContextStates.CrossAppDomain != 128) return 7;
                var noContext = new StreamingContext(StreamingContextStates.All);
                if (noContext.Context != null || noContext.State != StreamingContextStates.All) return 8;
                var transform = new CopyTransform();
                ICryptoTransform contract = transform;
                var values = new byte[] { 19, 23 };
                var output = new byte[4];
                if (contract.InputBlockSize != 1 || contract.OutputBlockSize != 1 || !contract.CanReuseTransform || !contract.CanTransformMultipleBlocks) return 9;
                if (contract.TransformBlock(values, 0, 2, output, 1) != 2 || output[1] + output[2] != 42) return 10;
                var tail = contract.TransformFinalBlock(values, 1, 1);
                if (tail.Length != 1 || tail[0] != 23) return 11;
                contract.Dispose();
                if (!transform.Disposed) return 12;
                SerializationBinder binder = new FixedBinder();
                binder.BindToName(typeof(Payload), out var assemblyName, out var typeName);
                if (assemblyName != null || typeName != null) return 13;
                binder.BindToName(null, out assemblyName, out typeName);
                if (assemblyName != null || typeName != null) return 14;
                if (binder.BindToType("fixture", "record") != typeof(Payload) || binder.BindToType("unknown", "unknown") != null) return 15;
                binder = new NamedBinder();
                binder.BindToName(typeof(Payload), out assemblyName, out typeName);
                return assemblyName == "fixture" && typeName == "record" ? 42 : 16;
            }
        }
        """;
}
