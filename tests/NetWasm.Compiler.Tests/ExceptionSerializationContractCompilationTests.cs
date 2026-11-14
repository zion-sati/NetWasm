using NetWasm.Compiler.ComponentModel;
using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

public sealed class ExceptionSerializationContractCompilationTests
{
    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void BaseContractsAndUserOverridesRemainUsable(WasmTarget target)
    {
        using var assets = TestAssets.Create();
        var executor = (ICompilationScenarioExecutor)new CompilationScenarioExecutor(assets);
        Assert.Equal(42, executor.Execute(new CompilationScenario(
            "ExceptionSerializationFixture", Source, "ExceptionSerializationFixture.EntryPoint", false, target, 41, [])
        { Optimization = FinalWasmOptimization.None }));
    }

    private const string Source = """
        #pragma warning disable SYSLIB0011, SYSLIB0050, SYSLIB0051, CS0672
        using System;
        using System.ComponentModel;
        using System.Runtime.InteropServices;
        using System.Runtime.Serialization;
        namespace ExceptionSerializationFixture;
        public static class EntryPoint
        {
            public static int Run(int input)
            {
                if (Portable(input) != 42) return 1;
                var caught = 0;
                ISerializable error = new Exception("ordinary");
                try { error.GetObjectData(null, default); return 2; }
                catch (PlatformNotSupportedException ex) { if (!ex.Message.Contains("Exception.GetObjectData")) return 3; caught++; }
                Exception native = new Win32Exception(123, "supplied");
                try { native.GetObjectData(null, default); return 4; }
                catch (PlatformNotSupportedException ex) { if (!ex.Message.Contains("Win32Exception")) return 5; caught++; }
                try { ((ISerializable)native).GetObjectData(null, default); return 6; }
                catch (PlatformNotSupportedException ex) { if (!ex.Message.Contains("Win32Exception")) return 7; caught++; }
                try { _ = new DeserializeException(null, default); return 8; } catch (PlatformNotSupportedException) { caught++; }
                try { _ = new DeserializeSystemException(null, default); return 9; } catch (PlatformNotSupportedException) { caught++; }
                try { _ = new DeserializeExternalException(null, default); return 10; } catch (PlatformNotSupportedException) { caught++; }
                try { _ = new DeserializeCOMException(null, default); return 11; } catch (PlatformNotSupportedException) { caught++; }
                var custom = new CustomException();
                try { custom.Subscribe(); return 12; } catch (PlatformNotSupportedException) { caught++; }
                try { custom.Unsubscribe(); return 13; } catch (PlatformNotSupportedException) { caught++; }
                return caught == 9 ? input + 1 : 14;
            }
            public static int Portable(int input)
            {
                var marker = new object();
                var custom = new CustomException();
                Exception asBase = custom;
                asBase.GetObjectData(null, new StreamingContext(StreamingContextStates.Other, marker));
                if (!ReferenceEquals(custom.Seen, marker)) return 15;
                custom.Seen = null;
                ((ISerializable)asBase).GetObjectData(null, new StreamingContext(StreamingContextStates.Other, marker));
                if (!ReferenceEquals(custom.Seen, marker) || custom.Message != "custom") return 16;
                var state = new State();
                ((ISafeSerializationData)state).CompleteDeserialization(marker);
                if (!ReferenceEquals(state.Seen, marker)) return 17;
                try { throw custom; } catch (Exception ex) { if (!ReferenceEquals(ex, custom)) return 18; }
                return input + 1;
            }
        }
        public sealed class CustomException : Exception
        {
            public object Seen;
            public CustomException() : base("custom") { }
            public override void GetObjectData(SerializationInfo info, StreamingContext context) { Seen = context.Context; }
            public void Subscribe() { SerializeObjectState += OnSerialize; }
            public void Unsubscribe() { SerializeObjectState -= OnSerialize; }
            private void OnSerialize(object sender, SafeSerializationEventArgs args) { }
        }
        public sealed class State : ISafeSerializationData
        {
            public object Seen;
            public void CompleteDeserialization(object deserialized) { Seen = deserialized; }
        }
        public sealed class DeserializeException : Exception
        { public DeserializeException(SerializationInfo info, StreamingContext context) : base(info, context) { } }
        public sealed class DeserializeSystemException : SystemException
        { public DeserializeSystemException(SerializationInfo info, StreamingContext context) : base(info, context) { } }
        public sealed class DeserializeExternalException : ExternalException
        { public DeserializeExternalException(SerializationInfo info, StreamingContext context) : base(info, context) { } }
        public sealed class DeserializeCOMException : COMException
        { public DeserializeCOMException(SerializationInfo info, StreamingContext context) : base(info, context) { } }
        """;
}
