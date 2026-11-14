using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

public sealed class NativeSynchronizationExceptionCompilationTests
{
    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void SynchronizationExceptionsPreservePayloadAndCatchIdentity(WasmTarget target)
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSource("NativeExceptionFixture", Source);
        var result = NetWasmCompiler.Compile(new(assembly, [assets.CoreLib], "NativeExceptionFixture.EntryPoint",
            "Run", [new("run", "NativeExceptionFixture.EntryPoint", "Run")], Target: target,
            WitPath: Path.Combine(assets.Root, "wit", "netwasm-platform-1.0.0"),
            WitWorld: "netwasm:platform/platform@1.0.0"));
        CompilerTestSupport.ValidateWithNode(result.ApplicationModule, assets.Directory);
        Assert.Equal(42, CompilerTestSupport.ExecuteWithStandardWasiNode(result.ApplicationModule,
            assets.Directory, 41, target, result.StaticDataEnd));
    }

    private const string Source = """
        using System;
        using System.Threading;
        using System.Runtime.Serialization;
        #pragma warning disable SYSLIB0050, SYSLIB0051
        namespace NativeExceptionFixture;
        public static class EntryPoint
        {
            public static int Run(int input)
            {
                var result = Portable(input); if (result != 42) return result;
                try { new SerializedAbandoned(); return 30; } catch (PlatformNotSupportedException) { }
                try { new SerializedOpen(); return 31; } catch (PlatformNotSupportedException) { }
                return input + 1;
            }
            public static int Portable(int input)
            {
                var inner = new InvalidOperationException("inner");
                using var unrelated = new DummyHandle();
                var errors = new[]
                {
                    new AbandonedMutexException(),
                    new AbandonedMutexException((string?)null),
                    new AbandonedMutexException(null, inner),
                    new AbandonedMutexException(7, null),
                    new AbandonedMutexException("located", -7, unrelated),
                    new AbandonedMutexException("wrapped", inner, 4, null)
                };
                for (var i = 0; i < errors.Length; i++)
                {
                    var error = errors[i];
                    if (error.HResult != unchecked((int)0x8013152D) || error.Mutex != null) return 1;
                    if (i < 3 && (error.MutexIndex != -1 || string.IsNullOrEmpty(error.Message))) return 2;
                    try { throw error; } catch (SystemException caught) { if (!ReferenceEquals(caught, error)) return 3; }
                }
                if (errors[3].MutexIndex != 7 || errors[4].MutexIndex != -7 || errors[5].MutexIndex != 4) return 4;
                if (errors[4].Message != "located" || errors[5].Message != "wrapped" || !ReferenceEquals(errors[2].InnerException, inner) || !ReferenceEquals(errors[5].InnerException, inner)) return 5;
                if (errors[0].Message != errors[1].Message || errors[0].Message != errors[2].Message) return 6;
                var openErrors = new[] { new WaitHandleCannotBeOpenedException(), new WaitHandleCannotBeOpenedException(null), new WaitHandleCannotBeOpenedException("named", inner) };
                foreach (var error in openErrors)
                {
                    if (error.HResult != unchecked((int)0x8013152C) || string.IsNullOrEmpty(error.Message)) return 7;
                    try { throw error; } catch (ApplicationException caught) { if (!ReferenceEquals(caught, error)) return 8; }
                }
                if (openErrors[0].Message != openErrors[1].Message || openErrors[2].Message != "named" || !ReferenceEquals(openErrors[2].InnerException, inner)) return 9;
                return input + 1;
            }
        }
        public sealed class DummyHandle : WaitHandle { }
        public sealed class SerializedAbandoned : AbandonedMutexException
        {
            public SerializedAbandoned() : base(null!, default(StreamingContext)) { }
        }
        public sealed class SerializedOpen : WaitHandleCannotBeOpenedException
        {
            public SerializedOpen() : base(null!, default(StreamingContext)) { }
        }
        """;
}
