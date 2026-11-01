namespace NetWasm.Compiler.Tests.Correctness;

public sealed class CryptographicRandomTests(CorrectnessTestRunner runner)
{
    [Fact]
    public void ArgumentAndSubclassContractsMatchDesktop() =>
        runner.Run(new("RandomArgumentContracts", "RandomArgumentContracts", ArgumentSource, [42])
        {
            ExpectedReturnValue = 42,
            RequiresReactor = true,
            Matrix = new(CorpusMatrixProfile.Extended, Backend: CorpusExecutionBackend.Simulated),
        });

    [Fact]
    public void PublicBuffersAndRangesPreserveFrameworkContracts() =>
        runner.Run(new("PublicRandomContracts", "PublicRandomContracts", Source, [42])
        {
            ExpectedReturnValue = 42,
            RequiresReactor = true,
            // The semantic oracle supplies deterministic test entropy. The SDK
            // consumers qualify the real secure provider and capability boundary.
            Matrix = new(CorpusMatrixProfile.Extended, Backend: CorpusExecutionBackend.Simulated),
        });

    internal const string ArgumentSource = """
        using System;
        using System.Security.Cryptography;
        namespace RandomArgumentContracts;

        public sealed class ArrayRandom : RandomNumberGenerator
        {
            public int Calls;
            public int Disposals;
            public override void GetBytes(byte[] bytes)
            {
                Calls++;
                Array.Fill(bytes, (byte)17);
            }
            protected override void Dispose(bool disposing)
            {
                if (disposing) Disposals++;
                base.Dispose(disposing);
            }
        }

        public static class EntryPoint
        {
            public static int Trace() => 0;
            public static int Run(int input)
            {
                using var builtin = RandomNumberGenerator.Create();
                var custom = new ArrayRandom();
                for (int kind = 0; kind < 2; kind++)
                {
                    RandomNumberGenerator random = kind == 0 ? builtin : custom;
                    int observed = 0;
                    try { random.GetBytes(null!, -1, -1); }
                    catch (ArgumentNullException ex) { if (ex.ParamName == "data") observed |= 1; }
                    try { random.GetBytes(Array.Empty<byte>(), -1, -1); }
                    catch (ArgumentOutOfRangeException ex) { if (ex.ParamName == "offset") observed |= 2; }
                    try { random.GetBytes(Array.Empty<byte>(), 1, -1); }
                    catch (ArgumentOutOfRangeException ex) { if (ex.ParamName == "count") observed |= 4; }
                    try { random.GetBytes(new byte[1], int.MaxValue, int.MaxValue); }
                    catch (ArgumentException) { observed |= 8; }
                    if (observed != 15) return 100 + kind * 16 + observed;
                }
                int invalidRanges = 0;
                try { _ = RandomNumberGenerator.GetInt32(0); }
                catch (ArgumentOutOfRangeException ex) { if (ex.ParamName == "toExclusive") invalidRanges |= 1; }
                try { _ = RandomNumberGenerator.GetInt32(7, 7); }
                catch (ArgumentException) { invalidRanges |= 2; }
                try { _ = RandomNumberGenerator.GetInt32(8, 7); }
                catch (ArgumentException) { invalidRanges |= 4; }
                try { _ = RandomNumberGenerator.GetBytes(-1); }
                catch (ArgumentOutOfRangeException ex) { if (ex.ParamName == "count") invalidRanges |= 8; }
                if (invalidRanges != 15 || custom.Calls != 0) return 2;
                byte[] guarded = [90, 90, 90, 90];
                custom.GetBytes(guarded, 1, 2);
                if (guarded[0] != 90 || guarded[1] != 17 || guarded[2] != 17 || guarded[3] != 90) return 3;
                Array.Fill(guarded, (byte)90);
                custom.GetBytes(guarded.AsSpan(1, 2));
                if (guarded[0] != 90 || guarded[1] != 17 || guarded[2] != 17 || guarded[3] != 90) return 4;
                bool baseNonzeroThrows = false;
                try { custom.GetNonZeroBytes(Span<byte>.Empty); }
                catch (NotImplementedException) { baseNonzeroThrows = true; }
                custom.Dispose();
                custom.Dispose();
                if (!baseNonzeroThrows || custom.Calls != 2 || custom.Disposals != 2) return 5;
                return input;
            }
        }
        """;

    internal const string Source = """
        using System;
        using System.Security.Cryptography;
        namespace PublicRandomContracts;

        public static class EntryPoint
        {
            public static int Trace() => 0;

            public static int Run(int input)
            {
                using var random = RandomNumberGenerator.Create();
                var bytes = new byte[1026];
                bytes[0] = 77;
                bytes[1025] = 99;
                random.GetBytes(bytes, 1, 1024);
                if (bytes[0] != 77 || bytes[1025] != 99) return 1;
                random.GetBytes(bytes.AsSpan(1, 1024));
                if (bytes[0] != 77 || bytes[1025] != 99) return 2;
                RandomNumberGenerator.Fill(bytes.AsSpan(1, 1024));
                if (bytes[0] != 77 || bytes[1025] != 99) return 3;
                random.GetNonZeroBytes(bytes.AsSpan(1, 1024));
                if (bytes[0] != 77 || bytes[1025] != 99) return 4;
                for (int index = 1; index < 1025; index++)
                    if (bytes[index] == 0) return 5;
                random.GetNonZeroBytes(bytes);
                foreach (byte value in bytes) if (value == 0) return 6;
                random.GetBytes(bytes);
                Span<byte> stack = stackalloc byte[16];
                random.GetBytes(stack);
                RandomNumberGenerator.Fill(stack);
                random.GetNonZeroBytes(stack);
                foreach (byte value in stack) if (value == 0) return 7;
                RandomNumberGenerator.Fill(Span<byte>.Empty);
                random.GetBytes(Array.Empty<byte>());
                random.GetBytes(Span<byte>.Empty);
                random.GetBytes(bytes, bytes.Length, 0);
                random.GetNonZeroBytes(Array.Empty<byte>());
                random.GetNonZeroBytes(Span<byte>.Empty);
                if (RandomNumberGenerator.GetBytes(0).Length != 0 ||
                    RandomNumberGenerator.GetBytes(17).Length != 17) return 8;
                if (RandomNumberGenerator.GetInt32(1) != 0 ||
                    RandomNumberGenerator.GetInt32(-7, -6) != -7) return 9;
                for (int index = 0; index < 128; index++)
                {
                    int small = RandomNumberGenerator.GetInt32(3);
                    int signed = RandomNumberGenerator.GetInt32(-7, 9);
                    int wide = RandomNumberGenerator.GetInt32(int.MinValue, int.MaxValue);
                    if (small < 0 || small >= 3 || signed < -7 || signed >= 9 || wide == int.MaxValue) return 10;
                }
                random.Dispose();
                random.GetBytes(stack);
                return input;
            }
        }
        """;
}
