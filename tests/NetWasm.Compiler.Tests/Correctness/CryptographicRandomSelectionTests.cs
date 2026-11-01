namespace NetWasm.Compiler.Tests.Correctness;

public sealed class CryptographicRandomSelectionTests(CorrectnessTestRunner runner)
{
    [Fact]
    public void SelectionContractsHoldAcrossTargetsAndForms() =>
        runner.Run(new("RandomSelectionContracts", "RandomSelectionContracts", Source, [42])
        {
            ExpectedReturnValue = 42,
            RequiresReactor = true,
            CaptureCompilerDiagnostics = true,
            // The linked corpus host currently exposes get-random-u64 only.
            // Use its existing simulated byte provider until shared linked-host
            // support for the secure random byte import is available.
            // Scripted source-linked tests own large-choice and shuffle rejection
            // cases: the simulator's incrementing bytes can starve those samplers.
            Matrix = new(CorpusMatrixProfile.Extended, Backend: CorpusExecutionBackend.Simulated),
            SameSourceReason = "Random values are checked by membership, bounds and permutation contracts; exact entropy is qualified in the scripted source-linked tests.",
        });

    private const string Source = """
        using System;
        using System.Security.Cryptography;
        namespace RandomSelectionContracts;

        public static class EntryPoint
        {
            public static int Trace() => 0;

            public static int Run(int input)
            {
                foreach (int count in new int[] { 1, 3, 4, 256 })
                {
                    var choices = new int[count];
                    for (int i = 0; i < count; i++) choices[i] = i + 10;
                    int[] items = RandomNumberGenerator.GetItems<int>(choices, 513);
                    if (items.Length != 513) return 1;
                    foreach (int item in items) if (item < 10 || item >= count + 10) return 2;
                    int[] guarded = [-1, 0, 0, 0, -1];
                    RandomNumberGenerator.GetItems<int>(choices, guarded.AsSpan(1, 3));
                    if (guarded[0] != -1 || guarded[4] != -1) return 3;
                    for (int i = 1; i < 4; i++) if (guarded[i] < 10 || guarded[i] >= count + 10) return 4;
                }
                string selected = RandomNumberGenerator.GetString("abc", 513);
                if (selected.Length != 513) return 5;
                foreach (char c in selected) if (c != 'a' && c != 'b' && c != 'c') return 6;

                foreach (int length in new int[] { 0, 1, 2, 127, 128, 129, 130 })
                {
                    for (int lower = 0; lower < 2; lower++)
                    {
                        bool lowercase = lower != 0;
                        string hex = RandomNumberGenerator.GetHexString(length, lowercase);
                        if (hex.Length != length) return 7;
                        foreach (char c in hex) if (!IsHex(c, lowercase)) return 8;
                        var guarded = new char[length + 2];
                        guarded[0] = '!'; guarded[length + 1] = '!';
                        RandomNumberGenerator.GetHexString(guarded.AsSpan(1, length), lowercase);
                        if (guarded[0] != '!' || guarded[length + 1] != '!') return 9;
                        for (int i = 1; i <= length; i++) if (!IsHex(guarded[i], lowercase)) return 10;
                    }
                }
                foreach (int length in new int[] { 0, 1, 2 })
                {
                    var values = new int[length];
                    for (int i = 0; i < length; i++) values[i] = i;
                    RandomNumberGenerator.Shuffle<int>(values);
                    var seen = new bool[length];
                    foreach (int value in values)
                    {
                        if (value < 0 || value >= length || seen[value]) return 11;
                        seen[value] = true;
                    }
                }
                return input;
            }

            private static bool IsHex(char value, bool lowercase) =>
                value >= '0' && value <= '9' ||
                (lowercase ? value >= 'a' && value <= 'f' : value >= 'A' && value <= 'F');
        }
        """;
}
