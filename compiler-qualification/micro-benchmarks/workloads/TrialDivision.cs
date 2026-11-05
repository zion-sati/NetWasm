using BenchmarkDotNet.Attributes;

namespace NetWasm.Workloads
{
    // Issue #166's prime search, from the fui-cs-demo prime worker: the largest prime below
    // 10,000,000 by trial division, testing each odd divisor with a remainder, and with the
    // demo's later single division a step.
    public class TrialDivision
    {
        private const int Limit = 10_000_000;

        [Benchmark]
        public int LargestPrimeRemainder()
        {
            var largest = 2;
            for (var candidate = 2; candidate < Limit; candidate++) if (IsPrimeDivRem(candidate)) largest = candidate;
            return largest;
        }

        [Benchmark]
        public int LargestPrimeOneDivision()
        {
            var largest = 2;
            for (var candidate = 2; candidate < Limit; candidate++) if (IsPrimeOneDivision(candidate)) largest = candidate;
            return largest;
        }

        private static bool IsPrimeDivRem(int value)
        {
            if (value < 2) return false;
            if (value == 2) return true;
            if ((value & 1) == 0) return false;
            for (var divisor = 3; divisor <= value / divisor; divisor += 2)
            {
                if (value % divisor == 0) return false;
            }
            return true;
        }

        private static bool IsPrimeOneDivision(int value)
        {
            if (value < 2) return false;
            if (value == 2) return true;
            if ((value & 1) == 0) return false;
            var divisor = 3;
            var quotient = value / divisor;
            while (divisor <= quotient)
            {
                if (quotient * divisor == value) return false;
                divisor += 2;
                quotient = value / divisor;
            }
            return true;
        }
    }
}
